using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Luna {
    internal static partial class Program {

        internal static ApplicationRuntime Runtime;
        internal static void InitializeRuntime(string[] args, Func<string, CancellationToken, Dictionary<string, object>> data = null,
            Func<string, CancellationToken, string> release = null, Func<string, long, CancellationToken, HttpWebResponse> download = null) {
            if (Runtime != null) return;
            Runtime = new ApplicationRuntime(new RuntimeOptions(Root, args.Contains("--offline"), args.Contains("--no-auto-update")), data, release, download);
            Runtime.DesktopAvailable = () => Desktop != null;
            Runtime.AppearanceChanged = () => { if (Desktop != null) Desktop.RefreshAppearance(); };
            Runtime.Storage.RequestBrowserCleanup = () => { if (Desktop != null) Desktop.RequestCacheCleanup(); };
        }
        static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string CacheDir = Path.Combine(Root, "cache");

        static readonly SemaphoreSlim Clients = new SemaphoreSlim(32, 32);

        static TcpListener Listener;
        static volatile bool Running = true;
        static int StopRequested;

        static string Address;
        static int Port;
        static bool DesktopProbe;
        static string ProbeToken = "";
        static long LastActivity = DateTime.UtcNow.Ticks;

        [STAThread]
        internal static void Main(string[] args) {
            try {
                InitializeRuntime(args);
                bool maintenanceProbe = args.Contains("--maintenance-probe");
                DesktopProbe = maintenanceProbe && args.Contains("--desktop-probe");
                int tokenPosition = Array.IndexOf(args, "--probe-token");
                if (maintenanceProbe && tokenPosition >= 0 && tokenPosition + 1 < args.Length) ProbeToken = args[tokenPosition + 1];
                if (ProbeToken != "" && !System.Text.RegularExpressions.Regex.IsMatch(ProbeToken, "^[a-f0-9]{32}$")) throw new ArgumentException("Invalid probe identity");
                if (!maintenanceProbe && Deployment.NeedsRecovery(Root)) { Deployment.StartWorker(Root, "recover", false, Process.GetCurrentProcess().Id); return; }

                bool noBrowser = args.Contains("--no-browser");
                int wanted = 17653;
                int position = Array.IndexOf(args, "--port");
                if (position >= 0 && position + 1 < args.Length) wanted = int.Parse(args[position + 1]);
                if ((wanted < 1024 || wanted > 65000) && !(maintenanceProbe && ProbeToken != "" && wanted == 0)) throw new Exception("端口必须在 1024–65000 之间。");
                string instance = DataFormat.Hash(Root + (Runtime.Options.Offline ? "offline" : "online") + wanted + ProbeToken).Substring(0, 20);
                using (var mutex = new Mutex(false, "Local\\Luna_" + instance)) {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    string addressFile = Path.Combine(CacheDir, "address-" + instance + ".txt");
                    if (!acquired) {
                        for (int i = 0; i < 30 && !File.Exists(addressFile); i++) Thread.Sleep(100);
                        if (!noBrowser && File.Exists(addressFile)) { var existing = File.ReadAllText(addressFile); if (!RestoreExistingWindow(existing)) OpenBrowser(existing); }
                        return;
                    }
                    try {
                        Directory.CreateDirectory(CacheDir);
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        ServicePointManager.DefaultConnectionLimit = 8;
                        for (int i = 0; i < 30; i++) {
                            try { Listener = new TcpListener(IPAddress.Loopback, wanted == 0 ? 0 : wanted + i); Listener.Start(64); Port = ((IPEndPoint)Listener.LocalEndpoint).Port; break; }
                            catch (SocketException) { if (i == 29) throw; }
                        }
                        Address = "http://127.0.0.1:" + Port;
                        File.WriteAllText(addressFile, Address, Encoding.ASCII);
                        File.WriteAllText(Path.Combine(CacheDir, "running-url.txt"), Address, Encoding.ASCII);
                        if (maintenanceProbe && ProbeToken != "" && wanted == 0) Deployment.Write(Path.Combine(CacheDir, "app-update", "probe-" + ProbeToken + ".json"), new { token = ProbeToken, pid = Process.GetCurrentProcess().Id, port = Port });
                        Runtime.Library.InitializeLibrary();
                        if (!maintenanceProbe) { Runtime.Updates.ScheduleStartupUpdate(); Runtime.Storage.StartHousekeeping(); Runtime.AppUpdates.ScheduleAppUpdate(); }
                        bool native = !noBrowser && !args.Contains("--browser");
                        if (native) { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Desktop = new DesktopWindow(); }
                        else if (!noBrowser) OpenBrowser(Address);
                        using (var timer = new System.Threading.Timer(delegate {
                            Runtime.Storage.ScheduleStorageMaintenance();
                            // Tray mode intentionally keeps updates running until explicit exit.
                            if (Desktop == null && DateTime.UtcNow - new DateTime(Interlocked.Read(ref LastActivity), DateTimeKind.Utc) > TimeSpan.FromMinutes(10)) Stop();
                        }, null, 60000, 60000)) {
                            if (native) {
                                var server = new Thread(delegate() { try { AcceptClients(); } catch (Exception ex) { Runtime.Diagnostics.Log(ex); Stop(); } }) { IsBackground = true, Name = "Luna local server" };
                                server.Start();
                                using (Desktop) Application.Run(Desktop);
                                Stop(); server.Join(2000);
                            } else AcceptClients();
                        }
                    } finally { if (Listener != null) Listener.Stop(); Runtime.Dispose(); if (DesktopIcons != null) DesktopIcons.Close(); mutex.ReleaseMutex(); }
                }
            } catch (Exception ex) {
                try { File.WriteAllText(Path.Combine(Root, "启动错误.txt"), ex.ToString(), Encoding.UTF8); } catch { }
                if (!args.Contains("--no-browser") && !DesktopProbe) MessageBox.Show(Runtime == null ? "Luna could not start. See 启动错误.txt." : Runtime.Preferences.StartupFailureMessage(), "Luna");
                Environment.ExitCode = 1;
            } finally { if (Runtime != null) Runtime.Dispose(); }
        }

        static void AcceptClients() {
            while (Running) {
                TcpClient client;
                try { client = Listener.AcceptTcpClient(); }
                catch (SocketException) { if (!Running) break; throw; }
                catch (ObjectDisposedException) { if (!Running) break; throw; }
                if (!Clients.Wait(0)) { client.Close(); continue; }
                ThreadPool.QueueUserWorkItem(delegate { try { Handle(client); } finally { Clients.Release(); } });
            }
        }
        internal static void Stop() {
            if (Interlocked.Exchange(ref StopRequested, 1) != 0) return;
            Running = false;
            // Cancellation callbacks may wait for active requests. Release the accept
            // loop/window first so a maintenance handoff can always finish exiting.
            if (Listener != null) Listener.Stop();
            if (Desktop != null) Desktop.End();
            Runtime.Workers.Stop();
        }

        static string[] EdgePaths() {
            return new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
            };
        }
        static void OpenBrowser(string url) {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || parsed.Scheme != "http" || parsed.Host != "127.0.0.1") return;
            string edge = EdgePaths().FirstOrDefault(File.Exists);
            if (edge != null) Process.Start(new ProcessStartInfo(edge, "--app=\"" + parsed.GetLeftPart(UriPartial.Authority) + "/" + DesktopTag(parsed.Port) + "\" --window-size=1320,900") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        static void Handle(TcpClient client) {
            using (client) {
                client.ReceiveTimeout = 10000; client.SendTimeout = 15000;
                try {
                    using (var stream = client.GetStream()) {
                        LocalHttpRequest request;
                        try { request = LocalHttpRequest.Read(stream); }
                        catch (LocalHttpException ex) { RejectRequest(client, stream, ex.Status, ex.Message); return; }
                        if (request == null) return;
                        var headers = request.Headers;
                        string host, origin;
                        if (!headers.TryGetValue("Host", out host) || host != "127.0.0.1:" + Port) { RejectRequest(client, stream, 403, "仅支持本机访问"); return; }
                        if (headers.TryGetValue("Origin", out origin) && origin != Address) { RejectRequest(client, stream, 403, "不允许跨站请求"); return; }
                        Uri url = new Uri(Address + request.Target);
                        string path = url.AbsolutePath;
                        if (path.StartsWith("/api/") && path != "/api/health") {
                            string marker;
                            if (!headers.TryGetValue("X-Luna", out marker) || marker != "1") { RejectRequest(client, stream, 403, "请从本地程序打开"); return; }
                        }
                        bool mutation = path == "/api/quit" || path == "/api/library/update" || path == "/api/library/cancel" || path == "/api/library/focus" || path == "/api/hero/update" || path == "/api/settings/save" || path == "/api/folders/open" || path == "/api/window/show" || path == "/api/window/close" || path == "/api/space/clean" || path == "/api/app-update/check" || path == "/api/app-update/download" || path == "/api/app-update/cancel" || path == "/api/maintenance/run";
                        if (request.Method != (mutation ? "POST" : "GET")) { RejectRequest(client, stream, 405, "不支持此请求方式"); return; }
                        string length;
                        if (headers.ContainsKey("Transfer-Encoding") || (headers.TryGetValue("Content-Length", out length) && length != "0")) { RejectRequest(client, stream, 400, "此接口不接收请求正文"); return; }
                        Interlocked.Exchange(ref LastActivity, DateTime.UtcNow.Ticks);
                        using (var pending = new LocalHttpCancellation(client, Runtime.Workers.Token)) try {
                            CancellationToken token = pending.Token;
                            var q = HttpUtility.ParseQueryString(url.Query);

                            if (path == "/api/health") { SendJson(stream, new { app = "luna-local", version = PreferenceStore.AppVersion, author = "FrancisChen", offline = Runtime.Options.Offline, probeToken = ProbeToken }); return; }
                            if (path == "/api/space") { SendJson(stream, Runtime.Storage.SpaceInfo()); return; }
                            if (path == "/api/space/clean") { Runtime.Storage.RequestSpaceMaintenance(); SendJson(stream, Runtime.Storage.SpaceInfo()); return; }
                            if (path == "/api/app-update") { SendJson(stream, Runtime.AppUpdates.AppUpdateInfo()); return; }
                            if (path == "/api/app-update/check" || path == "/api/app-update/download") { Runtime.AppUpdates.StartAppUpdate(path.EndsWith("/check") ? "check" : "download"); SendJson(stream, Runtime.AppUpdates.AppUpdateInfo()); return; }
                            if (path == "/api/app-update/cancel") { Runtime.AppUpdates.CancelAppUpdate(); SendJson(stream, Runtime.AppUpdates.AppUpdateInfo()); return; }
                            if (path == "/api/maintenance") { SendJson(stream, Runtime.AppUpdates.MaintenanceInfo()); return; }
                            if (path == "/api/maintenance/run") { Runtime.AppUpdates.PrepareMaintenance(q["action"], q["removeData"], Runtime.Updates.CancelLibraryUpdate); SendJson(stream, new { success = true }); Stop(); return; }
                            if (path == "/api/window") { SendJson(stream, WindowInfo()); return; }
                            if (path == "/api/window/show") { SendJson(stream, new { available = Desktop != null }); if (Desktop != null) Desktop.ShowFromAnotherLaunch(); return; }
                            if (path == "/api/window/close") { ValidateWindowChoice(q["choice"], q["remember"]); SendJson(stream, new { success = true }); Desktop.Choose(q["choice"]); return; }
                            if (path == "/api/settings") { SendJson(stream, Runtime.SettingsInfo()); return; }
                            if (path == "/api/settings/save") { SendJson(stream, Runtime.SavePreference(q["key"], q["value"])); return; }
                            if (path == "/api/folders/open") { Runtime.Preferences.OpenLocalFolder(q["kind"]); SendJson(stream, new { success = true }); return; }
                            if (path == "/api/diagnostics") { SendJson(stream, Runtime.DiagnosticInfo()); return; }
                            if (path == "/api/about/document") { SendJson(stream, Runtime.Preferences.ReadAboutDocument(q["name"])); return; }
                            if (path == "/api/heartbeat") { SendJson(stream, new { success = true }); return; }
                            if (path == "/api/quit") { SendJson(stream, new { success = true }); Stop(); return; }
                            if (path == "/api/catalog") { SendJson(stream, Runtime.Queries.LocalCatalog()); return; }
                            if (path == "/api/library" || path == "/api/library/update") {
                                string profile = (q["thresholdLevel"] ?? "1") + "-" + (q["sensitivity"] ?? "5");
                                if (!System.Text.RegularExpressions.Regex.IsMatch(profile, @"^[1-5]-[1-5]$")) throw new ArgumentException("筛选档位无效");
                                if (path.EndsWith("/update")) Runtime.Updates.StartLibraryUpdate(profile, q["all"] == "1");
                                SendJson(stream, Runtime.LibraryInfo(profile)); return;
                            }
                            if (path == "/api/library/cancel") { Runtime.Updates.CancelLibraryUpdate(); SendJson(stream, new { success = true }); return; }
                            if (path == "/api/library/focus") { Runtime.Library.RegisterFocus(q["key"]); SendJson(stream, new { success = true }); return; }
                            if (path == "/api/hero/status") { SendJson(stream, Runtime.Updates.HeroUpdateInfo(q["key"])); return; }
                            if (path == "/api/hero/update") { SendJson(stream, Runtime.Updates.StartHeroUpdate(q["key"])); return; }
                            if (path == "/api/detail") {
                                Runtime.Activity.Touch();
                                int id, threshold, sensitivity;
                                string lane = q["lane"], mode = q["mode"];
                                if (!int.TryParse(q["championId"], out id) || id < 1 || id > 10000 || !DataFormat.Lanes.Contains(lane) || (mode != "father" && mode != "son") ||
                                    !int.TryParse(q["thresholdLevel"], out threshold) || threshold < 1 || threshold > 5 || !int.TryParse(q["sensitivity"], out sensitivity) || sensitivity < 1 || sensitivity > 5) {
                                    SendText(stream, 400, "英雄、分路或筛选条件无效"); return;
                                }
                                string key = "detail-" + id + "-" + lane + "-" + mode + "-" + threshold + "-" + sensitivity;
                                SendJson(stream, Runtime.Queries.LocalDetail(key)); return;
                            }
                            if (path == "/api/window-icon") { SendJson(stream, DesktopIcons == null ? (object)new { supported = true, windows = new object[0] } : DesktopIcons.Status()); return; }
                            if (path == DesktopPath) { StartDesktopIcons(); path = "/"; }
                            var allowed = new Dictionary<string, string> { { "/", "index.html" }, { "/index.html", "index.html" }, { "/app.css", "app.css" }, { "/app.js", "app.js" }, { "/locales.js", "locales.js" }, { "/i18n.js", "i18n.js" }, { "/settings.js", "settings.js" }, { "/core.js", "core.js" }, { "/aliases.js", "aliases.js" }, { "/logo.svg", "logo.svg" }, { "/app.ico", "app.ico" }, { "/app-1.4.1.ico", "app-1.4.1.ico" }, { "/app-32.png", "app-32.png" }, { "/app-48.png", "app-48.png" }, { "/app-256.png", "app-256.png" }, { "/favicon.ico", "app.ico" } };
                            string file;
                            if (allowed.TryGetValue(path, out file)) {
                                string type = file.EndsWith(".ico") ? "image/x-icon" : file.EndsWith(".png") ? "image/png" : file.EndsWith(".js") ? "text/javascript" : file.EndsWith(".css") ? "text/css" : file.EndsWith(".svg") ? "image/svg+xml" : "text/html";
                                Send(stream, 200, type + "; charset=utf-8", File.ReadAllBytes(Path.Combine(Root, "web", file))); return;
                            }
                            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^/icons/[0-9]{1,5}\.png$")) {
                                string iconFile = Path.Combine(Root, "data", "icons", Path.GetFileName(path));
                                if (File.Exists(iconFile)) { Send(stream, 200, "image/png", File.ReadAllBytes(iconFile)); return; }
                                Send(stream, 200, "image/svg+xml", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='80'><rect width='80' height='80' rx='12' fill='#283449'/><path d='M40 15 58 33 40 65 22 33Z' fill='#8a9bb5'/></svg>")); return;
                            }
                            SendText(stream, 404, "未找到此内容");
                        } catch (OperationCanceledException) { return; }
                        catch (PreferenceStore.SettingsException ex) { SendText(stream, 400, ex.Code); }
                        catch (ArgumentException ex) { SendText(stream, 400, ex.Message); }
                        catch (Exception ex) { Runtime.Diagnostics.Log(ex); SendText(stream, 502, "本地数据未能读取，请查看数据管理中的状态，或从完整程序包恢复 data 文件夹。"); }
                    }
                } catch (Exception ex) { if (!(ex is IOException) && !(ex is SocketException)) Runtime.Diagnostics.Log(ex); }
            }
        }

        static void RejectRequest(TcpClient client, NetworkStream stream, int status, string message) {
            SendText(stream, status, message);
            // RFC 9112 section 9.6: closing with unread input can reset the connection
            // and discard the error response. Send EOF first, then discard only a
            // bounded amount of input. Rejected bytes are never parsed or executed.
            try {
                client.Client.Shutdown(SocketShutdown.Send);
                var clock = Stopwatch.StartNew();
                var buffer = new byte[4096];
                int remaining = 16384;
                while (remaining > 0 && clock.ElapsedMilliseconds < 200) {
                    int wait = (int)Math.Max(1, 200 - clock.ElapsedMilliseconds) * 1000;
                    if (!client.Client.Poll(wait, SelectMode.SelectRead)) break;
                    int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0) break;
                    remaining -= read;
                }
            } catch (IOException) { } catch (SocketException) { } catch (ObjectDisposedException) { }
        }
        static void SendText(NetworkStream stream, int status, string message) { Send(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(DataFormat.Json().Serialize(new { success = false, message = message }))); }
        static void SendJson(NetworkStream stream, object data) { Send(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(DataFormat.Json().Serialize(data))); }
        static void Send(NetworkStream stream, int status, string type, byte[] body) {
            string reason = status == 200 ? "OK" : status == 400 ? "Bad Request" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : status == 408 ? "Request Timeout" : "Bad Gateway";
            string header = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n" +
                "Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(header); stream.Write(bytes, 0, bytes.Length); stream.Write(body, 0, body.Length);
        }
    }
}

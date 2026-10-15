using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// Isolated test entry point, never compiled into the shipped Windows runtime.
// Only the update transports are replaced. Signature/hash/path checks, preferences,
// HTTP UI, resumable download and the maintenance handoff use application code.
internal static class AppUpdateSession {
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly object LogLock = new object();
    static Dictionary<string, object> Config;
    static void Log(object entry) { lock (LogLock) File.AppendAllText(Path.Combine(Root, "fixture-requests.jsonl"), Json.Serialize(entry) + Environment.NewLine, Encoding.UTF8); }
    [STAThread] static int Main(string[] args) {
        try {
            Config = (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(Path.Combine(Root, "fixture.json")));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            string endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/package";
            var server = new Thread(() => {
                while (true) {
                    using (var client = listener.AcceptTcpClient()) {
                        try { using (var stream = client.GetStream()) {
                            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); string line; long offset = 0;
                            while (!String.IsNullOrEmpty(line = reader.ReadLine())) if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) offset = long.Parse(line.Substring(13).TrimEnd('-'));
                            using (var file = File.OpenRead(Config["archive"].ToString())) {
                                Log(new { kind = "download", at = DateTime.UtcNow.ToString("o"), offset = offset, total = file.Length });
                                var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (offset > 0 ? "206 Partial Content" : "200 OK") + "\r\nContent-Length: " + (file.Length - offset) + "\r\n" + (offset > 0 ? "Content-Range: bytes " + offset + "-" + (file.Length - 1) + "/" + file.Length + "\r\n" : "") + "Connection: close\r\n\r\n");
                                stream.Write(header, 0, header.Length); file.Position = offset;
                                byte[] buffer = new byte[32768]; int count;
                                while ((count = file.Read(buffer, 0, buffer.Length)) > 0) { stream.Write(buffer, 0, count); stream.Flush(); Thread.Sleep(Convert.ToInt32(Config["delayMs"])); }
                            }
                        } } catch (IOException) { } catch (SocketException) { }
                    }
                }
            }) { IsBackground = true }; server.Start();
            var app = typeof(Luna.Program); var flags = BindingFlags.Static | BindingFlags.NonPublic;
            app.GetField("ReleaseTransport", flags).SetValue(null, new Func<string, CancellationToken, string>((url, token) => {
                token.ThrowIfCancellationRequested();
                if (url.StartsWith("https://api.github.com/")) {
                    Log(new { kind = "check", at = DateTime.UtcNow.ToString("o") });
                    return Json.Serialize(new[] { new { draft = false, prerelease = false, assets = new[] { new { name = "luna-update.json", browser_download_url = Config["metadataUrl"].ToString() } } } });
                }
                if (url != Config["metadataUrl"].ToString()) throw new IOException("Unexpected metadata request in test");
                return File.ReadAllText(Config["metadata"].ToString());
            }));
            app.GetField("AppDownloadTransport", flags).SetValue(null, new Func<string, long, CancellationToken, HttpWebResponse>((url, offset, token) => {
                var request = (HttpWebRequest)WebRequest.Create(endpoint); request.Proxy = null; request.Timeout = 5000;
                if (offset > 0) request.AddRange(offset);
                using (token.Register(request.Abort)) { token.ThrowIfCancellationRequested(); return (HttpWebResponse)request.GetResponse(); }
            }));
            app.GetMethod("Main", flags).Invoke(null, new object[] { args }); return 0;
        } catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "fixture-error.txt"), ex.ToString()); return 1; }
    }
}

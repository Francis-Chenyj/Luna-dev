using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;

internal static class RuntimeSmoke {
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static string CopyRuntime(string source, string target) {
        var manifest = Luna.Deployment.Manifest(source, true);
        foreach (var row in Luna.Deployment.Array(manifest, "files").Select(Luna.Deployment.Map)) {
            string relative = Luna.Deployment.Text(row, "path");
            Luna.Deployment.AtomicCopy(Luna.Deployment.Under(source, relative), Luna.Deployment.Under(target, relative));
        }
        Luna.Deployment.AtomicCopy(Path.Combine(source, "installed-files.json"), Path.Combine(target, "installed-files.json"));
        return target;
    }
    static void VerifyHelper(string root) {
        using (var p = Process.Start(new ProcessStartInfo(Path.Combine(root, "Luna.Maintenance.exe"), "--verify") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root })) {
            if (!p.WaitForExit(20000)) { p.Kill(); throw new Exception("Maintenance verification timed out"); }
            Check(p.ExitCode == 0, "The actual maintenance executable rejected a valid deep runtime");
        }
    }
    static void BrokenJournalStartup(string root) {
        string area = Path.Combine(root, "cache/app-update"); Directory.CreateDirectory(area);
        File.WriteAllText(Path.Combine(area, "transaction.json"), "{");
        int port; var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (port > 65000) port = 18883;
        using (var p = Process.Start(new ProcessStartInfo(Path.Combine(root, "Luna.exe"), "--offline --no-browser --port " + port) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root })) {
            string address = "";
            try {
                var watch = Stopwatch.StartNew(); bool ready = false; string lastError = "No response";
                while (!p.HasExited && watch.ElapsedMilliseconds < 15000) {
                    try {
                        address = File.ReadAllText(Path.Combine(root, "cache/running-url.txt")).Trim();
                        using (var client = new WebClient { Proxy = null }) {
                            client.Headers["X-Luna"] = "1";
                            var result = Luna.Deployment.Map(new JavaScriptSerializer().DeserializeObject(System.Text.Encoding.UTF8.GetString(client.DownloadData(address + "/api/detail?championId=266&lane=top&mode=father&thresholdLevel=1&sensitivity=5"))));
                            ready = result.ContainsKey("data") && result["data"] != null;
                            if (!ready) lastError = Luna.Deployment.Json.Serialize(result);
                        }
                        if (ready) break;
                    } catch (Exception ex) { lastError = ex.ToString(); Thread.Sleep(100); }
                }
                Check(ready, "A truncated update journal blocked a real offline query: " + lastError + (p.HasExited ? " Exit=" + p.ExitCode : ""));
                Check(Directory.GetFiles(area, "transaction.invalid-*.json").Length == 1, "Real launch did not preserve the bad record");
            } finally {
                try { if (address != "") using (var client = new WebClient { Proxy = null }) { client.Headers["X-Luna"] = "1"; client.UploadData(address + "/api/quit", "POST", new byte[0]); } } catch { }
                if (!p.WaitForExit(4000)) p.Kill();
            }
        }
    }
    static int Main(string[] args) {
        try {
            string source = args[0], output = Path.GetFullPath(args[1]); bool desktop = args.Contains("--desktop");
            if (Directory.Exists(output)) throw new IOException("Use a new isolated smoke directory");
            Directory.CreateDirectory(output);
            string root = CopyRuntime(source, Path.Combine(output, "recovery"));
            BrokenJournalStartup(root); Console.WriteLine("PASS: corrupt journal -> real offline startup/query, original record preserved");
            string deep = Path.Combine(output, "中文 long path");
            while (deep.Length < 222) deep = Path.Combine(deep, new string('d', Math.Max(1, Math.Min(40, 222 - deep.Length - 1))));
            deep = CopyRuntime(source, deep); VerifyHelper(deep);
            string longest = Luna.Deployment.Array(Luna.Deployment.Manifest(deep, true), "files").Select(Luna.Deployment.Map).Select(r => Luna.Deployment.Under(deep, Luna.Deployment.Text(r, "path"))).OrderByDescending(p => p.Length).First();
            Check(longest.Length > 260, "Deep-runtime test did not cross MAX_PATH");
            File.WriteAllText(Path.Combine(deep, "personal.txt"), "keep");
            string prefs = Path.Combine(deep, "cache/settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(prefs)); File.WriteAllText(prefs, "keep settings");
            Luna.Deployment.Uninstall(deep, false);
            Check(!File.Exists(Path.Combine(deep, "Luna.exe")) && File.ReadAllText(prefs) == "keep settings" && File.ReadAllText(Path.Combine(deep, "personal.txt")) == "keep", "Deep uninstall lost data or left the executable");
            Console.WriteLine("PASS: actual helper manifest verification and keep-data uninstall; longest owned path " + longest.Length);
            if (desktop) {
                string native = CopyRuntime(source, Path.Combine(output, "native"));
                Check(Luna.Deployment.HealthCheck(native), "Native WebView2 + app JavaScript + query probe failed");
                File.WriteAllText(Path.Combine(native, "web/app.js"), "throw new Error('intentional isolated smoke failure');");
                Check(!Luna.Deployment.HealthCheck(native), "A broken JavaScript application passed upgrade health checking");
                Check(Directory.GetFiles(Path.Combine(native, "cache/app-update"), "probe-*.json").Length == 0, "Health probes left per-process address records behind");
                Console.WriteLine("PASS: actual native readiness and rejection of broken application JavaScript");
            }
            Luna.Deployment.Write(Path.Combine(output, "result.json"), new { passed = true, corruptJournalRealQuery = true, maintenanceLongPath = longest.Length, keepDataUninstall = true, nativeReadiness = desktop, rejectsBrokenJavaScript = desktop });
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

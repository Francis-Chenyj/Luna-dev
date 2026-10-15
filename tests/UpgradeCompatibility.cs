using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;

internal static class UpgradeCompatibility {
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static string CopyPrevious(string source, string target) {
        var manifest = Luna.Deployment.Manifest(source, true);
        foreach (var row in Luna.Deployment.Array(manifest, "files").Select(Luna.Deployment.Map)) {
            string relative = Luna.Deployment.Text(row, "path");
            Luna.Deployment.AtomicCopy(Luna.Deployment.Under(source, relative), Luna.Deployment.Under(target, relative));
        }
        Luna.Deployment.AtomicCopy(Path.Combine(source, "installed-files.json"), Path.Combine(target, "installed-files.json"));
        Directory.CreateDirectory(Path.Combine(target, "cache"));
        File.WriteAllText(Path.Combine(target, "cache/settings.json"), "{\"schema\":1,\"language\":\"en\",\"theme\":\"dark\",\"autoUpdate\":false,\"appAutoCheck\":false,\"appAutoDownload\":false,\"closeAction\":\"ask\"}");
        File.WriteAllText(Path.Combine(target, "personal.txt"), "preserve"); return target;
    }
    static void Stage(string root, string archive, string envelope) {
        string area = Path.Combine(root, "cache/app-update"); Directory.CreateDirectory(area);
        File.Copy(archive, Path.Combine(area, "package.zip")); File.Copy(envelope, Path.Combine(area, "release.json"));
        Luna.Deployment.ExtractVerified(archive, Path.Combine(area, "stage"), Luna.Deployment.VerifyEnvelope(File.ReadAllText(envelope)));
    }
    static void OldQuery(string root, string expectedVersion) {
        int port; var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); if (port > 65000) port = 18893;
        using (var p = Process.Start(new ProcessStartInfo(Path.Combine(root, "Luna.exe"), "--no-browser --offline --maintenance-probe --port " + port) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root })) {
            string address = "";
            try {
                var watch = Stopwatch.StartNew(); bool ready = false;
                while (!p.HasExited && watch.ElapsedMilliseconds < 15000) {
                    try {
                        address = File.ReadAllText(Path.Combine(root, "cache/running-url.txt")).Trim();
                        using (var client = new WebClient { Proxy = null }) {
                            client.Headers["X-Luna"] = "1";
                            var health = Luna.Deployment.Map(Luna.Deployment.Json.DeserializeObject(System.Text.Encoding.UTF8.GetString(client.DownloadData(address + "/api/health"))));
                            var detail = Luna.Deployment.Map(Luna.Deployment.Json.DeserializeObject(System.Text.Encoding.UTF8.GetString(client.DownloadData(address + "/api/detail?championId=266&lane=top&mode=father&thresholdLevel=1&sensitivity=5"))));
                            ready = Luna.Deployment.Text(health, "version") == expectedVersion && detail.ContainsKey("data") && detail["data"] != null;
                        }
                        if (ready) break;
                    } catch { Thread.Sleep(100); }
                }
                Check(ready, "The unmodified historical executable did not serve an offline query");
            } finally {
                try { if (address != "") using (var client = new WebClient { Proxy = null }) { client.Headers["X-Luna"] = "1"; client.UploadData(address + "/api/quit", "POST", new byte[0]); } } catch { }
                if (!p.WaitForExit(4000)) p.Kill();
            }
        }
    }
    static int Main(string[] args) {
        try {
            string previous = args[0], archive = args[1], envelope = args[2], output = Path.GetFullPath(args[3]);
            Check(!Directory.Exists(output), "Use a new compatibility fixture directory"); Directory.CreateDirectory(output);
            var old = Luna.Deployment.Manifest(previous, true); string oldVersion = Luna.Deployment.Text(old, "version"), newVersion = Luna.Deployment.Text(Luna.Deployment.VerifyEnvelope(File.ReadAllText(envelope)), "version");
            Check(new Version(newVersion) > new Version(oldVersion), "Supply a genuinely older complete runtime");
            string root = CopyPrevious(previous, Path.Combine(output, "upgrade")); string preferences = File.ReadAllText(Path.Combine(root, "cache/settings.json"));
            OldQuery(root, oldVersion); Stage(root, archive, envelope); string area = Path.Combine(root, "cache/app-update");
            Luna.Deployment.Apply(root, Path.Combine(area, "stage"), Path.Combine(area, "release.json"), Luna.Deployment.HealthCheck);
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(root, true), "version") == newVersion && File.ReadAllText(Path.Combine(root, "cache/settings.json")) == preferences && File.ReadAllText(Path.Combine(root, "personal.txt")) == "preserve", "Upgrade lost version or user files");
            string failed = CopyPrevious(previous, Path.Combine(output, "rollback")); Stage(failed, archive, envelope); string failedArea = Path.Combine(failed, "cache/app-update"); bool rejected = false;
            try { Luna.Deployment.Apply(failed, Path.Combine(failedArea, "stage"), Path.Combine(failedArea, "release.json"), r => false); } catch (IOException) { rejected = true; }
            Check(rejected && Luna.Deployment.HashFile(Path.Combine(failed, "Luna.exe")) == Luna.Deployment.HashFile(Path.Combine(previous, "Luna.exe")), "Rollback did not restore the exact historical executable");
            OldQuery(failed, oldVersion);
            Luna.Deployment.Write(Path.Combine(output, "result.json"), new { passed = true, predecessorVersion = oldVersion, predecessorSha256 = Luna.Deployment.HashFile(Path.Combine(previous, "Luna.exe")), targetVersion = newVersion, nativeTargetHealth = true, exactHistoricalRollbackQuery = true, retainedSettings = true, transport = "Local archive, ephemeral test signature and current maintenance code; no GitHub/CDN or historical updater handoff claim" });
            Console.WriteLine("PASS: unmodified " + oldVersion + " runtime -> " + newVersion + " native health; exact historical rollback and queries; settings retained"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class LifecycleTests {
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly TestApplication App = new TestApplication(AppDomain.CurrentDomain.BaseDirectory);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    static object Call(string name, params object[] args) { try { return App.Call(name, args); } catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; } }
    static void Set(string name, object value) { App.Set(name, value); }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action, string message) { try { action(); } catch (IOException) { return; } catch (ArgumentException) { return; } catch (CryptographicException) { return; } throw new Exception(message); }
    static void Pass(string message) { Console.WriteLine("PASS: " + message); }
    static object Field(string name) { return App.Field(name); }
    static Dictionary<string, object> Snapshot(string name) { return Map(Json.DeserializeObject(Json.Serialize(Call(name)))); }
    static void Until(Func<bool> condition, string message) { var timer = Stopwatch.StartNew(); while (!condition() && timer.ElapsedMilliseconds < 5000) Thread.Sleep(10); Check(condition(), message); }
    static void Idle() { Until(() => !(bool)Snapshot("AppUpdateInfo")["busy"], "Updater did not become idle"); }
    static void UpdatePreferences(string archive, string envelope) {
        string settings = Path.Combine(Root, "cache/settings.json");
        Put(Root, "cache/settings.json", "{\"schema\":1,\"language\":\"en\",\"theme\":\"dark\",\"autoUpdate\":false,\"appAutoCheck\":false,\"appAutoDownload\":true}");
        var preferences = Map(Snapshot("SettingsInfo")["preferences"]);
        Check(!(bool)preferences["appAutoCheck"] && !(bool)preferences["appAutoDownload"], "Legacy download-only preference enabled networking");
        App.Runtime.SavePreference("theme", "light");
        Check(!(bool)Map(Json.DeserializeObject(File.ReadAllText(settings)))["appAutoDownload"], "Legacy preference not normalized on save");
        App.Runtime.AppUpdates.DiscardDownload();
        string feed = Json.Serialize(new[] { new { draft = false, prerelease = false, assets = new[] { new { name = "luna-update.json", browser_download_url = "https://github.com/Francis-Chenyj/Luna-dev/releases/download/v1.7.0/luna-update.json" } } } });
        int checks = 0; var entered = new ManualResetEventSlim(); var releaseCheck = new ManualResetEventSlim(); var finishCancellation = new ManualResetEventSlim();
        bool delayCancellation = true;
        Set("ReleaseTransport", new Func<string, CancellationToken, string>((url, token) => {
            if (!url.Contains("api.github.com")) return envelope;
            Interlocked.Increment(ref checks); entered.Set();
            try { releaseCheck.Wait(token); } catch (OperationCanceledException) { if (delayCancellation) finishCancellation.Wait(5000); throw; }
            return feed;
        }));
        using (var server = new DownloadServer(File.ReadAllBytes(archive))) {
            Set("AppDownloadTransport", new Func<string, long, CancellationToken, HttpWebResponse>((url, offset, token) => server.Get(offset)));
            Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache/app-update/check.json"), new { state = "current", checkedAt = DateTime.UtcNow.ToString("o") });
            var timer = Stopwatch.StartNew(); App.Runtime.SavePreference("appAutoDownload", "true");
            Check(timer.ElapsedMilliseconds < 1000, "Saving waited for network");
            Check(entered.Wait(2000), "Enabling download did not immediately check");
            preferences = Map(Snapshot("SettingsInfo")["preferences"]);
            Check((bool)preferences["appAutoCheck"] && (bool)preferences["appAutoDownload"], "Download did not atomically enable checking");
            Check((bool)Map(Json.DeserializeObject(File.ReadAllText(settings)))["appAutoCheck"], "Coupled check setting was not persisted");
            App.Runtime.SavePreference("appAutoDownload", "true"); App.Runtime.AppUpdates.CheckScheduledAppUpdate();
            Check(checks == 1, "An active check was duplicated");
            App.Runtime.SavePreference("appAutoCheck", "false");
            preferences = Map(Snapshot("SettingsInfo")["preferences"]);
            Check(!(bool)preferences["appAutoCheck"] && !(bool)preferences["appAutoDownload"], "Check opt-out did not disable downloads");
            App.Runtime.SavePreference("appAutoDownload", "true");
            releaseCheck.Set(); finishCancellation.Set();
            Until(() => checks == 2 && !(bool)Snapshot("AppUpdateInfo")["busy"], "Enable during cancellation lost its immediate check");
            Check(Snapshot("AppUpdateInfo")["state"].ToString() == "ready" && server.Offsets.Count == 1, "Automatic check/download did not reach ready exactly once");
            Check(File.Exists(Path.Combine(Root, "cache/app-update/stage/installed-files.json")), "Ready update was not saved");
            Pass("atomic toggle coupling, immediate check despite recent success, nonblocking save, deduplication and rapid off/on cancellation recovery");

            App.Runtime.SavePreference("appAutoDownload", "false"); preferences = Map(Snapshot("SettingsInfo")["preferences"]);
            Check((bool)preferences["appAutoCheck"] && !(bool)preferences["appAutoDownload"], "Disabling downloads disabled version checks");
            App.Runtime.SavePreference("appAutoDownload", "true"); Idle();
            Check(server.Offsets.Count == 1, "Re-enabling downloaded the verified package again");
            App.Runtime.AppUpdates.DiscardDownload();
            App.Runtime.SavePreference("appAutoCheck", "false");
            var downloadEntered = new ManualResetEventSlim(); var continueDownload = new ManualResetEventSlim();
            Set("AppDownloadTransport", new Func<string, long, CancellationToken, HttpWebResponse>((url, offset, token) => { downloadEntered.Set(); continueDownload.Wait(token); return server.Get(offset); }));
            App.Runtime.SavePreference("appAutoDownload", "true"); Check(downloadEntered.Wait(2000), "Automatic download did not start");
            App.Runtime.SavePreference("appAutoDownload", "false"); Idle();
            Check(Snapshot("AppUpdateInfo")["state"].ToString() == "paused", "Automatic download continued after opt-out");
            App.Runtime.AppUpdates.CheckScheduledAppUpdate(); Check(!(bool)Snapshot("AppUpdateInfo")["busy"], "Startup reused stale download preference");
            downloadEntered.Reset();
            App.Runtime.AppUpdates.StartAppUpdate("download"); Check(downloadEntered.Wait(2000), "Explicit manual download did not start");
            App.Runtime.SavePreference("appAutoCheck", "false");
            Check(!((CancellationTokenSource)Field("AppUpdateCancellation")).IsCancellationRequested, "Turning off automation cancelled an explicit manual download");
            continueDownload.Set(); Idle(); Check(Snapshot("AppUpdateInfo")["state"].ToString() == "ready", "Manual download failed with automation off");
            Pass("download opt-out retains checking and progress, prepared packages are reused, explicit manual downloads remain available");
        }
        int before = checks;
        Set("Offline", true); App.Runtime.SavePreference("appAutoDownload", "true"); App.Runtime.AppUpdates.CheckScheduledAppUpdate();
        Check(checks == before, "Offline preference change contacted the network");
        App.Runtime.SavePreference("appAutoCheck", "false"); Set("Offline", false); Set("CommandLineDisablesUpdates", true);
        App.Runtime.SavePreference("appAutoDownload", "true"); App.Runtime.AppUpdates.CheckScheduledAppUpdate(); Check(checks == before, "Manual-only launch contacted the network");
        Set("CommandLineDisablesUpdates", false); App.Runtime.SavePreference("appAutoCheck", "false");
        Set("ReleaseTransport", new Func<string, CancellationToken, string>((url, token) => { Interlocked.Increment(ref checks); throw new IOException("Controlled offline release feed"); }));
        App.Runtime.SavePreference("appAutoCheck", "true"); Idle();
        Check(Snapshot("AppUpdateInfo")["state"].ToString() == "error" && (bool)Map(Snapshot("SettingsInfo")["preferences"])["appAutoCheck"], "Network error incorrectly failed preference saving");
        before = checks; App.Runtime.AppUpdates.CheckScheduledAppUpdate(); Check(checks == before, "Startup immediately retried a failed enable check");
        App.Runtime.SavePreference("appAutoCheck", "false");
        Set("ReleaseTransport", new Func<string, CancellationToken, string>((url, token) => { Interlocked.Increment(ref checks); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return "[]"; }));
        App.Runtime.SavePreference("appAutoCheck", "true"); Until(() => checks > before, "Immediate check did not start"); App.Runtime.AppUpdates.CancelAppUpdate(); Idle();
        before = checks; App.Runtime.AppUpdates.CheckScheduledAppUpdate(); Check(checks == before, "Pause was undone by the delayed startup scheduler");
        string key = Path.GetFileName(Directory.GetFiles(Path.Combine(Root, "data/library/1-5"), "*.json.gz")[0]).Replace(".json.gz", "");
        Check(Map(App.Runtime.Queries.LocalDetail(key))["data"] != null, "Update failure affected local queries");
        Pass("offline/manual-only modes, successful save despite network failure, retry cooldown, pause protection and local queries after failure");
        Set("AppRelease", null); Set("AppUpdateState", "idle");
        string savedRelease = Path.Combine(Root, "cache/app-update/release.json"); if (File.Exists(savedRelease)) File.Delete(savedRelease);
        Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache/app-update/check.json"), new { state = "available", checkedAt = DateTime.UtcNow.AddMinutes(-1).ToString("o") });
        Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache/app-update/result.json"), new { state = "installed", version = Field("AppVersion").ToString(), at = DateTime.UtcNow.ToString("o") });
        Check(Snapshot("AppUpdateInfo")["state"].ToString() == "installed" && Snapshot("AppUpdateInfo")["newVersion"].ToString() == "", "Successful installation still advertised an old offer");
        Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache/app-update/result.json"), new { state = "installed", version = "0.0.1", at = DateTime.UtcNow.ToString("o") });
        Check(Snapshot("AppUpdateInfo")["state"].ToString() == "current", "An old installation receipt was mistaken for this upgrade");
        Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache/app-update/transaction.json"), new { state = "committed", version = Field("AppVersion").ToString(), at = DateTime.UtcNow.AddSeconds(1).ToString("o") });
        Check(Snapshot("AppUpdateInfo")["state"].ToString() == "installed", "A committed upgrade lost its status when receipt writing failed");
        Pass("restart reports the installed version, consumes the old availability state and ignores receipts for other versions");
    }
    static void Put(string root, string relative, string content) { string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content, Encoding.UTF8); }
    static string Runtime(string name, string version) {
        string root = Path.Combine(Root, name); Directory.CreateDirectory(root);
        Put(root, "Luna.exe", "exe " + version); Put(root, "Luna.Maintenance.exe", "helper " + version); Put(root, "web/index.html", "view " + version);
        WriteManifest(root, version); return root;
    }
    static void WriteManifest(string root, string version) {
        var files = new[] { "Luna.exe", "Luna.Maintenance.exe", "web/index.html" }.Select(p => new { path = p, bytes = new FileInfo(Path.Combine(root, p)).Length, sha256 = Luna.Deployment.HashFile(Path.Combine(root, p)) }).ToArray();
        Luna.Deployment.Write(Path.Combine(root, "installed-files.json"), new { owner = Luna.Deployment.Owner, version = version, files = files });
    }
    static string Envelope(string archive, string version) {
        byte[] body = Encoding.UTF8.GetBytes(Json.Serialize(new { schema = 1, product = "Luna", version = version, bytes = new FileInfo(archive).Length, sha256 = Luna.Deployment.HashFile(archive), url = "https://github.com/Francis-Chenyj/Luna-dev/releases/download/v" + version + "/Luna.zip", notes = "Fixture <script>must stay inert</script>" }));
        using (var rsa = new RSACryptoServiceProvider()) {
            rsa.PersistKeyInCsp = false; rsa.FromXmlString(File.ReadAllText(Path.Combine(Root, "test-private.xml")));
            return Json.Serialize(new { payload = Convert.ToBase64String(body), signature = Convert.ToBase64String(rsa.SignData(body, CryptoConfig.MapNameToOID("SHA256"))) });
        }
    }
    static string Archive(string root, string destination) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        using (var zip = ZipFile.Open(destination, ZipArchiveMode.Create)) {
            foreach (string relative in new[] { "Luna.exe", "Luna.Maintenance.exe", "web/index.html", "installed-files.json" }) zip.CreateEntryFromFile(Path.Combine(root, relative), "Luna/" + relative);
        }
        return destination;
    }
    static void Stage(string root, string archive, string envelope) {
        string area = Path.Combine(root, "cache", "app-update"); Directory.CreateDirectory(area);
        File.Copy(archive, Path.Combine(area, "package.zip")); File.WriteAllText(Path.Combine(area, "release.json"), envelope);
        Luna.Deployment.ExtractVerified(archive, Path.Combine(area, "stage"), Luna.Deployment.VerifyEnvelope(envelope));
    }
    static void RecoveryRecords() {
        string[] invalid = { "", "{", "null", "[]", "{\"state\":\"unexpected\"}" };
        for (int i = 0; i < invalid.Length; i++) {
            string root = Runtime("broken-journal-" + i, "1.6.2"), path = Path.Combine(root, "cache/app-update/transaction.json");
            Put(root, "cache/settings.json", "personal settings"); Put(root, "cache/app-update/transaction.json", invalid[i]);
            Check(!Luna.Deployment.NeedsRecovery(root), "Verified installation was blocked by a malformed journal");
            string[] saved = Directory.GetFiles(Path.GetDirectoryName(path), "transaction.invalid-*.json");
            Check(saved.Length == 1 && File.ReadAllText(saved[0]) == invalid[i], "Malformed journal evidence was lost");
            Check(!Luna.Deployment.NeedsRecovery(root) && Directory.GetFiles(Path.GetDirectoryName(path), "transaction.invalid-*.json").Length == 1, "Repeated launch did not settle recovery");
            Check(File.ReadAllText(Path.Combine(root, "cache/settings.json")) == "personal settings", "Recovery changed settings");
        }
        string partial = Runtime("partial-journal", "1.6.2"), backup = Runtime("partial-journal/cache/app-update/rollback", "1.6.1");
        Put(partial, "Luna.exe", "half replaced"); Put(partial, "cache/app-update/transaction.json", "{"); Put(partial, "my-file.txt", "keep");
        Check(Luna.Deployment.NeedsRecovery(partial), "Broken installation did not request its verified backup");
        Luna.Deployment.Restore(partial);
        Check(!Luna.Deployment.NeedsRecovery(partial) && Luna.Deployment.Text(Luna.Deployment.Manifest(partial, true), "version") == "1.6.1", "Broken journal rollback failed");
        Check(File.ReadAllText(Path.Combine(partial, "my-file.txt")) == "keep", "Recovery deleted unowned files");
        string unsafeRoot = Runtime("unrecoverable-journal", "1.6.2"); Put(unsafeRoot, "Luna.exe", "broken"); Put(unsafeRoot, "cache/app-update/transaction.json", "{");
        Reject(() => Luna.Deployment.NeedsRecovery(unsafeRoot), "An unverified installation was allowed to start without a backup");
        Check(File.ReadAllText(Path.Combine(unsafeRoot, "cache/app-update/transaction.json")) == "{", "Unrecoverable evidence was changed");
        string interrupted = Runtime("missing-backup", "1.6.2"); Put(interrupted, "cache/app-update/transaction.json", "{\"state\":\"applying\",\"added\":[]}");
        Reject(() => Luna.Deployment.Restore(interrupted), "An applying transaction without a backup was silently committed");
        Pass("empty/truncated/null/unknown journals, repeat launch, verified backup recovery, missing backup and preservation of evidence/user files");
    }
    static void MaintenanceFaults(string archive, string envelope) {
        var failures = new List<string>();
        foreach (string fault in new[] { "cleanup-locked", "receipt-locked", "backup-incomplete", "backup-damaged", "backup-personal" }) {
            string root = Runtime("fault-" + fault, "1.6.0"), area = Path.Combine(root, "cache/app-update");
            Stage(root, archive, envelope);
            FileStream held = null;
            try {
                if (fault == "backup-incomplete" || fault == "backup-damaged") {
                    Put(root, "cache/app-update/rollback/Luna.exe", "interrupted backup");
                    if (fault == "backup-damaged") Put(root, "cache/app-update/rollback/installed-files.json", "{");
                }
                if (fault == "backup-personal") {
                    Runtime("fault-" + fault + "/cache/app-update/rollback", "1.5.0");
                    Put(root, "cache/app-update/rollback/personal.txt", "retain this file");
                }
                Luna.Deployment.Apply(root, Path.Combine(area, "stage"), Path.Combine(area, "release.json"), r => {
                    if (fault == "cleanup-locked") held = new FileStream(Path.Combine(area, "stage/web/index.html"), FileMode.Open, FileAccess.Read, FileShare.None);
                    if (fault == "receipt-locked") {
                        Put(root, "cache/app-update/result.json", "{}");
                        held = new FileStream(Path.Combine(area, "result.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                    return true;
                });
                if (held != null) { held.Dispose(); held = null; }
                Check(Luna.Deployment.Text(Luna.Deployment.Manifest(root, true), "version") == "1.7.0", "Installed files did not verify");
                Check(Luna.Deployment.Text(Luna.Deployment.Read(Path.Combine(area, "transaction.json")), "state") == "committed", "A completed installation was reported as rolled back");
                Check(Luna.Deployment.Text(Luna.Deployment.Manifest(Path.Combine(area, "rollback"), true), "version") == "1.6.0", "A verified rollback copy was not prepared");
                if (fault.StartsWith("backup-")) {
                    string saved = Directory.GetDirectories(area, "rollback-preserved-*").Single();
                    string relative = fault == "backup-personal" ? "personal.txt" : "Luna.exe";
                    Check(File.ReadAllText(Path.Combine(saved, relative)) == (fault == "backup-personal" ? "retain this file" : "interrupted backup"), "Previous backup evidence was lost");
                }
                Pass("maintenance fault: " + fault);
            } catch (Exception ex) { failures.Add(fault + ": " + ex.Message); Console.WriteLine("FAIL: maintenance fault: " + fault + ": " + ex.Message); }
            finally { if (held != null) held.Dispose(); }
        }
        Check(failures.Count == 0, String.Join("\n", failures));
    }
    static int Main() {
        try {
            string shared = Path.Combine(Root, "atomic-reader.json");
            Luna.Deployment.Write(shared, new { value = "old snapshot" });
            using (var reader = new StreamReader(Luna.LocalFile.OpenRead(shared))) {
                Luna.Deployment.Write(shared, new { value = "new snapshot" });
                Check(reader.ReadToEnd().Contains("old snapshot") && Luna.LocalFile.ReadAllText(shared).Contains("new snapshot"), "Concurrent readers did not retain a coherent snapshot across replacement");
            }
            Pass("active readers allow atomic replacement while retaining their old snapshot");
            RecoveryRecords();
            string cache = Path.Combine(Root, "cache"), focus = Path.Combine(cache, "focus"); Directory.CreateDirectory(focus);
            for (int i = 1; i <= 100; i++) File.WriteAllText(Path.Combine(focus, "focus-" + i + "-top-1-5.json.gz"), "broken");
            Put(focus, "personal.txt", "keep");
            App.Runtime.Library.MaintainFocused();
            Check(Directory.GetFiles(focus, "focus-*.json.gz").Length == 0, "Invalid focus files escaped cleanup");
            string quarantine = Path.Combine(cache, "quarantine");
            Check(Directory.GetFiles(quarantine, "luna-*.bad").Length <= 20 && File.ReadAllText(Path.Combine(focus, "personal.txt")) == "keep", "Quarantine count/personal protection");
            string huge = Path.Combine(focus, "focus-101-top-1-5.json.gz"); using (var file = File.Create(huge)) file.SetLength(20 * 1024 * 1024);
            Reject(() => Luna.DataFormat.RecordFileHash(huge), "Oversized record hashed into memory"); App.Runtime.Library.MaintainFocused();
            Check(Directory.GetFiles(quarantine).Sum(p => new FileInfo(p).Length) <= 10 * 1024 * 1024, "Quarantine byte cap");
            foreach (string file in Directory.GetFiles(quarantine)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8)); App.Runtime.Quarantine.MaintainExceptionalFiles();
            Check(Directory.GetFiles(quarantine).Length == 0, "Expired quarantine retained");
            Pass("100 malformed hero records, oversized file, quarantine count/bytes/age limits and unrelated-file protection");

            App.Runtime.Library.InitializeLibrary(); string record = Directory.GetFiles(Path.Combine(Root, "data", "library", "1-5"), "*.json.gz")[0];
            string key = Path.GetFileName(record).Replace(".json.gz", ""); byte[] original = File.ReadAllBytes(record); File.WriteAllText(record, "broken");
            var query = Map(App.Runtime.Queries.LocalDetail(key)); Check(query["data"] == null, "Lazy initialization allowed corrupt query data"); File.WriteAllBytes(record, original);
            using (var cancel = new CancellationTokenSource(30)) { try { App.Runtime.Library.AuditLibrary(cancel.Token); } catch (OperationCanceledException) { } }
            var checkpoint = Map(Luna.DataFormat.ReadRecord(Path.Combine(cache, "audit-state.json"))); int before = int.Parse(checkpoint["next"].ToString());
            Check(before > 0, "Audit did not persist progress");
            App.Runtime.Library.AuditLibrary(CancellationToken.None); checkpoint = Map(Luna.DataFormat.ReadRecord(Path.Combine(cache, "audit-state.json")));
            Check(int.Parse(checkpoint["next"].ToString()) >= before && checkpoint["completedAt"].ToString() != "", "Audit did not resume/complete");
            Put(cache, "webview2/test.bin", "browser"); Put(cache, "app-update/test.bin", "update");
            App.Runtime.Storage.RefreshSpaceReport(CancellationToken.None); var report = Map(Luna.DataFormat.ReadRecord(Path.Combine(cache, "space-report.json"))); var sizes = Map(report["bytes"]);
            Check(Convert.ToInt64(sizes["browser"]) > 0 && Convert.ToInt64(sizes["updates"]) > 0 && Convert.ToInt64(report["totalBytes"]) == sizes.Values.Sum(v => Convert.ToInt64(v)), "Storage accounting omitted browser/update data");
            Pass("query-time integrity, persisted audit resume and categorized storage totals");

            string incoming = Runtime("incoming", "1.7.0"), archive = Archive(incoming, Path.Combine(Root, "offer.zip")), envelope = Envelope(archive, "1.7.0");
            var release = Luna.Deployment.VerifyEnvelope(envelope);
            var tampered = Map(Json.DeserializeObject(envelope)); tampered["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
            Reject(() => Luna.Deployment.VerifyEnvelope(Json.Serialize(tampered)), "Tampered signature accepted");
            Reject(() => Luna.Deployment.Under(Root, "../escape.txt"), "Path traversal accepted");
            Reject(() => Luna.Deployment.Under(Root, "cache/file:stream"), "Alternate data stream accepted");
            string malicious = Path.Combine(Root, "malicious.zip"); using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) { using (var stream = new StreamWriter(zip.CreateEntry("Luna/../escape.txt").Open())) stream.Write("bad"); }
            string maliciousEnvelope = Envelope(malicious, "1.7.0"); Reject(() => Luna.Deployment.ExtractVerified(malicious, Path.Combine(Root, "malicious-stage"), Luna.Deployment.VerifyEnvelope(maliciousEnvelope)), "Signed zip traversal accepted");
            Pass("publisher signature, package hash and archive path defenses");

            MaintenanceFaults(archive, envelope);

            string failed = Runtime("failed-install", "1.6.0"); Put(failed, "cache/settings.json", "user-settings"); Put(failed, "notes.txt", "personal"); Stage(failed, archive, envelope);
            string failedArea = Path.Combine(failed, "cache/app-update");
            Reject(() => Luna.Deployment.Apply(failed, Path.Combine(failedArea, "stage"), Path.Combine(failedArea, "release.json"), r => false), "Failed startup did not roll back");
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(failed, true), "version") == "1.6.0" && File.ReadAllText(Path.Combine(failed, "cache/settings.json")) == "user-settings", "Rollback lost old app/preferences");
            Luna.Deployment.Apply(failed, Path.Combine(failedArea, "stage"), Path.Combine(failedArea, "release.json"), r => true);
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(failed, true), "version") == "1.7.0" && File.Exists(Path.Combine(failedArea, "rollback/installed-files.json")), "Update/backup failed");
            Check(File.ReadAllText(Path.Combine(failed, "notes.txt")) == "personal", "Update changed an unowned file");
            // Recreate a journal left by an interrupted process and recover on next launch.
            Luna.Deployment.Write(Path.Combine(failedArea, "transaction.json"), new { state = "applying", added = new string[0] });
            Luna.Deployment.Restore(failed);
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(failed, true), "version") == "1.6.0", "Interrupted transaction did not recover");
            Pass("full update, failed health-check rollback, interrupted-transaction recovery and user-data preservation");

            string uninstall = Runtime("uninstall-keep", "1.6.0"); Put(uninstall, "cache/settings.json", "retain"); Put(uninstall, "personal.txt", "keep"); Directory.CreateDirectory(Path.Combine(uninstall, "empty-personal-folder"));
            Put(uninstall, "web/index.html", "locally modified"); int kept = Luna.Deployment.Uninstall(uninstall, false);
            Check(kept == 1 && File.Exists(Path.Combine(uninstall, "cache/settings.json")) && File.Exists(Path.Combine(uninstall, "personal.txt")) && Directory.Exists(Path.Combine(uninstall, "empty-personal-folder")), "Uninstall deleted protected files");
            string full = Runtime("uninstall-data", "1.6.0"); Put(full, "cache/settings.json", "delete by explicit choice"); Put(full, "personal.txt", "keep"); Luna.Deployment.Uninstall(full, true);
            Check(!Directory.Exists(Path.Combine(full, "cache")) && File.Exists(Path.Combine(full, "personal.txt")), "Full uninstall did not honor its boundary");
            string source = Runtime("source-protected", "1.6.0"); Directory.CreateDirectory(Path.Combine(source, "src")); Reject(() => Luna.Deployment.Uninstall(source, true), "Source directory was uninstallable");
            Pass("uninstall preserves settings by default, removes selected data, retains extra/modified files, rejects source folders");

            Set("AppRelease", release); Directory.CreateDirectory(Path.Combine(cache, "app-update"));
            byte[] package = File.ReadAllBytes(archive); int midpoint = package.Length / 2;
            string part = Path.Combine(cache, "app-update/package.part"); File.WriteAllBytes(part, package.Take(midpoint).ToArray());
            using (var server = new DownloadServer(package)) {
                Set("AppDownloadTransport", new Func<string, long, CancellationToken, HttpWebResponse>((url, offset, token) => server.Get(offset)));
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None);
                Check(server.Offsets.SequenceEqual(new[] { (long)midpoint }), "Download did not resume at saved byte offset");
                Check(File.Exists(Path.Combine(cache, "app-update/stage/installed-files.json")), "Resumed archive not staged");
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None); Check(server.Offsets.Count == 1, "Prepared update downloaded twice");
                Put(Root, "cache/app-update/stage/web/index.html", "interrupted extraction");
                Put(Root, "cache/app-update/stage/personal.txt", "keep this staged note");
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None);
                Check(server.Offsets.Count == 1 && Luna.Deployment.Text(Luna.Deployment.Manifest(Path.Combine(cache, "app-update/stage"), true), "version") == "1.7.0", "Damaged staged files did not recover from the authenticated local archive");
                string preservedStage = Directory.GetDirectories(Path.Combine(cache, "app-update"), "stage-preserved-*").Single();
                Check(File.ReadAllText(Path.Combine(preservedStage, "personal.txt")) == "keep this staged note", "Repair erased an unowned stage file");
                Put(Root, "cache/app-update/package.zip", "damaged completed download");
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None);
                Check(server.Offsets.Count == 2 && Luna.Deployment.HashFile(Path.Combine(cache, "app-update/package.zip")) == Luna.Deployment.HashFile(archive), "A damaged completed archive was reused instead of downloaded again");
                Check(Directory.GetFiles(Path.Combine(cache, "app-update"), "package-rejected-*.zip").Length == 1, "Rejected archive evidence was lost");
                Put(Root, "cache/app-update/stage/installed-files.json", "{");
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None);
                Check(server.Offsets.Count == 2 && Luna.Deployment.Text(Luna.Deployment.Manifest(Path.Combine(cache, "app-update/stage"), true), "version") == "1.7.0", "Damaged manifest retry did not rebuild locally");
                Runtime(".", Field("AppVersion").ToString());
                Put(Root, "cache/app-update/stage/web/index.html", "changed before install");
                bool refused = false;
                try { Call("PrepareMaintenance", "update", "false"); } catch (Exception) { refused = true; }
                Check(refused && Snapshot("AppUpdateInfo")["state"].ToString() == "error", "Damaged prepared update did not expose retry without exiting the app");
                App.Runtime.AppUpdates.DownloadAppRelease(CancellationToken.None);
                Pass("staged file/manifest corruption, unowned-file preservation and completed archive corruption recover on retry");
            }
            UpdatePreferences(archive, envelope);
            Set("Offline", true); Reject(() => App.Runtime.AppUpdates.StartAppUpdate("check"), "Offline mode made application requests");
            Pass("HTTP Range resume, no repeated download for verified package, and offline update restriction");
            Console.WriteLine("All lifecycle checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    sealed class DownloadServer : IDisposable {
        readonly System.Net.Sockets.TcpListener listener; readonly Thread worker; readonly byte[] bytes;
        internal readonly List<long> Offsets = new List<long>(); volatile bool running = true;
        internal DownloadServer(byte[] data) {
            bytes = data; listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
            worker = new Thread(Run) { IsBackground = true }; worker.Start();
        }
        internal HttpWebResponse Get(long offset) {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/package"); request.Proxy = null; request.Timeout = 3000; if (offset > 0) request.AddRange(offset); return (HttpWebResponse)request.GetResponse();
        }
        void Run() {
            try { while (running) using (var client = listener.AcceptTcpClient()) using (var stream = client.GetStream()) {
                var input = new StreamReader(stream, Encoding.ASCII, false, 1024, true); string line; long offset = 0;
                while (!String.IsNullOrEmpty(line = input.ReadLine())) if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) offset = long.Parse(line.Substring(13).TrimEnd('-'));
                Offsets.Add(offset);
                string headers = "HTTP/1.1 " + (offset == 0 ? "200 OK" : "206 Partial Content") + "\r\nContent-Length: " + (bytes.Length - offset) + "\r\n" + (offset == 0 ? "" : "Content-Range: bytes " + offset + "-" + (bytes.Length - 1) + "/" + bytes.Length + "\r\n") + "Connection: close\r\n\r\n";
                byte[] header = Encoding.ASCII.GetBytes(headers); stream.Write(header, 0, header.Length); stream.Write(bytes, (int)offset, bytes.Length - (int)offset);
            } } catch (Exception) { if (running) throw; }
        }
        public void Dispose() { running = false; listener.Stop(); worker.Join(1000); }
    }
}

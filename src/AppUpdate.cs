using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        const string ReleaseApi = "https://api.github.com/repos/Francis-Chenyj/Luna-dev/releases?per_page=15";
        static string AppUpdateDir { get { return Path.Combine(CacheDir, "app-update"); } }
        static readonly object AppUpdateSync = new object();
        static bool AppUpdateBusy;
        static string AppUpdateState = "idle", AppUpdateError = "";
        static long AppUpdateBytes;
        static CancellationTokenSource AppUpdateCancellation;
        static Dictionary<string, object> AppRelease;
        static bool AppUpdateAutomatic, AppUpdateDownloadAutomatic;
        static bool AppUpdatePendingCheck, AppUpdatePausedThisRun;
        static int AppUpdateScheduled;
        // Replaced only by isolated tests, never by an HTTP parameter or setting.
        static Func<string, CancellationToken, string> ReleaseTransport = ReadReleaseText;
        static Func<string, long, CancellationToken, HttpWebResponse> AppDownloadTransport = ReleaseResponse;
        static string UpdateFile(string name) {
            if (!PlainPath(AppUpdateDir)) throw new IOException("Unsafe update folder");
            return Path.Combine(AppUpdateDir, name);
        }
        static void LoadSavedAppRelease() {
            if (AppRelease != null) return;
            string path = UpdateFile("release.json");
            if (!File.Exists(path) || !SafeLocalPath(path)) return;
            try {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException();
                var saved = Deployment.VerifyEnvelope(File.ReadAllText(path, Encoding.UTF8));
                if (new Version(Deployment.Text(saved, "version")) > new Version(AppVersion)) AppRelease = saved;
            } catch { AppUpdateError = "invalidSignature"; }
        }
        static object AppUpdateInfo() {
            lock (AppUpdateSync) {
                LoadSavedAppRelease(); var status = ReadRecord(UpdateFile("check.json")); var result = ReadRecord(UpdateFile("result.json"));
                var committed = ReadRecord(UpdateFile("transaction.json"));
                if (Str(committed, "state") == "committed" && Str(committed, "version") == AppVersion && DateValue(Str(committed, "at")) > DateValue(Str(result, "at")))
                    result = new Dictionary<string, object> { { "state", "installed" }, { "version", AppVersion }, { "at", Str(committed, "at") } };
                string state = AppUpdateState;
                if (!AppUpdateBusy && state == "idle") {
                    if (AppRelease != null) state = File.Exists(UpdateFile("stage/installed-files.json")) ? "ready" : File.Exists(UpdateFile("package.part")) ? "paused" : "available";
                    else {
                        state = Str(status, "state", "idle");
                        if (Str(result, "state") == "installed" && Str(result, "version") == AppVersion && DateValue(Str(result, "at")) >= DateValue(Str(status, "checkedAt"))) state = "installed";
                        else if (state == "available" || state == "ready") state = AppUpdateError == "" ? "current" : "error";
                    }
                }
                return new { success = true, version = AppVersion, state = state, busy = AppUpdateBusy, error = AppUpdateError,
                    newVersion = AppRelease == null ? "" : Deployment.Text(AppRelease, "version"), notes = AppRelease == null ? "" : Deployment.Text(AppRelease, "notes"),
                    downloadedBytes = AppUpdateBusy ? AppUpdateBytes : File.Exists(UpdateFile("package.zip")) ? new FileInfo(UpdateFile("package.zip")).Length : File.Exists(UpdateFile("package.part")) ? new FileInfo(UpdateFile("package.part")).Length : 0,
                    totalBytes = AppRelease == null ? 0 : long.Parse(Deployment.Text(AppRelease, "bytes")), checkedAt = Str(status, "checkedAt"),
                    offline = Offline, managed = Deployment.Managed(Root), result = result };
            }
        }
        static HttpWebResponse ReleaseResponse(string url, long offset, CancellationToken token) {
            for (int redirects = 0; redirects < 5; redirects++) {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)) throw new IOException("Untrusted update URL");
                var request = (HttpWebRequest)WebRequest.Create(uri); request.UserAgent = "Luna/" + AppVersion;
                request.Timeout = 8000; request.ReadWriteTimeout = 5000; request.AllowAutoRedirect = false;
                if (offset > 0) request.AddRange(offset);
                HttpWebResponse response;
                using (token.Register(request.Abort)) { token.ThrowIfCancellationRequested(); response = (HttpWebResponse)request.GetResponse(); }
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400) { string next = response.Headers["Location"]; response.Dispose(); url = new Uri(uri, next).AbsoluteUri; continue; }
                return response;
            }
            throw new IOException("Too many release redirects");
        }
        static string ReadReleaseText(string url, CancellationToken token) {
            using (var response = ReleaseResponse(url, 0, token)) using (var input = response.GetResponseStream()) using (var output = new MemoryStream()) {
                using (token.Register(response.Close)) {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); if (output.Length > 2 * 1024 * 1024) throw new IOException("Release metadata too large"); }
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
        static void CheckAppRelease(CancellationToken token) {
            Directory.CreateDirectory(AppUpdateDir);
            WriteRecord(UpdateFile("attempt.json"), new { attemptedAt = Stamp() });
            object parsed = Json().DeserializeObject(ReleaseTransport(ReleaseApi, token));
            var releases = Arr(parsed).Select(Obj).Where(r => Get(r, "draft", true).Equals(false) && Get(r, "prerelease", true).Equals(false));
            Dictionary<string, object> best = null; string signed = null;
            foreach (var release in releases) {
                token.ThrowIfCancellationRequested();
                var asset = Arr(Get(release, "assets")).Select(Obj).FirstOrDefault(a => Str(a, "name") == "luna-update.json");
                if (asset == null) continue;
                string url = Str(asset, "browser_download_url");
                if (!url.StartsWith("https://github.com/Francis-Chenyj/Luna-dev/releases/download/", StringComparison.Ordinal)) throw new IOException("Invalid release metadata URL");
                string envelope = ReleaseTransport(url, token); var candidate = Deployment.VerifyEnvelope(envelope);
                if (best == null || new Version(Deployment.Text(candidate, "version")) > new Version(Deployment.Text(best, "version"))) { best = candidate; signed = envelope; }
            }
            Directory.CreateDirectory(AppUpdateDir);
            string state = best == null ? "noRelease" : new Version(Deployment.Text(best, "version")) <= new Version(AppVersion) ? "current" : "available";
            if (state == "available") {
                lock (AppUpdateSync) {
                    LoadSavedAppRelease();
                    // Retain an already authenticated newer offer if a feed is stale.
                    if (AppRelease != null && new Version(Deployment.Text(AppRelease, "version")) > new Version(Deployment.Text(best, "version"))) { best = AppRelease; state = "available"; }
                    else {
                        if (AppRelease != null && Deployment.Text(AppRelease, "sha256") != Deployment.Text(best, "sha256")) DiscardDownload();
                        WriteRecord(UpdateFile("release.json"), Json().DeserializeObject(signed)); AppRelease = best;
                    }
                }
            }
            lock (AppUpdateSync) { if (AppRelease != null) state = File.Exists(UpdateFile("stage/installed-files.json")) ? "ready" : "available"; AppUpdateState = state; }
            WriteRecord(UpdateFile("check.json"), new { state = state, checkedAt = Stamp() });
        }
        static void DiscardDownload() {
            Deployment.DiscardUpdateStage(AppUpdateDir);
            foreach (string name in new[] { "package.part", "package.zip" }) { string path = UpdateFile(name); if (File.Exists(path) && SafeLocalPath(path)) File.Delete(path); }
        }
        static void DownloadAppRelease(CancellationToken token) {
            Dictionary<string, object> release;
            lock (AppUpdateSync) { LoadSavedAppRelease(); release = AppRelease; AppUpdateState = "downloading"; }
            if (release == null) throw new IOException("No authenticated update is available");
            Directory.CreateDirectory(AppUpdateDir);
            string part = UpdateFile("package.part"), archive = UpdateFile("package.zip"), stage = UpdateFile("stage");
            long length = long.Parse(Deployment.Text(release, "bytes"));
            if (!SafeLocalPath(part) || !SafeLocalPath(archive)) throw new IOException("Unsafe update path");
            if (File.Exists(archive) && (new FileInfo(archive).Length != length || Deployment.HashFile(archive) != Deployment.Text(release, "sha256")))
                File.Move(archive, UpdateFile("package-rejected-" + Guid.NewGuid().ToString("N") + ".zip"));
            if (!File.Exists(archive)) {
                long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (offset > length) { File.Delete(part); offset = 0; }
                if (offset < length) using (var response = AppDownloadTransport(Deployment.Text(release, "url"), offset, token)) {
                    if (response.StatusCode == HttpStatusCode.PartialContent) {
                        string expected = "bytes " + offset + "-";
                        if (!(response.Headers["Content-Range"] ?? "").StartsWith(expected, StringComparison.Ordinal) || !(response.Headers["Content-Range"] ?? "").EndsWith("/" + length, StringComparison.Ordinal)) throw new IOException("Invalid resumed response");
                    } else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                    else throw new IOException("Unexpected download response");
                    using (var output = new FileStream(LocalPaths.Io(part), offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Read)) using (var input = response.GetResponseStream()) using (token.Register(response.Close)) {
                        byte[] buffer = new byte[32768]; int count; long flush = offset;
                        AppUpdateBytes = offset;
                        try {
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0) {
                                token.ThrowIfCancellationRequested(); if (output.Length + count > length) throw new IOException("Download exceeds the signed size");
                                output.Write(buffer, 0, count); Interlocked.Exchange(ref AppUpdateBytes, output.Length);
                                if (output.Length - flush >= 262144) { output.Flush(true); flush = output.Length; YieldMaintenance(token); }
                            }
                        } finally { output.Flush(true); }
                    }
                }
                token.ThrowIfCancellationRequested();
                if (new FileInfo(part).Length != length || Deployment.HashFile(part) != Deployment.Text(release, "sha256")) { File.Delete(part); throw new IOException("Package checksum mismatch"); }
                File.Move(part, archive);
            }
            token.ThrowIfCancellationRequested();
            lock (AppUpdateSync) AppUpdateState = "verifying";
            if (Directory.Exists(stage)) {
                bool verified = false;
                try {
                    Deployment.VerifyPreparedUpdate(stage, archive, release); verified = true;
                } catch (Exception ex) {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                    verified = false;
                }
                if (verified) { lock (AppUpdateSync) AppUpdateState = "ready"; return; }
                Deployment.PreserveUpdateDirectory(AppUpdateDir, "stage");
            }
            Deployment.ExtractVerified(archive, stage, release);
            lock (AppUpdateSync) AppUpdateState = "ready";
        }
        static void StartAppUpdate(string action) { BeginAppUpdate(action, false); }
        static void BeginAppUpdate(string action, bool automatic) {
            if (automatic && (Offline || CommandLineDisablesUpdates || Shutdown.IsCancellationRequested)) return;
            if (Offline) throw new SettingsException("appUpdate.offline");
            if (action != "check" && action != "download") throw new SettingsException("settings.invalid");
            lock (AppUpdateSync) {
                if (automatic) {
                    string warning; var preferences = ReadPreferences(out warning);
                    if (!preferences.appAutoCheck || AppUpdatePausedThisRun || (action == "download" && !preferences.appAutoDownload)) return;
                } else AppUpdatePausedThisRun = false;
                if (AppUpdateBusy) {
                    // Reuse a live check/download. If it is still cancelling, retain
                    // one explicit enable request and start it after cleanup.
                    if (automatic && action == "check" && AppUpdateCancellation.IsCancellationRequested) AppUpdatePendingCheck = true;
                    return;
                }
                AppUpdateAutomatic = automatic; AppUpdateDownloadAutomatic = automatic && action == "download";
                AppUpdateBusy = true; AppUpdateError = ""; AppUpdateState = action == "check" ? "checking" : "downloading";
                AppUpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
            }
            var thread = new Thread(() => {
                using (var mutex = new Mutex(false, "Local\\LunaAppUpdate_" + Hash(Root.ToLowerInvariant()).Substring(0, 20))) {
                    bool held = false;
                    try {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("Another updater is running");
                        var token = AppUpdateCancellation.Token;
                        if (action == "check") CheckAppRelease(token);
                        bool download;
                        lock (AppUpdateSync) {
                            string warning; var preferences = ReadPreferences(out warning);
                            download = action == "download" ? (!automatic || preferences.appAutoDownload) : (preferences.appAutoCheck && preferences.appAutoDownload && AppRelease != null);
                            AppUpdateDownloadAutomatic = download && (automatic || action != "download");
                        }
                        token.ThrowIfCancellationRequested();
                        if (download) DownloadAppRelease(token);
                    } catch (Exception ex) {
                        lock (AppUpdateSync) { AppUpdateState = AppUpdateCancellation.IsCancellationRequested ? "paused" : "error"; AppUpdateError = AppUpdateState == "error" ? "failed" : ""; }
                        if (!Shutdown.IsCancellationRequested) Log(ex);
                    } finally {
                        if (held) mutex.ReleaseMutex();
                        bool restart;
                        lock (AppUpdateSync) {
                            AppUpdateBusy = false; AppUpdateAutomatic = false; AppUpdateDownloadAutomatic = false;
                            AppUpdateCancellation.Dispose(); AppUpdateCancellation = null;
                            restart = AppUpdatePendingCheck; AppUpdatePendingCheck = false;
                        }
                        if (restart) BeginAppUpdate("check", true);
                    }
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Luna app update" };
            thread.Start();
        }
        static void CancelAppUpdate() {
            lock (AppUpdateSync) {
                AppUpdatePausedThisRun = true; AppUpdatePendingCheck = false;
                if (AppUpdateCancellation != null) AppUpdateCancellation.Cancel();
            }
        }
        static void AppUpdatePreferenceChanged(string key, bool enabled) {
            lock (AppUpdateSync) {
                string warning; var preferences = ReadPreferences(out warning);
                if (!preferences.appAutoCheck) AppUpdatePendingCheck = false;
                if ((!preferences.appAutoCheck && AppUpdateAutomatic) || (!preferences.appAutoDownload && AppUpdateDownloadAutomatic)) {
                    if (AppUpdateCancellation != null) AppUpdateCancellation.Cancel();
                }
                if (enabled && preferences.appAutoCheck && (key == "appAutoCheck" || preferences.appAutoDownload)) {
                    AppUpdatePausedThisRun = false;
                    BeginAppUpdate("check", true);
                }
            }
        }
        static void MaintainAppDownloads() {
            lock (AppUpdateSync) {
                if (AppUpdateBusy || !Directory.Exists(AppUpdateDir)) return;
                var files = new[] { UpdateFile("package.part"), UpdateFile("package.zip") }.Where(File.Exists).ToArray();
                if (files.Length > 0 && files.All(p => SafeLocalPath(p) && File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7)) && !Deployment.NeedsRecovery(Root))
                    try { DiscardDownload(); AppUpdateState = AppRelease == null ? "idle" : "available"; } catch (IOException) { }
            }
        }
        static void ScheduleAppUpdate() {
            if (Offline || CommandLineDisablesUpdates || Interlocked.Exchange(ref AppUpdateScheduled, 1) != 0) return;
            var worker = new Thread(() => {
                if (Shutdown.Token.WaitHandle.WaitOne(8000)) return;
                try { CheckScheduledAppUpdate(); } catch (Exception ex) { Log(ex); }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }
        static void CheckScheduledAppUpdate() {
            lock (AppUpdateSync) {
                // Read the current choice after the delay, rather than a launch-time
                // snapshot that could restart a download the user just disabled.
                string warning; var preferences = ReadPreferences(out warning);
                if (Offline || CommandLineDisablesUpdates || !preferences.appAutoCheck || AppUpdatePausedThisRun || AppUpdateBusy) return;
                var state = ReadRecord(UpdateFile("check.json")); var attempt = ReadRecord(UpdateFile("attempt.json"));
                if (DateValue(Str(state, "checkedAt")) <= DateTime.UtcNow.AddDays(-1) && DateValue(Str(attempt, "attemptedAt")) <= DateTime.UtcNow.AddHours(-1)) BeginAppUpdate("check", true);
                else if (preferences.appAutoDownload) { LoadSavedAppRelease(); if (AppRelease != null) BeginAppUpdate("download", true); }
            }
        }
        static object MaintenanceInfo() {
            return new { success = true, available = Deployment.Managed(Root), path = Root.TrimEnd('\\'), sharedRuntimeRemoved = false };
        }
        static void PrepareMaintenance(string action, string removeData) {
            if (!Deployment.Managed(Root)) throw new SettingsException("maintenance.sourceFolder");
            if (action != "update" && action != "uninstall" || removeData != "true" && removeData != "false") throw new SettingsException("settings.invalid");
            if (action == "update") {
                lock (AppUpdateSync) {
                    if (AppUpdateBusy) throw new SettingsException("settings.busy");
                    LoadSavedAppRelease(); if (AppRelease == null) throw new SettingsException("appUpdate.noRelease");
                    try { Deployment.VerifyPreparedUpdate(UpdateFile("stage"), UpdateFile("package.zip"), AppRelease); }
                    catch (Exception ex) {
                        if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                        AppUpdateState = "error"; AppUpdateError = "failed";
                        throw new SettingsException("appUpdate.failed");
                    }
                }
            }
            CancelAppUpdate(); CancelLibraryUpdate();
            Deployment.StartWorker(Root, action, removeData == "true", Process.GetCurrentProcess().Id);
        }
    }
}

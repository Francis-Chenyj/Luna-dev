using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Luna {
    internal sealed class AppUpdateService {
        readonly RuntimeOptions options;
        readonly BackgroundWork workers;
        readonly PreferenceStore preferenceStore;
        readonly ReleaseClient releaseClient;
        readonly QueryActivity activity;
        readonly DiagnosticLog diagnostics;
        internal AppUpdateService(RuntimeOptions options, BackgroundWork workers, PreferenceStore preferenceStore, ReleaseClient releaseClient, QueryActivity activity, DiagnosticLog diagnostics) {
            this.options = options;
            this.workers = workers;
            this.preferenceStore = preferenceStore;
            this.releaseClient = releaseClient;
            this.activity = activity;
            this.diagnostics = diagnostics;
        }
        const string ReleaseApi = "https://api.github.com/repos/Francis-Chenyj/Luna-dev/releases?per_page=15";
        readonly object AppUpdateSync = new object();
        bool AppUpdateBusy;
        string AppUpdateState = "idle", AppUpdateError = "";
        long AppUpdateBytes;
        CancellationTokenSource AppUpdateCancellation;
        Dictionary<string, object> AppRelease;
        bool AppUpdateAutomatic, AppUpdateDownloadAutomatic;
        bool AppUpdatePendingCheck, AppUpdatePausedThisRun;
        int AppUpdateScheduled;
        Mutex UpdateMutex() { return new Mutex(false, "Local\\LunaAppUpdate_" + DataFormat.Hash(options.Root.ToLowerInvariant()).Substring(0, 20)); }
        string UpdateFile(string name) {
            if (!DataFormat.PlainPath(options.AppUpdateDir)) throw new IOException("Unsafe update folder");
            return Path.Combine(options.AppUpdateDir, name);
        }
        void LoadSavedAppRelease() {
            if (AppRelease != null) return;
            string path = UpdateFile("release.json");
            if (!File.Exists(path) || !DataFormat.SafeLocalPath(path)) return;
            try {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException();
                var saved = Deployment.VerifyEnvelope(File.ReadAllText(path, Encoding.UTF8));
                if (new Version(Deployment.Text(saved, "version")) > new Version(PreferenceStore.AppVersion)) AppRelease = saved;
            } catch { AppUpdateError = "invalidSignature"; }
        }
        internal object AppUpdateInfo() {
            lock (AppUpdateSync) {
                LoadSavedAppRelease(); var status = DataFormat.ReadRecord(UpdateFile("check.json")); var result = DataFormat.ReadRecord(UpdateFile("result.json"));
                var committed = DataFormat.ReadRecord(UpdateFile("transaction.json"));
                if (DataFormat.Str(committed, "state") == "committed" && DataFormat.Str(committed, "version") == PreferenceStore.AppVersion && DataFormat.DateValue(DataFormat.Str(committed, "at")) > DataFormat.DateValue(DataFormat.Str(result, "at")))
                    result = new Dictionary<string, object> { { "state", "installed" }, { "version", PreferenceStore.AppVersion }, { "at", DataFormat.Str(committed, "at") } };
                string state = AppUpdateState;
                if (!AppUpdateBusy && state == "idle") {
                    if (AppRelease != null) state = File.Exists(UpdateFile("stage/installed-files.json")) ? "ready" : File.Exists(UpdateFile("package.part")) ? "paused" : "available";
                    else {
                        state = DataFormat.Str(status, "state", "idle");
                        if (DataFormat.Str(result, "state") == "installed" && DataFormat.Str(result, "version") == PreferenceStore.AppVersion && DataFormat.DateValue(DataFormat.Str(result, "at")) >= DataFormat.DateValue(DataFormat.Str(status, "checkedAt"))) state = "installed";
                        else if (state == "available" || state == "ready") state = AppUpdateError == "" ? "current" : "error";
                    }
                }
                return new { success = true, version = PreferenceStore.AppVersion, state = state, busy = AppUpdateBusy, error = AppUpdateError,
                    newVersion = AppRelease == null ? "" : Deployment.Text(AppRelease, "version"), notes = AppRelease == null ? "" : Deployment.Text(AppRelease, "notes"),
                    downloadedBytes = AppUpdateBusy ? AppUpdateBytes : File.Exists(UpdateFile("package.zip")) ? new FileInfo(UpdateFile("package.zip")).Length : File.Exists(UpdateFile("package.part")) ? new FileInfo(UpdateFile("package.part")).Length : 0,
                    totalBytes = AppRelease == null ? 0 : long.Parse(Deployment.Text(AppRelease, "bytes")), checkedAt = DataFormat.Str(status, "checkedAt"),
                    offline = options.Offline, managed = Deployment.Managed(options.Root), result = result };
            }
        }
        void CheckAppRelease(CancellationToken token) {
            Directory.CreateDirectory(options.AppUpdateDir);
            DataFormat.WriteRecord(UpdateFile("attempt.json"), new { attemptedAt = DataFormat.Stamp() });
            object parsed = DataFormat.Json().DeserializeObject(releaseClient.Read(ReleaseApi, token));
            var releases = DataFormat.Arr(parsed).Select(DataFormat.Obj).Where(r => DataFormat.Get(r, "draft", true).Equals(false) && DataFormat.Get(r, "prerelease", true).Equals(false));
            Dictionary<string, object> best = null; string signed = null;
            foreach (var release in releases) {
                token.ThrowIfCancellationRequested();
                var asset = DataFormat.Arr(DataFormat.Get(release, "assets")).Select(DataFormat.Obj).FirstOrDefault(a => DataFormat.Str(a, "name") == "luna-update.json");
                if (asset == null) continue;
                string url = DataFormat.Str(asset, "browser_download_url");
                if (!url.StartsWith("https://github.com/Francis-Chenyj/Luna-dev/releases/download/", StringComparison.Ordinal)) throw new IOException("Invalid release metadata URL");
                string envelope = releaseClient.Read(url, token); var candidate = Deployment.VerifyEnvelope(envelope);
                if (best == null || new Version(Deployment.Text(candidate, "version")) > new Version(Deployment.Text(best, "version"))) { best = candidate; signed = envelope; }
            }
            Directory.CreateDirectory(options.AppUpdateDir);
            string state = best == null ? "noRelease" : new Version(Deployment.Text(best, "version")) <= new Version(PreferenceStore.AppVersion) ? "current" : "available";
            if (state == "available") {
                lock (AppUpdateSync) {
                    LoadSavedAppRelease();
                    // Retain an already authenticated newer offer if a feed is stale.
                    if (AppRelease != null && new Version(Deployment.Text(AppRelease, "version")) > new Version(Deployment.Text(best, "version"))) { best = AppRelease; state = "available"; }
                    else {
                        if (AppRelease != null && Deployment.Text(AppRelease, "sha256") != Deployment.Text(best, "sha256")) DiscardDownload();
                        DataFormat.WriteRecord(UpdateFile("release.json"), DataFormat.Json().DeserializeObject(signed)); AppRelease = best;
                    }
                }
            }
            lock (AppUpdateSync) { if (AppRelease != null) state = File.Exists(UpdateFile("stage/installed-files.json")) ? "ready" : "available"; AppUpdateState = state; }
            DataFormat.WriteRecord(UpdateFile("check.json"), new { state = state, checkedAt = DataFormat.Stamp() });
        }
        internal void DiscardDownload() {
            Deployment.DiscardUpdateStage(options.AppUpdateDir);
            foreach (string name in new[] { "package.part", "package.zip" }) { string path = UpdateFile(name); if (File.Exists(path) && DataFormat.SafeLocalPath(path)) File.Delete(path); }
        }
        internal void DownloadAppRelease(CancellationToken token) {
            Dictionary<string, object> release;
            lock (AppUpdateSync) { LoadSavedAppRelease(); release = AppRelease; AppUpdateState = "downloading"; }
            if (release == null) throw new IOException("No authenticated update is available");
            Directory.CreateDirectory(options.AppUpdateDir);
            string part = UpdateFile("package.part"), archive = UpdateFile("package.zip"), stage = UpdateFile("stage");
            long length = long.Parse(Deployment.Text(release, "bytes"));
            if (!DataFormat.SafeLocalPath(part) || !DataFormat.SafeLocalPath(archive)) throw new IOException("Unsafe update path");
            if (File.Exists(archive) && (new FileInfo(archive).Length != length || Deployment.HashFile(archive) != Deployment.Text(release, "sha256")))
                File.Move(archive, UpdateFile("package-rejected-" + Guid.NewGuid().ToString("N") + ".zip"));
            if (!File.Exists(archive)) {
                long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (offset > length) { File.Delete(part); offset = 0; }
                if (offset < length) using (var response = releaseClient.Download(Deployment.Text(release, "url"), offset, token)) {
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
                                if (output.Length - flush >= 262144) { output.Flush(true); flush = output.Length; activity.YieldMaintenance(token); }
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
                Deployment.PreserveUpdateDirectory(options.AppUpdateDir, "stage");
            }
            Deployment.ExtractVerified(archive, stage, release);
            lock (AppUpdateSync) AppUpdateState = "ready";
        }
        internal void StartAppUpdate(string action) { BeginAppUpdate(action, false); }
        void BeginAppUpdate(string action, bool automatic) {
            if (workers.IsCancellationRequested || automatic && (options.Offline || options.CommandLineDisablesUpdates)) return;
            CancellationTokenSource cancellation;
            if (options.Offline) throw new PreferenceStore.SettingsException("appUpdate.offline");
            if (action != "check" && action != "download") throw new PreferenceStore.SettingsException("settings.invalid");
            lock (AppUpdateSync) {
                if (automatic) {
                    string warning; var preferences = preferenceStore.ReadPreferences(out warning);
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
                AppUpdateCancellation = cancellation = CancellationTokenSource.CreateLinkedTokenSource(workers.Token);
            }
            if (!workers.TryStart(() => {
                using (var mutex = UpdateMutex()) {
                    bool held = false;
                    try {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("Another updater is running");
                        var token = cancellation.Token;
                        if (action == "check") CheckAppRelease(token);
                        bool download;
                        lock (AppUpdateSync) {
                            string warning; var preferences = preferenceStore.ReadPreferences(out warning);
                            download = action == "download" ? (!automatic || preferences.appAutoDownload) : (preferences.appAutoCheck && preferences.appAutoDownload && AppRelease != null);
                            AppUpdateDownloadAutomatic = download && (automatic || action != "download");
                        }
                        token.ThrowIfCancellationRequested();
                        if (download) DownloadAppRelease(token);
                    } catch (Exception ex) {
                        lock (AppUpdateSync) { AppUpdateState = cancellation.IsCancellationRequested ? "paused" : "error"; AppUpdateError = AppUpdateState == "error" ? "failed" : ""; }
                        if (!workers.IsCancellationRequested) diagnostics.Log(ex);
                    } finally {
                        if (held) mutex.ReleaseMutex();
                        bool restart;
                        lock (AppUpdateSync) {
                            AppUpdateBusy = false; AppUpdateAutomatic = false; AppUpdateDownloadAutomatic = false;
                            cancellation.Dispose(); AppUpdateCancellation = null;
                            restart = AppUpdatePendingCheck; AppUpdatePendingCheck = false;
                        }
                        if (restart) BeginAppUpdate("check", true);
                    }
                }
            }, "Luna app update")) {
                lock (AppUpdateSync) { AppUpdateBusy = false; cancellation.Dispose(); AppUpdateCancellation = null; }
            }
        }
        internal void CancelAppUpdate() {
            lock (AppUpdateSync) {
                AppUpdatePausedThisRun = true; AppUpdatePendingCheck = false;
                if (AppUpdateCancellation != null) AppUpdateCancellation.Cancel();
            }
        }
        internal void AppUpdatePreferenceChanged(string key, bool enabled) {
            lock (AppUpdateSync) {
                string warning; var preferences = preferenceStore.ReadPreferences(out warning);
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
        internal void MaintainAppDownloads() {
            lock (AppUpdateSync) {
                if (AppUpdateBusy || !Directory.Exists(options.AppUpdateDir)) return;
                // Another process or runtime may own this same directory. Never wait
                // under AppUpdateSync: an active worker needs that lock to finish.
                using (var mutex = UpdateMutex()) {
                    bool held = false;
                    try {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) return;
                        var files = new[] { UpdateFile("package.part"), UpdateFile("package.zip") }.Where(File.Exists).ToArray();
                        if (files.Length > 0 && files.All(p => DataFormat.SafeLocalPath(p) && File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7)) && !Deployment.NeedsRecovery(options.Root))
                            try { DiscardDownload(); AppUpdateState = AppRelease == null ? "idle" : "available"; } catch (IOException) { }
                    } finally { if (held) mutex.ReleaseMutex(); }
                }
            }
        }
        internal void ScheduleAppUpdate() {
            if (options.Offline || options.CommandLineDisablesUpdates || Interlocked.Exchange(ref AppUpdateScheduled, 1) != 0) return;
            if (!workers.TryStart(() => {
                if (workers.Token.WaitHandle.WaitOne(8000)) return;
                try { CheckScheduledAppUpdate(); } catch (Exception ex) { diagnostics.Log(ex); }
            }, "Luna scheduled app update")) Interlocked.Exchange(ref AppUpdateScheduled, 0);
        }
        internal void CheckScheduledAppUpdate() {
            lock (AppUpdateSync) {
                // Read the current choice after the delay, rather than a launch-time
                // snapshot that could restart a download the user just disabled.
                string warning; var preferences = preferenceStore.ReadPreferences(out warning);
                if (options.Offline || options.CommandLineDisablesUpdates || !preferences.appAutoCheck || AppUpdatePausedThisRun || AppUpdateBusy) return;
                var state = DataFormat.ReadRecord(UpdateFile("check.json")); var attempt = DataFormat.ReadRecord(UpdateFile("attempt.json"));
                if (DataFormat.DateValue(DataFormat.Str(state, "checkedAt")) <= DateTime.UtcNow.AddDays(-1) && DataFormat.DateValue(DataFormat.Str(attempt, "attemptedAt")) <= DateTime.UtcNow.AddHours(-1)) BeginAppUpdate("check", true);
                else if (preferences.appAutoDownload) { LoadSavedAppRelease(); if (AppRelease != null) BeginAppUpdate("download", true); }
            }
        }
        internal object MaintenanceInfo() {
            return new { success = true, available = Deployment.Managed(options.Root), path = options.Root.TrimEnd('\\'), sharedRuntimeRemoved = false };
        }
        internal void PrepareMaintenance(string action, string removeData, Action cancelData) {
            if (!Deployment.Managed(options.Root)) throw new PreferenceStore.SettingsException("maintenance.sourceFolder");
            if (action != "update" && action != "uninstall" || removeData != "true" && removeData != "false") throw new PreferenceStore.SettingsException("settings.invalid");
            if (action == "update") {
                lock (AppUpdateSync) {
                    if (AppUpdateBusy) throw new PreferenceStore.SettingsException("settings.busy");
                    LoadSavedAppRelease(); if (AppRelease == null) throw new PreferenceStore.SettingsException("appUpdate.noRelease");
                    try { Deployment.VerifyPreparedUpdate(UpdateFile("stage"), UpdateFile("package.zip"), AppRelease); }
                    catch (Exception ex) {
                        if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                        AppUpdateState = "error"; AppUpdateError = "failed";
                        throw new PreferenceStore.SettingsException("appUpdate.failed");
                    }
                }
            }
            CancelAppUpdate(); cancelData();
            Deployment.StartWorker(options.Root, action, removeData == "true", Process.GetCurrentProcess().Id);
        }
    }
}

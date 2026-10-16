using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace Luna {
    internal sealed class StorageMaintenance {
        readonly RuntimeOptions options;
        readonly BackgroundWork workers;
        readonly LibraryStore store;
        readonly AppUpdateService appUpdates;
        readonly QuarantineStore quarantine;
        readonly QueryActivity activity;
        readonly DiagnosticLog diagnostics;
        readonly IRuntimeClock clock;
        internal StorageMaintenance(RuntimeOptions options, BackgroundWork workers, LibraryStore store, AppUpdateService appUpdates, QuarantineStore quarantine, QueryActivity activity, DiagnosticLog diagnostics, IRuntimeClock clock) {
            this.options = options;
            this.workers = workers;
            this.store = store;
            this.appUpdates = appUpdates;
            this.quarantine = quarantine;
            this.activity = activity;
            this.diagnostics = diagnostics;
            this.clock = clock;
        }
        const int ResumeRetentionDays = 7;
        int StorageWorker;
        long StorageNextCheck;
        volatile bool StorageWaiting;
        sealed class StorageReport {
            public bool ready, busy, deferred;
            public long downloadedBytes, freedBytes;
            public int removedProfiles;
            public string checkedAt = "", message = "正在检查本地空间…";
            public int resumeDays = ResumeRetentionDays;
        }
        volatile StorageReport Storage = new StorageReport();
        sealed class StoredProfile {
            public string Path, Fingerprint, Profile;
            public bool Complete;
            public DateTime ProgressAt;
            public string[] Files;
        }

        internal bool LibraryPath(string path) {
            try {
                string full = Path.GetFullPath(path), root = Path.GetFullPath(options.LibraryDir).TrimEnd(Path.DirectorySeparatorChar);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !DataFormat.PlainPath(full)) return false;
                return !File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
            } catch { return false; }
        }
        bool OwnedRecordName(string name, string profile) {
            if (!name.EndsWith(".json.gz", StringComparison.Ordinal)) return false;
            string key = name.Substring(0, name.Length - 8);
            return DataFormat.ValidKey(key) && DataFormat.ProfileOf(key) == profile;
        }
        bool OwnedTempName(string name, string profile) {
            if (DataFormat.ShortTempName(name)) return true;
            var match = Regex.Match(name, @"^(.*)\.[a-f0-9]{32}\.tmp$");
            return match.Success && (match.Groups[1].Value == "manifest.json" || OwnedRecordName(match.Groups[1].Value, profile));
        }
        StoredProfile InspectStoredProfile(string path, string fingerprint, string profile) {
            try {
                if (!LibraryPath(path) || Directory.GetDirectories(path).Length != 0) return null;
                string[] files = Directory.GetFiles(path);
                DateTime progress = Directory.GetLastWriteTimeUtc(path);
                var written = new List<DateTime>(); bool complete = false;
                foreach (string file in files) {
                    if (!LibraryPath(file)) return null;
                    string name = Path.GetFileName(file);
                    if (name == "manifest.json") {
                        var manifest = DataFormat.ReadRecord(file);
                        if (manifest == null || DataFormat.Str(manifest, "schema") != "1" || DataFormat.Str(manifest, "profile") != profile || DataFormat.Str(manifest, "fingerprint") != fingerprint) return null;
                        complete = true;
                    } else if (OwnedRecordName(name, profile)) written.Add(File.GetLastWriteTimeUtc(file));
                    else if (!OwnedTempName(name, profile)) return null;
                }
                if (written.Count > 0) progress = written.Max();
                return new StoredProfile { Path = path, Fingerprint = fingerprint, Profile = profile, Complete = complete, ProgressAt = progress, Files = files };
            } catch { return null; }
        }
        bool DeleteOwnedFile(string path, ref long freed) {
            try {
                if (!LibraryPath(path)) return false;
                if (!File.Exists(path)) return true;
                long bytes = new FileInfo(path).Length;
                File.Delete(path); freed += bytes; return true;
            } catch { return false; }
        }
        bool DeleteOwnedProfile(StoredProfile profile, ref long freed, CancellationToken token) {
            // Recheck immediately before deleting; never use a recursive directory delete.
            var current = InspectStoredProfile(profile.Path, profile.Fingerprint, profile.Profile);
            if (current == null) return false;
            bool complete = true;
            foreach (string file in current.Files.OrderBy(f => Path.GetFileName(f) == "manifest.json" ? 1 : 0)) {
                token.ThrowIfCancellationRequested();
                if (!complete && Path.GetFileName(file) == "manifest.json") continue;
                if (!DeleteOwnedFile(file, ref freed)) complete = false;
            }
            try {
                if (LibraryPath(profile.Path) && Directory.Exists(profile.Path) && Directory.GetFileSystemEntries(profile.Path).Length == 0)
                    Directory.Delete(profile.Path, false);
                else complete = false;
            } catch { complete = false; }
            return complete;
        }
        long DownloadedLibraryBytes() {
            long bytes = store.FocusBytes();
            if (!Directory.Exists(options.LibraryDir) || !DataFormat.PlainPath(options.LibraryDir)) return bytes;
            var pending = new Stack<string>(); pending.Push(options.LibraryDir);
            while (pending.Count > 0) {
                string directory = pending.Pop();
                try {
                    foreach (string file in Directory.GetFiles(directory)) if (LibraryPath(file)) bytes += new FileInfo(file).Length;
                    foreach (string child in Directory.GetDirectories(directory)) if (LibraryPath(child)) pending.Push(child);
                } catch { }
            }
            return bytes;
        }
        string KnownSourceFingerprint(DateTime now) {
            var source = DataFormat.ReadRecord(Path.Combine(options.LibraryDir, "source-state.json"));
            string fingerprint = DataFormat.Str(source, "fingerprint");
            DateTime checkedAt = DataFormat.DateValue(DataFormat.Str(source, "checkedAt"));
            return DataFormat.Fingerprint(fingerprint) && checkedAt <= now.AddMinutes(5) && checkedAt >= now.AddDays(-ResumeRetentionDays) ? fingerprint : "";
        }
        internal void MaintainStorage(string latestFingerprint, CancellationToken token) {
            StorageWaiting = false;
            var prior = Storage;
            Storage = new StorageReport { busy = true, ready = prior.ready, downloadedBytes = prior.downloadedBytes, freedBytes = prior.freedBytes, checkedAt = prior.checkedAt, message = "正在自动整理离线数据…" };
            long freed = 0; int removed = 0; bool deferred = false;
            try {
                DateTime now = clock.UtcNow;
                quarantine.MaintainExceptionalFiles();
                appUpdates.MaintainAppDownloads();
                store.MaintainFocused();
                if (!Directory.Exists(options.LibraryDir)) return;
                if (!DataFormat.PlainPath(options.LibraryDir)) { deferred = true; return; }
                if (!DataFormat.Fingerprint(latestFingerprint)) latestFingerprint = KnownSourceFingerprint(now);
                var candidates = new List<StoredProfile>();
                foreach (string directory in Directory.GetDirectories(options.LibraryDir)) {
                    token.ThrowIfCancellationRequested();
                    string fingerprint = Path.GetFileName(directory);
                    if (!DataFormat.Fingerprint(fingerprint)) continue;
                    if (!LibraryPath(directory)) { deferred = true; continue; }
                    foreach (string child in Directory.GetDirectories(directory)) {
                        string profile = Path.GetFileName(child);
                        if (!Regex.IsMatch(profile, @"^[1-5]-[1-5]$")) continue;
                        var candidate = InspectStoredProfile(child, fingerprint, profile);
                        if (candidate == null) deferred = true; else candidates.Add(candidate);
                    }
                }
                foreach (var group in candidates.GroupBy(p => p.Profile)) {
                    token.ThrowIfCancellationRequested();
                    string profile = group.Key, pointerPath = Path.Combine(options.LibraryDir, "active-" + profile + ".json");
                    var pointer = DataFormat.ReadRecord(pointerPath);
                    var keep = new HashSet<string>(StringComparer.Ordinal);
                    bool uncertain = false;
                    if (File.Exists(pointerPath)) {
                        if (!LibraryPath(pointerPath) || pointer == null || !DataFormat.Fingerprint(DataFormat.Str(pointer, "current")) || (DataFormat.Str(pointer, "previous") != "" && !DataFormat.Fingerprint(DataFormat.Str(pointer, "previous")))) uncertain = true;
                        else {
                            keep.Add(DataFormat.Str(pointer, "current"));
                            if (DataFormat.Fingerprint(DataFormat.Str(pointer, "previous"))) keep.Add(DataFormat.Str(pointer, "previous"));
                            // A broken active edition must never cause the removal of potentially useful complete data.
                            string activePath = Path.Combine(options.LibraryDir, DataFormat.Str(pointer, "current"), profile);
                            if (!LibraryPath(activePath) || !store.IsIntactProfile(activePath, profile)) uncertain = true;
                        }
                    }
                    if (uncertain) { deferred = true; continue; }
                    // Preserve complete, unpublished data if an activation pointer is missing.
                    // This is an abnormal state; keeping it is safer than guessing which edition to delete.
                    if (pointer == null && group.Any(p => p.Complete)) { deferred = true; foreach (var item in group.Where(p => p.Complete)) keep.Add(item.Fingerprint); }
                    var resume = group.Where(p => !p.Complete && !keep.Contains(p.Fingerprint) && p.ProgressAt >= now.AddDays(-ResumeRetentionDays) && (!DataFormat.Fingerprint(latestFingerprint) || p.Fingerprint == latestFingerprint))
                        .OrderByDescending(p => p.ProgressAt).ThenBy(p => p.Fingerprint, StringComparer.Ordinal).FirstOrDefault();
                    if (resume != null) keep.Add(resume.Fingerprint);
                    foreach (var item in group) {
                        token.ThrowIfCancellationRequested();
                        if (!keep.Contains(item.Fingerprint)) {
                            if (DeleteOwnedProfile(item, ref freed, token)) removed++; else deferred = true;
                        } else {
                            foreach (string file in item.Files.Where(f => OwnedTempName(Path.GetFileName(f), profile))) {
                                if (File.GetLastWriteTimeUtc(file) < now.AddDays(-1) && !DeleteOwnedFile(file, ref freed)) deferred = true;
                            }
                        }
                    }
                }
                // Interrupted atomic pointer/state writes may leave tiny temporary files.
                foreach (string file in Directory.GetFiles(options.LibraryDir)) {
                    if ((DataFormat.ShortTempName(Path.GetFileName(file)) || Regex.IsMatch(Path.GetFileName(file), @"^(active-[1-5]-[1-5]|source-state|catalog-cache)\.json\.[a-f0-9]{32}\.tmp$")) && File.GetLastWriteTimeUtc(file) < now.AddDays(-1))
                        if (!DeleteOwnedFile(file, ref freed)) deferred = true;
                }
                foreach (string directory in Directory.GetDirectories(options.LibraryDir)) {
                    if (!DataFormat.Fingerprint(Path.GetFileName(directory)) || !LibraryPath(directory)) continue;
                    try { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory, false); } catch { deferred = true; }
                }
            } catch (OperationCanceledException) { deferred = true; }
            catch (Exception ex) { deferred = true; diagnostics.Log(ex); }
            finally {
                freed += store.TakeFreedBytes();
                Storage = new StorageReport { ready = true, busy = false, deferred = deferred, downloadedBytes = DownloadedLibraryBytes(), freedBytes = prior.freedBytes + freed, removedProfiles = prior.removedProfiles + removed, checkedAt = clock.UtcNow.ToString("o"),
                    message = deferred ? "部分文件暂未清理；为保护数据已保留，下次检查再处理。" : "自动空间管理已开启。" };
                Interlocked.Exchange(ref StorageNextCheck, clock.UtcNow.AddHours(24).Ticks);
                RefreshSpaceReport(token);
                if (!token.IsCancellationRequested && !deferred && DataFormat.SafeLocalPath(Path.Combine(options.CacheDir, "maintenance-state.json")))
                    DataFormat.WriteRecord(Path.Combine(options.CacheDir, "maintenance-state.json"), new { checkedAt = clock.UtcNow.ToString("o") });
            }
        }
        internal void ScheduleStorageMaintenance() {
            if (workers.IsCancellationRequested || clock.UtcNow.Ticks < Interlocked.Read(ref StorageNextCheck) || Interlocked.CompareExchange(ref StorageWorker, 1, 0) != 0) return;
            if (!workers.TryStart(() => {
                bool acquired = false;
                using (var mutex = new Mutex(false, "Local\\LunaData_" + DataFormat.Hash(options.Root).Substring(0, 20))) try {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    StorageWaiting = !acquired;
                    if (acquired) MaintainStorage("", workers.Token);
                    else Interlocked.Exchange(ref StorageNextCheck, clock.UtcNow.AddMinutes(1).Ticks);
                } catch (Exception ex) { diagnostics.Log(ex); }
                finally { if (acquired) mutex.ReleaseMutex(); Interlocked.Exchange(ref StorageWorker, 0); }
            }, "Luna storage maintenance")) Interlocked.Exchange(ref StorageWorker, 0);
        }
        int HousekeepingStarted, SpaceWorker;
        readonly object SpaceSync = new object();
        object SpaceSnapshot;
        internal void StartHousekeeping() {
            if (Interlocked.Exchange(ref HousekeepingStarted, 1) != 0) return;
            var last = DataFormat.ReadRecord(Path.Combine(options.CacheDir, "maintenance-state.json"));
            var checkedAt = DataFormat.DateValue(DataFormat.Str(last, "checkedAt"));
            if (checkedAt <= clock.UtcNow && checkedAt > clock.UtcNow.AddDays(-1)) Interlocked.Exchange(ref StorageNextCheck, checkedAt.AddDays(1).Ticks);
            if (!workers.TryStart(() => {
                try {
                    if (workers.Token.WaitHandle.WaitOne(5000)) return;
                    ScheduleStorageMaintenance();
                    store.AuditLibrary(workers.Token);
                } catch (OperationCanceledException) { } catch (Exception ex) { diagnostics.Log(ex); }
            }, "Luna integrity audit")) Interlocked.Exchange(ref HousekeepingStarted, 0);
        }
        internal object SpaceInfo() {
            lock (SpaceSync) {
                if (SpaceSnapshot == null) SpaceSnapshot = DataFormat.ReadRecord(Path.Combine(options.CacheDir, "space-report.json"));
                return new { success = true, busy = Volatile.Read(ref SpaceWorker) != 0 || StorageWorker != 0, waiting = StorageWaiting, report = SpaceSnapshot };
            }
        }
        internal void RequestSpaceMaintenance() {
            if (RequestBrowserCleanup != null) RequestBrowserCleanup();
            Interlocked.Exchange(ref StorageNextCheck, 0); ScheduleStorageMaintenance();
        }
        internal void RefreshSpaceReport(CancellationToken token) {
            if (Interlocked.CompareExchange(ref SpaceWorker, 1, 0) != 0) return;
            try {
                var sizes = new Dictionary<string, long> { { "program", 0 }, { "library", 0 }, { "heroes", 0 }, { "browser", 0 }, { "updates", 0 }, { "diagnostics", 0 }, { "settings", 0 }, { "other", 0 } };
                bool incomplete = false; int count = 0;
                var roots = new List<string>();
                foreach (string name in new[] { "cache", "data", "web", "runtimes" }) {
                    string path = Path.Combine(options.Root, name); if (Directory.Exists(path) && DataFormat.PlainPath(path)) roots.Add(path);
                }
                foreach (string name in new[] { "Luna.exe", "Luna.Maintenance.exe", "Luna.exe.config", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "installed-files.json" }) {
                    string path = Path.Combine(options.Root, name); if (File.Exists(path) && DataFormat.SafeLocalPath(path)) sizes["program"] += new FileInfo(path).Length;
                }
                var pending = new Stack<string>(roots);
                while (pending.Count > 0) {
                    token.ThrowIfCancellationRequested(); string dir = pending.Pop();
                    try {
                        foreach (string path in Directory.EnumerateFiles(dir)) {
                            token.ThrowIfCancellationRequested();
                            if (!DataFormat.SafeLocalPath(path)) { incomplete = true; continue; }
                            string relative = path.Substring(options.Root.Length).Replace('\\', '/');
                            string kind = !relative.StartsWith("cache/") ? "program" : relative.StartsWith("cache/library/") ? "library" : relative.StartsWith("cache/focus/") ? "heroes" :
                                relative.StartsWith("cache/webview2/") ? "browser" : relative.StartsWith("cache/app-update/") ? "updates" : relative.StartsWith("cache/quarantine/") || relative.StartsWith("cache/logs/") || relative == "cache/error.log" ? "diagnostics" :
                                relative == "cache/settings.json" ? "settings" : "other";
                            sizes[kind] += new FileInfo(path).Length; count++;
                            if (count % 64 == 0) activity.YieldMaintenance(token);
                        }
                        foreach (string child in Directory.EnumerateDirectories(dir)) { if (DataFormat.PlainPath(child)) pending.Push(child); else incomplete = true; }
                    } catch (IOException) { incomplete = true; } catch (UnauthorizedAccessException) { incomplete = true; }
                }
                var report = new { checkedAt = clock.UtcNow.ToString("o"), bytes = sizes, totalBytes = sizes.Values.Sum(), fileCount = count, partial = incomplete, freedBytes = Storage.freedBytes + quarantine.FreedBytes };
                lock (SpaceSync) { SpaceSnapshot = report; if (DataFormat.SafeLocalPath(Path.Combine(options.CacheDir, "space-report.json"))) DataFormat.WriteRecord(Path.Combine(options.CacheDir, "space-report.json"), report); }
            } catch (OperationCanceledException) { } catch (Exception ex) { diagnostics.Log(ex); }
            finally { Interlocked.Exchange(ref SpaceWorker, 0); }
        }

        internal Action RequestBrowserCleanup;
        internal object Status { get { return Storage; } }
        internal void AfterUpdate(int changed, CancellationToken token) {
            if (changed > 0 || clock.UtcNow.Ticks >= Interlocked.Read(ref StorageNextCheck)) MaintainStorage("", token);
        }
    }
}

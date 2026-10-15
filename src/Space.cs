using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        const long RecordByteLimit = 16 * 1024 * 1024;
        static long LastQueryTicks;
        static int HousekeepingStarted, SpaceWorker;
        static readonly object SpaceSync = new object();
        static object SpaceSnapshot;
        static long ExtraFreed;
        static string QuarantineDir { get { return Path.Combine(CacheDir, "quarantine"); } }

        static string RecordFileHash(string path) {
            if (!SafeLocalPath(path)) throw new IOException("Unsafe data path");
            using (var file = File.OpenRead(path)) {
                if (file.Length > RecordByteLimit) throw new IOException("Data record exceeds the size limit");
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            }
        }
        static void YieldMaintenance(CancellationToken token) {
            int delay = DateTime.UtcNow.Ticks - Interlocked.Read(ref LastQueryTicks) < TimeSpan.FromMilliseconds(400).Ticks ? 80 : 8;
            if (token.WaitHandle.WaitOne(delay)) token.ThrowIfCancellationRequested();
        }
        static void StartHousekeeping() {
            if (Interlocked.Exchange(ref HousekeepingStarted, 1) != 0) return;
            var last = ReadRecord(Path.Combine(CacheDir, "maintenance-state.json"));
            var checkedAt = DateValue(Str(last, "checkedAt"));
            if (checkedAt <= DateTime.UtcNow && checkedAt > DateTime.UtcNow.AddDays(-1)) Interlocked.Exchange(ref StorageNextCheck, checkedAt.AddDays(1).Ticks);
            var worker = new Thread(() => {
                try {
                    if (Shutdown.Token.WaitHandle.WaitOne(5000)) return;
                    ScheduleStorageMaintenance();
                    AuditLibrary(Shutdown.Token);
                } catch (OperationCanceledException) { } catch (Exception ex) { Log(ex); }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Luna integrity audit" };
            worker.Start();
        }
        // Index discovery is cheap; validation advances from a persisted checkpoint.
        // The checkpoint is only a scheduling hint. Query reads always verify bytes.
        static void AuditLibrary(CancellationToken token) {
            using (var mutex = new Mutex(false, "Local\\LunaAudit_" + Hash(Root.ToLowerInvariant()).Substring(0, 20))) {
                bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) return;
                try {
                    var profiles = Profiles.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
                    string revision = AuditRevision(profiles);
                    string checkpoint = Path.Combine(CacheDir, "audit-state.json");
                    var saved = SafeLocalPath(checkpoint) ? ReadRecord(checkpoint) : null;
                    int next = 0; bool same = Str(saved, "revision") == revision;
                    if (same && DateValue(Str(saved, "completedAt")) > DateTime.UtcNow.AddDays(-1)) return;
                    if (same) int.TryParse(Str(saved, "next"), out next);
                    if (next < 0) next = 0;
                    var items = profiles.SelectMany(p => p.Entries.OrderBy(e => e.Key).Select(e => new { profile = p, key = e.Key, hash = Convert.ToString(e.Value) })).ToArray();
                    if (next > items.Length) next = 0;
                    try {
                        while (next < items.Length) {
                            token.ThrowIfCancellationRequested();
                            var item = items[next]; bool good;
                            try { good = RecordFileHash(Path.Combine(item.profile.Directory, item.key + ".json.gz")) == item.hash; } catch { good = false; }
                            if (good) { bool ignored; item.profile.Damaged.TryRemove(item.key, out ignored); } else item.profile.Damaged[item.key] = true;
                            next++;
                            if (next % 16 == 0) { SaveAudit(checkpoint, profiles, revision, next, items.Length, false); YieldMaintenance(token); }
                        }
                    } finally {
                        SaveAudit(checkpoint, profiles, revision, next, items.Length, next == items.Length);
                    }
                } finally { mutex.ReleaseMutex(); }
            }
        }
        static string AuditRevision(IEnumerable<LibraryProfile> profiles) { return Hash(String.Join(";", profiles.Select(p => p.Directory + ":" + Str(p.Manifest, "fingerprint") + ":" + Str(p.Manifest, "builtAt")))); }
        static string AuditKey(LibraryProfile profile, string key) { return profile.Source + ":" + Str(profile.Manifest, "fingerprint") + ":" + key; }
        static void SaveAudit(string path, LibraryProfile[] profiles, string revision, int next, int total, bool complete) {
            if (SafeLocalPath(path)) WriteRecord(path, new { revision = revision, next = next, total = total, completedAt = complete ? Stamp() : "", damaged = profiles.SelectMany(p => p.Damaged.Keys.Select(k => AuditKey(p, k))).ToArray() });
        }
        static void RestoreAuditedDamage(Dictionary<string, List<LibraryProfile>> profiles) {
            var entries = profiles.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
            var saved = ReadRecord(Path.Combine(CacheDir, "audit-state.json"));
            if (Str(saved, "revision") != AuditRevision(entries)) return;
            var damaged = new HashSet<string>(Arr(Get(saved, "damaged")).Select(Convert.ToString));
            foreach (var profile in entries) foreach (string key in profile.Entries.Keys) if (damaged.Contains(AuditKey(profile, key))) profile.Damaged[key] = true;
        }
        static bool OwnedExceptionalFile(string path) {
            string parent = Path.GetFullPath(Path.GetDirectoryName(path)).TrimEnd('\\'), name = Path.GetFileName(path);
            if (parent.Equals(Path.GetFullPath(FocusDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return name.EndsWith(".json.gz", StringComparison.Ordinal) && ValidFocus(name.Substring(0, name.Length - 8));
            return parent.Equals(Path.GetFullPath(CacheDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                (name == "settings.json" || Regex.IsMatch(name, @"^settings\.invalid-[a-f0-9]{32}\.json$"));
        }
        static void QuarantineOwnedFile(string path) {
            lock (SpaceSync) {
                try {
                    if (!OwnedExceptionalFile(path) || !SafeLocalPath(path) || !File.Exists(path) || !PlainPath(QuarantineDir)) return;
                    Directory.CreateDirectory(QuarantineDir);
                    string target = Path.Combine(QuarantineDir, "luna-" + Guid.NewGuid().ToString("N") + ".bad");
                    File.Move(path, target); File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                    PruneQuarantine();
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        static void PruneQuarantine() {
            if (!PlainPath(QuarantineDir) || !Directory.Exists(QuarantineDir)) return;
            var files = Directory.EnumerateFiles(QuarantineDir).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^luna-[a-f0-9]{32}\.bad$") && SafeLocalPath(p))
                .Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long kept = 0; int count = 0;
            foreach (var file in files) {
                if (file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7) && count < 20 && kept + file.Length <= 10 * 1024 * 1024) { count++; kept += file.Length; continue; }
                try { long bytes = file.Length; File.Delete(file.FullName); Interlocked.Add(ref ExtraFreed, bytes); } catch { }
            }
        }
        static void MaintainExceptionalFiles() {
            lock (SpaceSync) {
                if (!PlainPath(CacheDir) || !Directory.Exists(CacheDir)) return;
                foreach (string path in Directory.EnumerateFiles(CacheDir, "settings.invalid-*.json"))
                    if (OwnedExceptionalFile(path)) QuarantineOwnedFile(path);
                PruneQuarantine();
                foreach (string path in Directory.EnumerateFiles(CacheDir, "*.tmp")) {
                    if (!ShortTempName(Path.GetFileName(path)) || !SafeLocalPath(path) || File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-1)) continue;
                    try { long bytes = new FileInfo(path).Length; File.Delete(path); Interlocked.Add(ref ExtraFreed, bytes); } catch { }
                }
            }
        }
        static object SpaceInfo() {
            lock (SpaceSync) {
                if (SpaceSnapshot == null) SpaceSnapshot = ReadRecord(Path.Combine(CacheDir, "space-report.json"));
                return new { success = true, busy = Volatile.Read(ref SpaceWorker) != 0 || StorageWorker != 0, waiting = StorageWaiting, report = SpaceSnapshot };
            }
        }
        static void RequestSpaceMaintenance() {
            if (Desktop != null) Desktop.RequestCacheCleanup();
            Interlocked.Exchange(ref StorageNextCheck, 0); ScheduleStorageMaintenance();
        }
        static void RefreshSpaceReport(CancellationToken token) {
            if (Interlocked.CompareExchange(ref SpaceWorker, 1, 0) != 0) return;
            try {
                var sizes = new Dictionary<string, long> { { "program", 0 }, { "library", 0 }, { "heroes", 0 }, { "browser", 0 }, { "updates", 0 }, { "diagnostics", 0 }, { "settings", 0 }, { "other", 0 } };
                bool incomplete = false; int count = 0;
                var roots = new List<string>();
                foreach (string name in new[] { "cache", "data", "web", "runtimes" }) {
                    string path = Path.Combine(Root, name); if (Directory.Exists(path) && PlainPath(path)) roots.Add(path);
                }
                foreach (string name in new[] { "Luna.exe", "Luna.Maintenance.exe", "Luna.exe.config", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "installed-files.json" }) {
                    string path = Path.Combine(Root, name); if (File.Exists(path) && SafeLocalPath(path)) sizes["program"] += new FileInfo(path).Length;
                }
                var pending = new Stack<string>(roots);
                while (pending.Count > 0) {
                    token.ThrowIfCancellationRequested(); string dir = pending.Pop();
                    try {
                        foreach (string path in Directory.EnumerateFiles(dir)) {
                            token.ThrowIfCancellationRequested();
                            if (!SafeLocalPath(path)) { incomplete = true; continue; }
                            string relative = path.Substring(Root.Length).Replace('\\', '/');
                            string kind = !relative.StartsWith("cache/") ? "program" : relative.StartsWith("cache/library/") ? "library" : relative.StartsWith("cache/focus/") ? "heroes" :
                                relative.StartsWith("cache/webview2/") ? "browser" : relative.StartsWith("cache/app-update/") ? "updates" : relative.StartsWith("cache/quarantine/") || relative.StartsWith("cache/logs/") || relative == "cache/error.log" ? "diagnostics" :
                                relative == "cache/settings.json" ? "settings" : "other";
                            sizes[kind] += new FileInfo(path).Length; count++;
                            if (count % 64 == 0) YieldMaintenance(token);
                        }
                        foreach (string child in Directory.EnumerateDirectories(dir)) { if (PlainPath(child)) pending.Push(child); else incomplete = true; }
                    } catch (IOException) { incomplete = true; } catch (UnauthorizedAccessException) { incomplete = true; }
                }
                var report = new { checkedAt = Stamp(), bytes = sizes, totalBytes = sizes.Values.Sum(), fileCount = count, partial = incomplete, freedBytes = Storage.freedBytes + Interlocked.Read(ref ExtraFreed) };
                lock (SpaceSync) { SpaceSnapshot = report; if (SafeLocalPath(Path.Combine(CacheDir, "space-report.json"))) WriteRecord(Path.Combine(CacheDir, "space-report.json"), report); }
            } catch (OperationCanceledException) { } catch (Exception ex) { Log(ex); }
            finally { Interlocked.Exchange(ref SpaceWorker, 0); }
        }
    }
}

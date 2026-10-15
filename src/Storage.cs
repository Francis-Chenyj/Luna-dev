using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        const int ResumeRetentionDays = 7;
        static int StorageWorker;
        static long StorageNextCheck;
        static volatile bool StorageWaiting;
        sealed class StorageReport {
            public bool ready, busy, deferred;
            public long downloadedBytes, freedBytes;
            public int removedProfiles;
            public string checkedAt = "", message = "正在检查本地空间…";
            public int resumeDays = ResumeRetentionDays;
        }
        static volatile StorageReport Storage = new StorageReport();
        sealed class StoredProfile {
            public string Path, Fingerprint, Profile;
            public bool Complete;
            public DateTime ProgressAt;
            public string[] Files;
        }

        // Automatic deletion is limited to Luna-owned files inside this exact library.
        // Never traverse junctions/symlinks, unknown subdirectories or user-added files.
        static bool PlainPath(string path) {
            try {
                var full = Path.GetFullPath(path);
                for (var item = new DirectoryInfo(full); item != null; item = item.Parent)
                    if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            } catch { return false; }
        }
        static bool LibraryPath(string path) {
            try {
                string full = Path.GetFullPath(path), root = Path.GetFullPath(LibraryDir).TrimEnd(Path.DirectorySeparatorChar);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !PlainPath(full)) return false;
                return !File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
            } catch { return false; }
        }
        static bool OwnedRecordName(string name, string profile) {
            if (!name.EndsWith(".json.gz", StringComparison.Ordinal)) return false;
            string key = name.Substring(0, name.Length - 8);
            return ValidKey(key) && ProfileOf(key) == profile;
        }
        static bool OwnedTempName(string name, string profile) {
            if (ShortTempName(name)) return true;
            var match = Regex.Match(name, @"^(.*)\.[a-f0-9]{32}\.tmp$");
            return match.Success && (match.Groups[1].Value == "manifest.json" || OwnedRecordName(match.Groups[1].Value, profile));
        }
        static StoredProfile InspectStoredProfile(string path, string fingerprint, string profile) {
            try {
                if (!LibraryPath(path) || Directory.GetDirectories(path).Length != 0) return null;
                string[] files = Directory.GetFiles(path);
                DateTime progress = Directory.GetLastWriteTimeUtc(path);
                var written = new List<DateTime>(); bool complete = false;
                foreach (string file in files) {
                    if (!LibraryPath(file)) return null;
                    string name = Path.GetFileName(file);
                    if (name == "manifest.json") {
                        var manifest = ReadRecord(file);
                        if (manifest == null || Str(manifest, "schema") != "1" || Str(manifest, "profile") != profile || Str(manifest, "fingerprint") != fingerprint) return null;
                        complete = true;
                    } else if (OwnedRecordName(name, profile)) written.Add(File.GetLastWriteTimeUtc(file));
                    else if (!OwnedTempName(name, profile)) return null;
                }
                if (written.Count > 0) progress = written.Max();
                return new StoredProfile { Path = path, Fingerprint = fingerprint, Profile = profile, Complete = complete, ProgressAt = progress, Files = files };
            } catch { return null; }
        }
        static bool DeleteOwnedFile(string path, ref long freed) {
            try {
                if (!LibraryPath(path)) return false;
                if (!File.Exists(path)) return true;
                long bytes = new FileInfo(path).Length;
                File.Delete(path); freed += bytes; return true;
            } catch { return false; }
        }
        static bool DeleteOwnedProfile(StoredProfile profile, ref long freed, CancellationToken token) {
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
        static long DownloadedLibraryBytes() {
            long bytes = FocusBytes();
            if (!Directory.Exists(LibraryDir) || !PlainPath(LibraryDir)) return bytes;
            var pending = new Stack<string>(); pending.Push(LibraryDir);
            while (pending.Count > 0) {
                string directory = pending.Pop();
                try {
                    foreach (string file in Directory.GetFiles(directory)) if (LibraryPath(file)) bytes += new FileInfo(file).Length;
                    foreach (string child in Directory.GetDirectories(directory)) if (LibraryPath(child)) pending.Push(child);
                } catch { }
            }
            return bytes;
        }
        static string KnownSourceFingerprint(DateTime now) {
            var source = ReadRecord(Path.Combine(LibraryDir, "source-state.json"));
            string fingerprint = Str(source, "fingerprint");
            DateTime checkedAt = DateValue(Str(source, "checkedAt"));
            return Fingerprint(fingerprint) && checkedAt <= now.AddMinutes(5) && checkedAt >= now.AddDays(-ResumeRetentionDays) ? fingerprint : "";
        }
        static void RecordSourceFingerprint(string fingerprint) {
            if (!Fingerprint(fingerprint)) return;
            Directory.CreateDirectory(LibraryDir);
            WriteRecord(Path.Combine(LibraryDir, "source-state.json"), new { fingerprint = fingerprint, checkedAt = Stamp() });
        }

        // Caller holds the same cross-process mutex as the updater. No external requests.
        static void MaintainStorage(string latestFingerprint, CancellationToken token) {
            StorageWaiting = false;
            var prior = Storage;
            Storage = new StorageReport { busy = true, ready = prior.ready, downloadedBytes = prior.downloadedBytes, freedBytes = prior.freedBytes, checkedAt = prior.checkedAt, message = "正在自动整理离线数据…" };
            long freed = 0; int removed = 0; bool deferred = false;
            try {
                DateTime now = DateTime.UtcNow;
                MaintainExceptionalFiles();
                MaintainAppDownloads();
                MaintainFocused();
                if (!Directory.Exists(LibraryDir)) return;
                if (!PlainPath(LibraryDir)) { deferred = true; return; }
                if (!Fingerprint(latestFingerprint)) latestFingerprint = KnownSourceFingerprint(now);
                var candidates = new List<StoredProfile>();
                foreach (string directory in Directory.GetDirectories(LibraryDir)) {
                    token.ThrowIfCancellationRequested();
                    string fingerprint = Path.GetFileName(directory);
                    if (!Fingerprint(fingerprint)) continue;
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
                    string profile = group.Key, pointerPath = Path.Combine(LibraryDir, "active-" + profile + ".json");
                    var pointer = ReadRecord(pointerPath);
                    var keep = new HashSet<string>(StringComparer.Ordinal);
                    bool uncertain = false;
                    if (File.Exists(pointerPath)) {
                        if (!LibraryPath(pointerPath) || pointer == null || !Fingerprint(Str(pointer, "current")) || (Str(pointer, "previous") != "" && !Fingerprint(Str(pointer, "previous")))) uncertain = true;
                        else {
                            keep.Add(Str(pointer, "current"));
                            if (Fingerprint(Str(pointer, "previous"))) keep.Add(Str(pointer, "previous"));
                            // A broken active edition must never cause the removal of potentially useful complete data.
                            string activePath = Path.Combine(LibraryDir, Str(pointer, "current"), profile);
                            var active = LibraryPath(activePath) ? OpenProfile(activePath, profile, "library") : null;
                            if (active == null || !ProfileFilesIntact(active)) uncertain = true;
                        }
                    }
                    if (uncertain) { deferred = true; continue; }
                    // Preserve complete, unpublished data if an activation pointer is missing.
                    // This is an abnormal state; keeping it is safer than guessing which edition to delete.
                    if (pointer == null && group.Any(p => p.Complete)) { deferred = true; foreach (var item in group.Where(p => p.Complete)) keep.Add(item.Fingerprint); }
                    var resume = group.Where(p => !p.Complete && !keep.Contains(p.Fingerprint) && p.ProgressAt >= now.AddDays(-ResumeRetentionDays) && (!Fingerprint(latestFingerprint) || p.Fingerprint == latestFingerprint))
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
                foreach (string file in Directory.GetFiles(LibraryDir)) {
                    if ((ShortTempName(Path.GetFileName(file)) || Regex.IsMatch(Path.GetFileName(file), @"^(active-[1-5]-[1-5]|source-state|catalog-cache)\.json\.[a-f0-9]{32}\.tmp$")) && File.GetLastWriteTimeUtc(file) < now.AddDays(-1))
                        if (!DeleteOwnedFile(file, ref freed)) deferred = true;
                }
                foreach (string directory in Directory.GetDirectories(LibraryDir)) {
                    if (!Fingerprint(Path.GetFileName(directory)) || !LibraryPath(directory)) continue;
                    try { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory, false); } catch { deferred = true; }
                }
            } catch (OperationCanceledException) { deferred = true; }
            catch (Exception ex) { deferred = true; Log(ex); }
            finally {
                freed += Interlocked.Exchange(ref FocusFreedBytes, 0);
                Storage = new StorageReport { ready = true, busy = false, deferred = deferred, downloadedBytes = DownloadedLibraryBytes(), freedBytes = prior.freedBytes + freed, removedProfiles = prior.removedProfiles + removed, checkedAt = Stamp(),
                    message = deferred ? "部分文件暂未清理；为保护数据已保留，下次检查再处理。" : "自动空间管理已开启。" };
                Interlocked.Exchange(ref StorageNextCheck, DateTime.UtcNow.AddHours(24).Ticks);
                RefreshSpaceReport(token);
                if (!token.IsCancellationRequested && !deferred && SafeLocalPath(Path.Combine(CacheDir, "maintenance-state.json")))
                    WriteRecord(Path.Combine(CacheDir, "maintenance-state.json"), new { checkedAt = Stamp() });
            }
        }
        static void ScheduleStorageMaintenance() {
            if (Shutdown.IsCancellationRequested || DateTime.UtcNow.Ticks < Interlocked.Read(ref StorageNextCheck) || Interlocked.CompareExchange(ref StorageWorker, 1, 0) != 0) return;
            var thread = new Thread(() => {
                bool acquired = false;
                using (var mutex = new Mutex(false, "Local\\LunaData_" + Hash(Root).Substring(0, 20))) try {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    StorageWaiting = !acquired;
                    if (acquired) MaintainStorage("", Shutdown.Token);
                    else Interlocked.Exchange(ref StorageNextCheck, DateTime.UtcNow.AddMinutes(1).Ticks);
                } catch (Exception ex) { Log(ex); }
                finally { if (acquired) mutex.ReleaseMutex(); Interlocked.Exchange(ref StorageWorker, 0); }
            });
            thread.IsBackground = true; thread.Start();
        }
        static string PreviousUsableFingerprint(string profile, string incoming) {
            List<LibraryProfile> list;
            if (Profiles.TryGetValue(profile, out list)) foreach (var item in list)
                if (item.Source != "bundled" && Str(item.Manifest, "fingerprint") != incoming && ProfileFilesIntact(item)) return Str(item.Manifest, "fingerprint");
            return "";
        }
    }
}

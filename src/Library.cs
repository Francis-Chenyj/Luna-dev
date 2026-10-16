using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace Luna {
    internal sealed class LibraryStore {
        readonly RuntimeOptions options;
        readonly DiagnosticLog diagnostics;
        readonly QuarantineStore quarantine;
        readonly QueryActivity activity;
        internal LibraryStore(RuntimeOptions options, DiagnosticLog diagnostics, QuarantineStore quarantine, QueryActivity activity) {
            this.options = options;
            this.diagnostics = diagnostics;
            this.quarantine = quarantine;
            this.activity = activity;
        }
        sealed class LibraryProfile {
            public string Directory, Source;
            public bool Fallback;
            public Dictionary<string, object> Manifest, Entries;
            public readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Damaged = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        }
        readonly object LibrarySync = new object();
        volatile Dictionary<string, List<LibraryProfile>> Profiles = new Dictionary<string, List<LibraryProfile>>();
        readonly Dictionary<string, string> ProfilePointers = new Dictionary<string, string>();
        LibraryProfile OpenProfile(string directory, string profile, string source) {
            return OpenProfileIndex(directory, profile, source, true);
        }
        LibraryProfile OpenProfileIndex(string directory, string profile, string source, bool verifyFiles) {
            try {
                var manifest = DataFormat.ReadRecord(Path.Combine(directory, "manifest.json"));
                if (manifest == null || DataFormat.Str(manifest, "schema") != "1" || DataFormat.Str(manifest, "profile") != profile || !DataFormat.Fingerprint(DataFormat.Str(manifest, "fingerprint")) || DataFormat.DateValue(DataFormat.Str(manifest, "sourceGeneratedAt")) == DateTime.MinValue) return null;
                var entries = DataFormat.Obj(DataFormat.Get(manifest, "entries")); var expected = DataFormat.ExpectedKeys(DataFormat.Obj(DataFormat.Get(manifest, "catalog")), profile);
                if (entries.Count != expected.Count || DataFormat.Str(manifest, "expected") != expected.Count.ToString() || expected.Any(key => !entries.ContainsKey(key) || !DataFormat.Fingerprint(DataFormat.Str(entries, key)))) return null;
                var result = new LibraryProfile { Directory = directory, Source = source, Manifest = manifest, Entries = entries };
                if (verifyFiles) foreach (var entry in entries) {
                    try { if (DataFormat.RecordFileHash(Path.Combine(directory, entry.Key + ".json.gz")) != Convert.ToString(entry.Value)) result.Damaged[entry.Key] = true; }
                    catch { result.Damaged[entry.Key] = true; }
                }
                return result;
            } catch (Exception ex) { diagnostics.Log(ex); return null; }
        }
        string PointerIdentity(Dictionary<string, object> pointer) { return DataFormat.Str(pointer, "current") + ":" + DataFormat.Str(pointer, "previous"); }
        List<LibraryProfile> LoadProfile(string profile, Dictionary<string, object> pointer) {
            var list = new List<LibraryProfile>();
            bool unavailableCurrent = pointer == null && File.Exists(Path.Combine(options.LibraryDir, "active-" + profile + ".json"));
            if (pointer != null) foreach (string field in new[] { "current", "previous" }) {
                string fingerprint = DataFormat.Str(pointer, field);
                if (!DataFormat.Fingerprint(fingerprint)) { if (field == "current") unavailableCurrent = true; continue; }
                var record = OpenProfileIndex(Path.Combine(options.LibraryDir, fingerprint, profile), profile, field == "current" ? "library" : "previous", false);
                if (record != null) list.Add(record);
                else if (field == "current") unavailableCurrent = true;
            }
            var bundled = OpenProfileIndex(Path.Combine(options.Root, "data", "library", profile), profile, "bundled", false);
            if (bundled != null) list.Add(bundled);
            foreach (var item in list) item.Fallback = unavailableCurrent || item.Source == "previous";
            return list.OrderByDescending(p => DataFormat.DateValue(DataFormat.Str(p.Manifest, "sourceGeneratedAt"))).ToList();
        }
        string ProfileUpdatedAt(LibraryProfile profile) {
            // A bundled build time belongs to the publisher, not to this user's computer.
            return profile == null || profile.Source == "bundled" ? "" : DataFormat.RecordedTime(DataFormat.Str(profile.Manifest, "builtAt"));
        }
        internal bool RefreshProfile(string profile) {
            var pointer = DataFormat.ReadRecord(Path.Combine(options.LibraryDir, "active-" + profile + ".json"));
            string identity = PointerIdentity(pointer), previous;
            lock (LibrarySync) {
                if (ProfilePointers.TryGetValue(profile, out previous) && previous == identity) return false;
            }
            // Disk validation must never hold the lock shared with local queries.
            var list = LoadProfile(profile, pointer);
            if (PointerIdentity(DataFormat.ReadRecord(Path.Combine(options.LibraryDir, "active-" + profile + ".json"))) != identity) return false;
            lock (LibrarySync) {
                if (ProfilePointers.TryGetValue(profile, out previous) && previous == identity) return false;
                var next = new Dictionary<string, List<LibraryProfile>>(Profiles);
                if (list.Count > 0) next[profile] = list; else next.Remove(profile);
                ProfilePointers[profile] = identity; Profiles = next; return true;
            }
        }
        internal void InitializeLibrary() {
            var next = new Dictionary<string, List<LibraryProfile>>();
            var pointers = new Dictionary<string, string>();
            for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) {
                string profile = t + "-" + s;
                var pointer = DataFormat.ReadRecord(Path.Combine(options.LibraryDir, "active-" + profile + ".json"));
                var list = LoadProfile(profile, pointer);
                pointers[profile] = PointerIdentity(pointer);
                if (list.Count > 0) next[profile] = list;
            }
            // Startup reads manifests only. Every queried record is still checked;
            // a resumable, throttled background audit checks the rest.
            RestoreAuditedDamage(next);
            lock (LibrarySync) {
                foreach (var item in pointers) ProfilePointers[item.Key] = item.Value;
                Profiles = next;
            }
        }
        internal Dictionary<string, object> ReadLocalDetail(string key, bool mayRefresh) {
            List<LibraryProfile> list; bool damaged = false, skipped = false;
            if (Profiles.TryGetValue(DataFormat.ProfileOf(key), out list)) foreach (var profile in list) {
                if (!profile.Entries.ContainsKey(key)) { skipped = true; continue; }
                try {
                    string path = Path.Combine(profile.Directory, key + ".json.gz");
                    if (DataFormat.RecordFileHash(path) != DataFormat.Str(profile.Entries, key)) throw new IOException("记录校验失败：" + key);
                    var record = DataFormat.ReadCompressed(path);
                    if (!DataFormat.ValidRecord(record, key, DataFormat.Str(profile.Manifest, "fingerprint"))) throw new IOException("查询记录与条件不一致：" + key);
                    bool ignored; profile.Damaged.TryRemove(key, out ignored);
                    bool fallback = damaged || skipped || profile.Fallback;
                    return new Dictionary<string, object> { { "success", true }, { "data", record["data"] }, { "cache", new {
                        source = profile.Source, fetchedAt = DataFormat.Str(record, "fetchedAt"), sourceGeneratedAt = DataFormat.Str(profile.Manifest, "sourceGeneratedAt"), fingerprint = DataFormat.Str(profile.Manifest, "fingerprint"), needsRefresh = false,
                        localUpdatedAt = ProfileUpdatedAt(profile), isFallback = fallback,
                        warning = fallback ? "优先使用的数据未能提供此结果，已使用备用数据。可在数据管理中更新或恢复程序包。" : ""
                    } } };
                } catch (Exception ex) {
                    // Another local process may publish and retire old data during this read.
                    if (mayRefresh && RefreshProfile(DataFormat.ProfileOf(key))) return ReadLocalDetail(key, false);
                    damaged = true; profile.Damaged[key] = true; diagnostics.Log(ex);
                }
            }
            // Keep 1.2 user records as an exact-query fallback, never as a different filter profile.
            foreach (string directory in new[] { options.CacheDir, Path.Combine(options.Root, "data", "seeds") }) {
                var record = DataFormat.ReadRecord(Path.Combine(directory, key + ".json"));
                if (record == null || !DataFormat.ValidDetail(DataFormat.Obj(DataFormat.Get(record, "data")), key)) continue;
                bool bundled = directory != options.CacheDir;
                return new Dictionary<string, object> { { "success", true }, { "data", record["data"] }, { "cache", new {
                    source = "legacy", origin = bundled ? "bundled" : "downloaded", isFallback = true,
                    fetchedAt = DataFormat.Str(record, "fetchedAt"), sourceGeneratedAt = DataFormat.RecordedTime(DataFormat.Str(record, "sourceGeneratedAt")),
                    localUpdatedAt = bundled ? "" : DataFormat.RecordedTime(DataFormat.Str(record, "fetchedAt")), needsRefresh = false,
                    warning = "这是旧版保存的单条结果；数据管理可补齐此筛选组合的完整离线库。"
                } } };
            }
            return new Dictionary<string, object> { { "success", true }, { "data", null }, { "cache", new {
                source = "missing", reason = damaged ? "damaged" : "not_saved", needsRefresh = false, warning = damaged ? "这条本地数据损坏，且没有可用备份。请更新离线库或重新解压程序包。" : "本地尚未保存这个筛选组合的结果。可恢复默认条件，或在“数据管理”中下载此组合；不会用其他条件的结果代替。"
            } } };
        }
        internal Dictionary<string, object> LocalCatalog() {
            RefreshProfile("1-5");
            List<LibraryProfile> list;
            if (Profiles.TryGetValue("1-5", out list)) return DataFormat.Envelope(DataFormat.Obj(list[0].Manifest["catalog"]), list[0].Source);
            var fallback = DataFormat.ReadRecord(Path.Combine(options.Root, "data", "catalog.json"));
            if (fallback == null) throw new IOException("缺少英雄目录");
            return DataFormat.Envelope(fallback, "bundled", "完整离线库未能加载，请恢复 data 文件夹。");
        }
        internal Dictionary<string, object> LibraryInfo(string profile) {
            RefreshProfile(profile);
            List<LibraryProfile> list; Profiles.TryGetValue(profile, out list);
            var current = list == null ? null : list[0];
            var checkedSource = DataFormat.ReadRecord(Path.Combine(options.LibraryDir, "source-state.json"));
            string checkedAt = DataFormat.RecordedTime(DataFormat.Str(checkedSource, "checkedAt"));
            if (!DataFormat.Fingerprint(DataFormat.Str(checkedSource, "fingerprint")) || DataFormat.DateValue(checkedAt) > DateTime.UtcNow.AddMinutes(5)) checkedAt = "";
            int expected = DataFormat.ExpectedKeys(new Dictionary<string, object> { { "data", LocalCatalog()["data"] } }, profile).Count;
            var snapshot = Profiles;
            var savedProfiles = snapshot.Where(p => p.Value[0].Damaged.IsEmpty).Select(p => p.Key).OrderBy(p => p).ToArray();
            string revision = String.Join(";", snapshot.OrderBy(p => p.Key).Select(p => p.Key + ":" + DataFormat.Str(p.Value[0].Manifest, "fingerprint"))) + ";focused:" + FocusRevision();
            return DataFormat.Obj(DataFormat.Json().DeserializeObject(DataFormat.Json().Serialize(new {
                success = true, profile = profile, complete = current != null && current.Damaged.IsEmpty, saved = current == null ? 0 : current.Entries.Count - current.Damaged.Count,
                expected = expected,
                sourceGeneratedAt = current == null ? "" : DataFormat.Str(current.Manifest, "sourceGeneratedAt"), builtAt = current == null ? "" : DataFormat.Str(current.Manifest, "builtAt"),
                source = current == null ? "missing" : current.Source, isFallback = current != null && current.Fallback,
                localUpdatedAt = ProfileUpdatedAt(current), sourceCheckedAt = checkedAt,
                profiles = savedProfiles, revision = revision
            })));
        }
        bool ProfileFilesIntact(LibraryProfile profile) {
            try { return profile.Entries.All(item => { string path = Path.Combine(profile.Directory, item.Key + ".json.gz"); return File.Exists(path) && DataFormat.RecordFileHash(path) == Convert.ToString(item.Value); }); }
            catch { return false; }
        }
        readonly object FocusSync = new object();
        List<string> FocusRecent;
        long FocusFreedBytes;
        const int FocusCacheLimit = 64;
        const long FocusByteLimit = 8 * 1024 * 1024;
        internal string FocusFile(string pair) { if (!DataFormat.ValidFocus(pair)) throw new ArgumentException("英雄更新条件无效"); return Path.Combine(options.FocusDir, pair + ".json.gz"); }
        internal List<string> RecentFocus() {
            lock (FocusSync) {
                if (FocusRecent == null) {
                    var saved = DataFormat.PlainPath(options.FocusDir) ? DataFormat.ReadRecord(Path.Combine(options.FocusDir, "recent.json")) : null;
                    FocusRecent = DataFormat.Arr(DataFormat.Get(saved, "pairs")).Select(Convert.ToString).Where(DataFormat.ValidFocus).Distinct().Take(24).ToList();
                }
                return new List<string>(FocusRecent);
            }
        }
        internal void RegisterFocus(string key) {
            if (!DataFormat.ValidKey(key)) throw new ArgumentException("英雄更新条件无效");
            string pair = DataFormat.FocusPair(key);
            if (!DataFormat.PlainPath(options.FocusDir)) return;
            lock (FocusSync) {
                var recent = RecentFocus(); recent.Remove(pair); recent.Insert(0, pair);
                FocusRecent = recent.Take(24).ToList();
                Directory.CreateDirectory(options.FocusDir);
                DataFormat.WriteRecord(Path.Combine(options.FocusDir, "recent.json"), new { owner = "Luna.Focus.1", pairs = FocusRecent });
            }
        }
        internal Dictionary<string, object> ReadFocusedPair(string pair) {
            string path = FocusFile(pair);
            if (!DataFormat.SafeFocusPath(path) || !File.Exists(path)) return null;
            var record = DataFormat.ReadCompressed(path);
            if (DataFormat.Str(record, "owner") != "Luna.Focus.1" || DataFormat.Str(record, "pair") != pair || !DataFormat.Fingerprint(DataFormat.Str(record, "fingerprint")) || DataFormat.DateValue(DataFormat.Str(record, "sourceGeneratedAt")) == DateTime.MinValue || DataFormat.DateValue(DataFormat.Str(record, "savedAt")) == DateTime.MinValue) throw new IOException("英雄更新记录格式异常");
            string payload = DataFormat.Str(record, "payload");
            if (DataFormat.Hash(payload) != DataFormat.Str(record, "sha256")) throw new IOException("英雄更新记录校验失败");
            var rows = DataFormat.Obj(DataFormat.Json().DeserializeObject(payload)); var keys = DataFormat.FocusKeys(pair);
            if (rows.Count != 2 || keys.Any(k => !rows.ContainsKey(k) || !DataFormat.ValidRecord(DataFormat.Obj(rows[k]), k, DataFormat.Str(record, "fingerprint")))) throw new IOException("英雄两侧更新记录不完整");
            record["records"] = rows; return record;
        }
        internal Dictionary<string, object> FocusedResult(string key, out bool damaged) {
            damaged = false; var pair = DataFormat.FocusPair(key);
            try {
                var record = ReadFocusedPair(pair); if (record == null) return null;
                List<LibraryProfile> profiles;
                if (Profiles.TryGetValue(DataFormat.ProfileOf(key), out profiles) && profiles.Count > 0) {
                    var best = profiles[0];
                    if (DataFormat.DateValue(DataFormat.Str(best.Manifest, "sourceGeneratedAt")) > DataFormat.DateValue(DataFormat.Str(record, "sourceGeneratedAt"))) return null;
                    if (DataFormat.Str(best.Manifest, "fingerprint") == DataFormat.Str(record, "fingerprint") && best.Damaged.IsEmpty) {
                        bool manual = DataFormat.Get(record, "manualRefresh") is bool && (bool)record["manualRefresh"];
                        if (!manual || (best.Source != "bundled" && DataFormat.DateValue(ProfileUpdatedAt(best)) >= DataFormat.DateValue(DataFormat.Str(record, "savedAt")))) return null;
                    }
                }
                var row = DataFormat.Obj(DataFormat.Obj(record["records"])[key]);
                return new Dictionary<string, object> { { "success", true }, { "data", row["data"] }, { "cache", new {
                    source = "focused", scope = "champion", fetchedAt = DataFormat.Str(row, "fetchedAt"), sourceGeneratedAt = DataFormat.Str(record, "sourceGeneratedAt"),
                    localUpdatedAt = DataFormat.Str(record, "savedAt"), fingerprint = DataFormat.Str(record, "fingerprint"), needsRefresh = false, isFallback = false,
                    warning = ""
                } } };
            } catch (Exception ex) { damaged = true; diagnostics.Log(ex); return null; }
        }
        string FocusRevision() {
            try { string path = Path.Combine(options.FocusDir, "revision.json"); return DataFormat.SafeFocusPath(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0"; }
            catch { return "0"; }
        }
        internal bool AlreadyCurrent(string pair, string fingerprint) {
            List<LibraryProfile> profiles;
            if (Profiles.TryGetValue(DataFormat.FocusProfile(pair), out profiles) && profiles.Any(p => DataFormat.Str(p.Manifest, "fingerprint") == fingerprint && DataFormat.FocusKeys(pair).All(k => p.Entries.ContainsKey(k) && !p.Damaged.ContainsKey(k)))) return true;
            try { var saved = ReadFocusedPair(pair); return saved != null && DataFormat.Str(saved, "fingerprint") == fingerprint; } catch { return false; }
        }
        internal long FocusBytes() {
            try { return !Directory.Exists(options.FocusDir) || !DataFormat.PlainPath(options.FocusDir) ? 0 : Directory.GetFiles(options.FocusDir).Where(DataFormat.SafeFocusPath).Sum(p => new FileInfo(p).Length); } catch { return 0; }
        }
        internal void MaintainFocused() {
            if (!Directory.Exists(options.FocusDir) || !DataFormat.PlainPath(options.FocusDir)) return;
            var files = new List<FileInfo>(); var recent = RecentFocus();
            foreach (string path in Directory.GetFiles(options.FocusDir, "focus-*.json.gz")) {
                string pair = Path.GetFileName(path); pair = pair.Substring(0, pair.Length - 8);
                if (!DataFormat.ValidFocus(pair) || !DataFormat.SafeFocusPath(path)) continue;
                try { if (ReadFocusedPair(pair) != null) files.Add(new FileInfo(path)); } catch { quarantine.QuarantineOwnedFile(path); }
            }
            long bytes = files.Sum(f => f.Length); int remaining = files.Count;
            foreach (var file in files.OrderByDescending(f => { int i = recent.IndexOf(f.Name.Substring(0, f.Name.Length - 8)); return i < 0 ? Int32.MaxValue : i; }).ThenBy(f => f.LastWriteTimeUtc)) {
                if (remaining <= FocusCacheLimit && bytes <= FocusByteLimit) break;
                try { if (DataFormat.SafeFocusPath(file.FullName)) { long size = file.Length; File.Delete(file.FullName); remaining--; bytes -= size; Interlocked.Add(ref FocusFreedBytes, size); } } catch { }
            }
            foreach (string path in Directory.GetFiles(options.FocusDir, "*.tmp")) {
                if (DataFormat.SafeFocusPath(path) && (DataFormat.ShortTempName(Path.GetFileName(path)) || Regex.IsMatch(Path.GetFileName(path), @"^(focus-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-[1-5]-[1-5]\.json\.gz|recent\.json|revision\.json)\.[a-f0-9]{32}\.tmp$")) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) try { File.Delete(path); } catch { }
            }
        }
        internal string PreviousUsableFingerprint(string profile, string incoming) {
            List<LibraryProfile> list;
            if (Profiles.TryGetValue(profile, out list)) foreach (var item in list)
                if (item.Source != "bundled" && DataFormat.Str(item.Manifest, "fingerprint") != incoming && ProfileFilesIntact(item)) return DataFormat.Str(item.Manifest, "fingerprint");
            return "";
        }
        internal void AuditLibrary(CancellationToken token) {
            using (var mutex = new Mutex(false, "Local\\LunaAudit_" + DataFormat.Hash(options.Root.ToLowerInvariant()).Substring(0, 20))) {
                bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) return;
                try {
                    var profiles = Profiles.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
                    string revision = AuditRevision(profiles);
                    string checkpoint = Path.Combine(options.CacheDir, "audit-state.json");
                    var saved = DataFormat.SafeLocalPath(checkpoint) ? DataFormat.ReadRecord(checkpoint) : null;
                    int next = 0; bool same = DataFormat.Str(saved, "revision") == revision;
                    if (same && DataFormat.DateValue(DataFormat.Str(saved, "completedAt")) > DateTime.UtcNow.AddDays(-1)) return;
                    if (same) int.TryParse(DataFormat.Str(saved, "next"), out next);
                    if (next < 0) next = 0;
                    var items = profiles.SelectMany(p => p.Entries.OrderBy(e => e.Key).Select(e => new { profile = p, key = e.Key, hash = Convert.ToString(e.Value) })).ToArray();
                    if (next > items.Length) next = 0;
                    try {
                        while (next < items.Length) {
                            token.ThrowIfCancellationRequested();
                            var item = items[next]; bool good;
                            try { good = DataFormat.RecordFileHash(Path.Combine(item.profile.Directory, item.key + ".json.gz")) == item.hash; } catch { good = false; }
                            if (good) { bool ignored; item.profile.Damaged.TryRemove(item.key, out ignored); } else item.profile.Damaged[item.key] = true;
                            next++;
                            if (next % 16 == 0) { SaveAudit(checkpoint, profiles, revision, next, items.Length, false); activity.YieldMaintenance(token); }
                        }
                    } finally {
                        SaveAudit(checkpoint, profiles, revision, next, items.Length, next == items.Length);
                    }
                } finally { mutex.ReleaseMutex(); }
            }
        }
        string AuditRevision(IEnumerable<LibraryProfile> profiles) { return DataFormat.Hash(String.Join(";", profiles.Select(p => p.Directory + ":" + DataFormat.Str(p.Manifest, "fingerprint") + ":" + DataFormat.Str(p.Manifest, "builtAt")))); }
        string AuditKey(LibraryProfile profile, string key) { return profile.Source + ":" + DataFormat.Str(profile.Manifest, "fingerprint") + ":" + key; }
        void SaveAudit(string path, LibraryProfile[] profiles, string revision, int next, int total, bool complete) {
            if (DataFormat.SafeLocalPath(path)) DataFormat.WriteRecord(path, new { revision = revision, next = next, total = total, completedAt = complete ? DataFormat.Stamp() : "", damaged = profiles.SelectMany(p => p.Damaged.Keys.Select(k => AuditKey(p, k))).ToArray() });
        }
        void RestoreAuditedDamage(Dictionary<string, List<LibraryProfile>> profiles) {
            var entries = profiles.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
            var saved = DataFormat.ReadRecord(Path.Combine(options.CacheDir, "audit-state.json"));
            if (DataFormat.Str(saved, "revision") != AuditRevision(entries)) return;
            var damaged = new HashSet<string>(DataFormat.Arr(DataFormat.Get(saved, "damaged")).Select(Convert.ToString));
            foreach (var profile in entries) foreach (string key in profile.Entries.Keys) if (damaged.Contains(AuditKey(profile, key))) profile.Damaged[key] = true;
        }
        internal void RecordSourceFingerprint(string fingerprint) {
            if (!DataFormat.Fingerprint(fingerprint)) return;
            Directory.CreateDirectory(options.LibraryDir);
            DataFormat.WriteRecord(Path.Combine(options.LibraryDir, "source-state.json"), new { fingerprint = fingerprint, checkedAt = DataFormat.Stamp() });
        }


        internal bool IsIntactProfile(string directory, string profile) {
            var found = OpenProfile(directory, profile, "library"); return found != null && ProfileFilesIntact(found);
        }
        internal string[] AvailableProfiles { get { return Profiles.Keys.ToArray(); } }
        internal long TakeFreedBytes() { return Interlocked.Exchange(ref FocusFreedBytes, 0); }
        internal bool NeedsUpdate(string profile, string fingerprint, bool automatic) {
            List<LibraryProfile> list;
            return !Profiles.TryGetValue(profile, out list) || DataFormat.Str(list[0].Manifest, "fingerprint") != fingerprint || !list[0].Damaged.IsEmpty || !automatic && !ProfileFilesIntact(list[0]);
        }
        internal void PublishProfile(string profile, string fingerprint, CancellationToken token) {
            var complete = OpenProfile(Path.Combine(options.LibraryDir, fingerprint, profile), profile, "library");
            if (complete == null || !ProfileFilesIntact(complete)) throw new IOException("新库完整性验证失败，已保留原库");
            token.ThrowIfCancellationRequested();
            DataFormat.WriteRecord(Path.Combine(options.LibraryDir, "active-" + profile + ".json"), new { current = fingerprint, previous = PreviousUsableFingerprint(profile, fingerprint) });
            InitializeLibrary();
        }
        internal void PublishFocusedPair(string pair, Dictionary<string, object> before, Dictionary<string, object> rows, bool fresh, CancellationToken token) {
            string fingerprint = DataFormat.Str(DataFormat.Obj(before["package"]), "sha256");
            string payload = DataFormat.Json().Serialize(rows);
            if (Encoding.UTF8.GetByteCount(payload) > 4 * 1024 * 1024) throw new IOException("英雄更新记录超过限制");
            if (!DataFormat.PlainPath(options.FocusDir)) throw new IOException("英雄更新目录不可写");
            Directory.CreateDirectory(options.FocusDir); token.ThrowIfCancellationRequested();
            DataFormat.WriteCompressed(FocusFile(pair), new { owner = "Luna.Focus.1", pair = pair, fingerprint = fingerprint, manualRefresh = fresh,
                sourceGeneratedAt = DataFormat.Str(DataFormat.Obj(before["meta"]), "generatedAt"), savedAt = DataFormat.Stamp(), payload = payload, sha256 = DataFormat.Hash(payload) });
            DataFormat.WriteRecord(Path.Combine(options.FocusDir, "revision.json"), new { changedAt = DataFormat.Stamp() });
            MaintainFocused();
        }
    }
}

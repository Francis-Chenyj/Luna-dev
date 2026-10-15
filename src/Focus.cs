using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Luna {
    internal static partial class Program {
        static string FocusDir { get { return Path.Combine(CacheDir, "focus"); } }
        static readonly object FocusSync = new object();
        static List<string> FocusRecent;
        static int FocusUpdated;
        static long FocusFreedBytes;
        const int FocusCacheLimit = 64;
        const long FocusByteLimit = 8 * 1024 * 1024;
        static bool SafeFocusPath(string path) { try { return PlainPath(path) && (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0); } catch { return false; } }
        static bool ValidFocus(string pair) { return Regex.IsMatch(pair ?? "", @"^focus-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-[1-5]-[1-5]$"); }
        static string FocusPair(string key) { var p = key.Split('-'); return "focus-" + p[1] + "-" + p[2] + "-" + p[4] + "-" + p[5]; }
        static string FocusProfile(string pair) { var p = pair.Split('-'); return p[3] + "-" + p[4]; }
        static string[] FocusKeys(string pair) { var p = pair.Split('-'); return new[] { "detail-" + p[1] + "-" + p[2] + "-father-" + p[3] + "-" + p[4], "detail-" + p[1] + "-" + p[2] + "-son-" + p[3] + "-" + p[4] }; }
        static string FocusFile(string pair) { if (!ValidFocus(pair)) throw new ArgumentException("英雄更新条件无效"); return Path.Combine(FocusDir, pair + ".json.gz"); }
        static List<string> RecentFocus() {
            lock (FocusSync) {
                if (FocusRecent == null) {
                    var saved = PlainPath(FocusDir) ? ReadRecord(Path.Combine(FocusDir, "recent.json")) : null;
                    FocusRecent = Arr(Get(saved, "pairs")).Select(Convert.ToString).Where(ValidFocus).Distinct().Take(24).ToList();
                }
                return new List<string>(FocusRecent);
            }
        }
        // This records local priority only. It never opens a connection or starts an update.
        static void RegisterFocus(string key) {
            if (!ValidKey(key)) throw new ArgumentException("英雄更新条件无效");
            string pair = FocusPair(key);
            if (!PlainPath(FocusDir)) return;
            lock (FocusSync) {
                var recent = RecentFocus(); recent.Remove(pair); recent.Insert(0, pair);
                FocusRecent = recent.Take(24).ToList();
                Directory.CreateDirectory(FocusDir);
                WriteRecord(Path.Combine(FocusDir, "recent.json"), new { owner = "Luna.Focus.1", pairs = FocusRecent });
            }
        }
        static Dictionary<string, object> ReadFocusedPair(string pair) {
            string path = FocusFile(pair);
            if (!SafeFocusPath(path) || !File.Exists(path)) return null;
            var record = ReadCompressed(path);
            if (Str(record, "owner") != "Luna.Focus.1" || Str(record, "pair") != pair || !Fingerprint(Str(record, "fingerprint")) || DateValue(Str(record, "sourceGeneratedAt")) == DateTime.MinValue || DateValue(Str(record, "savedAt")) == DateTime.MinValue) throw new IOException("英雄更新记录格式异常");
            string payload = Str(record, "payload");
            if (Hash(payload) != Str(record, "sha256")) throw new IOException("英雄更新记录校验失败");
            var rows = Obj(Json().DeserializeObject(payload)); var keys = FocusKeys(pair);
            if (rows.Count != 2 || keys.Any(k => !rows.ContainsKey(k) || !ValidRecord(Obj(rows[k]), k, Str(record, "fingerprint")))) throw new IOException("英雄两侧更新记录不完整");
            record["records"] = rows; return record;
        }
        static Dictionary<string, object> FocusedResult(string key, out bool damaged) {
            damaged = false; var pair = FocusPair(key);
            try {
                var record = ReadFocusedPair(pair); if (record == null) return null;
                List<LibraryProfile> profiles;
                if (Profiles.TryGetValue(ProfileOf(key), out profiles) && profiles.Count > 0) {
                    var best = profiles[0];
                    if (DateValue(Str(best.Manifest, "sourceGeneratedAt")) > DateValue(Str(record, "sourceGeneratedAt"))) return null;
                    if (Str(best.Manifest, "fingerprint") == Str(record, "fingerprint") && best.Damaged.IsEmpty) {
                        bool manual = Get(record, "manualRefresh") is bool && (bool)record["manualRefresh"];
                        if (!manual || (best.Source != "bundled" && DateValue(ProfileUpdatedAt(best)) >= DateValue(Str(record, "savedAt")))) return null;
                    }
                }
                var row = Obj(Obj(record["records"])[key]);
                return new Dictionary<string, object> { { "success", true }, { "data", row["data"] }, { "cache", new {
                    source = "focused", scope = "champion", fetchedAt = Str(row, "fetchedAt"), sourceGeneratedAt = Str(record, "sourceGeneratedAt"),
                    localUpdatedAt = Str(record, "savedAt"), fingerprint = Str(record, "fingerprint"), needsRefresh = false, isFallback = false,
                    warning = ""
                } } };
            } catch (Exception ex) { damaged = true; Log(ex); return null; }
        }
        static string FocusRevision() {
            try { string path = Path.Combine(FocusDir, "revision.json"); return SafeFocusPath(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "0"; }
            catch { return "0"; }
        }
        static bool AlreadyCurrent(string pair, string fingerprint) {
            List<LibraryProfile> profiles;
            if (Profiles.TryGetValue(FocusProfile(pair), out profiles) && profiles.Any(p => Str(p.Manifest, "fingerprint") == fingerprint && FocusKeys(pair).All(k => p.Entries.ContainsKey(k) && !p.Damaged.ContainsKey(k)))) return true;
            try { var saved = ReadFocusedPair(pair); return saved != null && Str(saved, "fingerprint") == fingerprint; } catch { return false; }
        }
        static Dictionary<string, object> FetchFocusRecord(string key, string fingerprint, CancellationToken token, bool fresh) {
            string directory = Path.Combine(LibraryDir, fingerprint, ProfileOf(key)); if (!fresh) Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, key + ".json.gz");
            if (!fresh) try { var saved = ReadCompressed(path); if (ValidRecord(saved, key, fingerprint)) return saved; } catch { }
            var p = key.Split('-');
            var data = FetchLibraryJson(Source + "/api/counter/detail?championId=" + p[1] + "&lane=" + p[2] + "&mode=" + p[3] + "&thresholdLevel=" + p[4] + "&sensitivity=" + p[5], token);
            if (!ValidDetail(data, key)) throw new IOException("原站返回不匹配的英雄更新记录");
            token.ThrowIfCancellationRequested();
            var row = new Dictionary<string, object> { { "key", key }, { "fingerprint", fingerprint }, { "fetchedAt", Stamp() }, { "data", data } };
            // Published full-profile files are immutable. Manual refreshes are
            // saved only as an atomic pair, never over an active manifest entry.
            if (!fresh) WriteCompressed(path, row); return row;
        }
        static void RefreshPriorityPairs(Dictionary<string, object> before, IEnumerable<string> targets, HashSet<string> attempted, int limit, CancellationToken token) {
            string fingerprint = Str(Obj(before["package"]), "sha256");
            for (int count = 0; count < limit; count++) {
                string pair = RecentFocus().FirstOrDefault(p => targets.Contains(FocusProfile(p)) && !attempted.Contains(p));
                if (pair == null) return;
                attempted.Add(pair); if (AlreadyCurrent(pair, fingerprint)) continue;
                var pieces = pair.Split('-');
                var heroes = Arr(Get(Obj(LocalCatalog()["data"]), "heroes")).Select(Obj);
                if (!heroes.Any(h => Str(h, "id") == pieces[1] && Obj(Get(h, "lanes")).ContainsKey(pieces[2]))) continue;
                Progress("优先更新正在查询和最近使用的英雄；原来的本地结果仍可查看。", "focused");
                SaveFocusedPair(pair, before, token, false);
            }
        }
        static void SaveFocusedPair(string pair, Dictionary<string, object> before, CancellationToken token, bool fresh) {
            string fingerprint = Str(Obj(before["package"]), "sha256");
            var rows = new Dictionary<string, object>(); var sync = new object();
            Parallel.ForEach(FocusKeys(pair), new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, key => {
                var row = FetchFocusRecord(key, fingerprint, token, fresh); lock (sync) rows[key] = row;
            });
            token.ThrowIfCancellationRequested();
            var after = SourceStatus(token);
            if (Str(Obj(after["package"]), "sha256") != fingerprint) throw new IOException("更新期间原站换版，未启用混合版本；已保存的可用结果保持不变。");
            string payload = Json().Serialize(rows);
            if (Encoding.UTF8.GetByteCount(payload) > 4 * 1024 * 1024) throw new IOException("英雄更新记录超过限制");
            if (!PlainPath(FocusDir)) throw new IOException("英雄更新目录不可写");
            Directory.CreateDirectory(FocusDir); token.ThrowIfCancellationRequested();
            WriteCompressed(FocusFile(pair), new { owner = "Luna.Focus.1", pair = pair, fingerprint = fingerprint, manualRefresh = fresh,
                sourceGeneratedAt = Str(Obj(before["meta"]), "generatedAt"), savedAt = Stamp(), payload = payload, sha256 = Hash(payload) });
            WriteRecord(Path.Combine(FocusDir, "revision.json"), new { changedAt = Stamp() });
            Interlocked.Increment(ref FocusUpdated); MaintainFocused();
        }
        static long FocusBytes() {
            try { return !Directory.Exists(FocusDir) || !PlainPath(FocusDir) ? 0 : Directory.GetFiles(FocusDir).Where(SafeFocusPath).Sum(p => new FileInfo(p).Length); } catch { return 0; }
        }
        // Bounded, application-owned atomic pairs; unrelated files and links are untouched.
        static void MaintainFocused() {
            if (!Directory.Exists(FocusDir) || !PlainPath(FocusDir)) return;
            var files = new List<FileInfo>(); var recent = RecentFocus();
            foreach (string path in Directory.GetFiles(FocusDir, "focus-*.json.gz")) {
                string pair = Path.GetFileName(path); pair = pair.Substring(0, pair.Length - 8);
                if (!ValidFocus(pair) || !SafeFocusPath(path)) continue;
                try { if (ReadFocusedPair(pair) != null) files.Add(new FileInfo(path)); } catch { QuarantineOwnedFile(path); }
            }
            long bytes = files.Sum(f => f.Length); int remaining = files.Count;
            foreach (var file in files.OrderByDescending(f => { int i = recent.IndexOf(f.Name.Substring(0, f.Name.Length - 8)); return i < 0 ? Int32.MaxValue : i; }).ThenBy(f => f.LastWriteTimeUtc)) {
                if (remaining <= FocusCacheLimit && bytes <= FocusByteLimit) break;
                try { if (SafeFocusPath(file.FullName)) { long size = file.Length; File.Delete(file.FullName); remaining--; bytes -= size; Interlocked.Add(ref FocusFreedBytes, size); } } catch { }
            }
            foreach (string path in Directory.GetFiles(FocusDir, "*.tmp")) {
                if (SafeFocusPath(path) && (ShortTempName(Path.GetFileName(path)) || Regex.IsMatch(Path.GetFileName(path), @"^(focus-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-[1-5]-[1-5]\.json\.gz|recent\.json|revision\.json)\.[a-f0-9]{32}\.tmp$")) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) try { File.Delete(path); } catch { }
            }
        }
        static Dictionary<string, object> ResumeCatalog(Dictionary<string, object> status, CancellationToken token) {
            string fingerprint = Str(Obj(status["package"]), "sha256"), path = Path.Combine(LibraryDir, "catalog-cache.json");
            var saved = ReadRecord(path);
            try {
                string payload = Str(saved, "payload");
                if (Str(saved, "fingerprint") == fingerprint && Hash(payload) == Str(saved, "sha256")) {
                    var catalog = Obj(Json().DeserializeObject(payload)); ExpectedKeys(catalog, "1-5");
                    var heroes = Arr(Get(Obj(catalog["data"]), "heroes")).Select(Obj).ToArray();
                    if (Arr(status["lanes"]).Select(Obj).All(l => heroes.Count(h => Obj(Get(h, "lanes")).ContainsKey(Str(l, "key"))).ToString() == Str(l, "championCount"))) return catalog;
                }
            } catch { }
            var built = BuildLibraryCatalog(status, token); string text = Json().Serialize(built);
            WriteRecord(path, new { fingerprint = fingerprint, payload = text, sha256 = Hash(text) }); return built;
        }
    }
}

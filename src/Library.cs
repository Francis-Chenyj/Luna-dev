using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Luna {
    internal static partial class Program {
        // Queries never call the network. A complete profile is published with one atomic pointer write.
        sealed class LibraryProfile {
            public string Directory, Source;
            public bool Fallback;
            public Dictionary<string, object> Manifest, Entries;
            public readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Damaged = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        }
        static readonly string LibraryDir = Path.Combine(CacheDir, "library");
        static readonly object LibrarySync = new object(), JobSync = new object(), RateSync = new object();
        static volatile Dictionary<string, List<LibraryProfile>> Profiles = new Dictionary<string, List<LibraryProfile>>();
        static readonly Dictionary<string, string> ProfilePointers = new Dictionary<string, string>();
        static CancellationTokenSource UpdateCancellation;
        static bool UpdateBusy, UpdateAutomatic;
        static bool AutomaticUpdatesEnabled = true;
        static int StartupUpdateScheduled;
        const int StartupUpdateDelayMs = 1000;
        static Action<CancellationToken> PriorityPump;
        static string UpdateState = "idle", UpdateMessage = "查询只读取本地数据。需要时可在这里更新。", UpdateProfile = "", LastUpdate = "";
        static int UpdateDone, UpdateTotal;
        static DateTime NextRequest = DateTime.MinValue;
        // The transport is replaceable by the isolated test harness; never configured through HTTP.
        static Func<string, CancellationToken, Dictionary<string, object>> LibraryTransport = DownloadJson;
        static string FileHash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static bool Fingerprint(string value) { return Regex.IsMatch(value ?? "", @"^[a-f0-9]{64}$"); }
        static bool ValidKey(string key) { return Regex.IsMatch(key ?? "", @"^detail-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-(father|son)-[1-5]-[1-5]$"); }
        static string ProfileOf(string key) { var pieces = key.Split('-'); return pieces[4] + "-" + pieces[5]; }
        static DateTime DateValue(string value) { DateTime result; return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out result) ? result.ToUniversalTime() : DateTime.MinValue; }
        static bool IsArray(object value) { return value is object[] || value is System.Collections.ArrayList; }

        static bool ValidDetail(Dictionary<string, object> data, string key) {
            if (!ValidKey(key) || !(Get(data, "success") is bool) || !(bool)data["success"]) return false;
            var pieces = key.Split('-');
            if (Str(Obj(Get(data, "champion")), "championId") != pieces[1] || Str(data, "lane") != pieces[2] || Str(data, "mode") != pieces[3] || !IsArray(Get(data, "sections"))) return false;
            var seen = new HashSet<string>();
            foreach (var section in Arr(data["sections"])) {
                var s = Obj(section); string sectionKey = Str(s, "key");
                // The public son response also uses *_father group keys; data.mode identifies direction.
                if (!Regex.IsMatch(sectionKey, @"^(all_segment|(high_mid|high|high_low|mid|mid_low|low)_segment_(father|son))$") || !seen.Add(sectionKey) || !IsArray(Get(s, "items"))) return false;
                foreach (var item in Arr(s["items"])) {
                    var row = Obj(item); int id;
                    if (!int.TryParse(Str(row, "championId"), out id) || id < 1 || id > 10000) return false;
                }
            }
            return seen.Count <= 7;
        }
        static List<string> ExpectedKeys(Dictionary<string, object> catalog, string profile) {
            var keys = new HashSet<string>(); var ids = new HashSet<int>(); var data = Obj(Get(catalog, "data"));
            if (!IsArray(Get(data, "heroes")) || !IsArray(Get(data, "lanes"))) throw new IOException("离线目录格式不完整");
            foreach (var value in Arr(data["heroes"])) {
                var hero = Obj(value); int id;
                if (!int.TryParse(Str(hero, "id"), out id) || id < 1 || id > 10000 || !ids.Add(id)) throw new IOException("英雄编号异常或重复");
                foreach (var lane in Obj(Get(hero, "lanes")).Keys) {
                    if (!Lanes.Contains(lane)) throw new IOException("分路无效");
                    foreach (var mode in new[] { "father", "son" })
                        if (!keys.Add("detail-" + id + "-" + lane + "-" + mode + "-" + profile)) throw new IOException("目录存在重复英雄");
                }
            }
            if (keys.Count == 0) throw new IOException("原站未提供可查询的分路");
            return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
        static LibraryProfile OpenProfile(string directory, string profile, string source) {
            try {
                var manifest = ReadRecord(Path.Combine(directory, "manifest.json"));
                if (manifest == null || Str(manifest, "schema") != "1" || Str(manifest, "profile") != profile || !Fingerprint(Str(manifest, "fingerprint")) || DateValue(Str(manifest, "sourceGeneratedAt")) == DateTime.MinValue) return null;
                var entries = Obj(Get(manifest, "entries")); var expected = ExpectedKeys(Obj(Get(manifest, "catalog")), profile);
                if (entries.Count != expected.Count || Str(manifest, "expected") != expected.Count.ToString() || expected.Any(key => !entries.ContainsKey(key) || !Fingerprint(Str(entries, key)))) return null;
                var result = new LibraryProfile { Directory = directory, Source = source, Manifest = manifest, Entries = entries };
                foreach (var entry in entries) {
                    try { if (FileHash(File.ReadAllBytes(Path.Combine(directory, entry.Key + ".json.gz"))) != Convert.ToString(entry.Value)) result.Damaged[entry.Key] = true; }
                    catch { result.Damaged[entry.Key] = true; }
                }
                return result;
            } catch (Exception ex) { Log(ex); return null; }
        }
        static string PointerIdentity(Dictionary<string, object> pointer) { return Str(pointer, "current") + ":" + Str(pointer, "previous"); }
        static List<LibraryProfile> LoadProfile(string profile, Dictionary<string, object> pointer) {
            var list = new List<LibraryProfile>();
            bool unavailableCurrent = pointer == null && File.Exists(Path.Combine(LibraryDir, "active-" + profile + ".json"));
            if (pointer != null) foreach (string field in new[] { "current", "previous" }) {
                string fingerprint = Str(pointer, field);
                if (!Fingerprint(fingerprint)) { if (field == "current") unavailableCurrent = true; continue; }
                var record = OpenProfile(Path.Combine(LibraryDir, fingerprint, profile), profile, field == "current" ? "library" : "previous");
                if (record != null) list.Add(record);
                else if (field == "current") unavailableCurrent = true;
            }
            var bundled = OpenProfile(Path.Combine(Root, "data", "library", profile), profile, "bundled");
            if (bundled != null) list.Add(bundled);
            foreach (var item in list) item.Fallback = unavailableCurrent || item.Source == "previous";
            return list.OrderByDescending(p => DateValue(Str(p.Manifest, "sourceGeneratedAt"))).ToList();
        }
        static string RecordedTime(string value) { return DateValue(value) == DateTime.MinValue ? "" : value; }
        static string ProfileUpdatedAt(LibraryProfile profile) {
            // A bundled build time belongs to the publisher, not to this user's computer.
            return profile == null || profile.Source == "bundled" ? "" : RecordedTime(Str(profile.Manifest, "builtAt"));
        }
        static bool RefreshProfile(string profile) {
            var pointer = ReadRecord(Path.Combine(LibraryDir, "active-" + profile + ".json"));
            string identity = PointerIdentity(pointer), previous;
            lock (LibrarySync) {
                if (ProfilePointers.TryGetValue(profile, out previous) && previous == identity) return false;
            }
            // Disk validation must never hold the lock shared with local queries.
            var list = LoadProfile(profile, pointer);
            if (PointerIdentity(ReadRecord(Path.Combine(LibraryDir, "active-" + profile + ".json"))) != identity) return false;
            lock (LibrarySync) {
                if (ProfilePointers.TryGetValue(profile, out previous) && previous == identity) return false;
                var next = new Dictionary<string, List<LibraryProfile>>(Profiles);
                if (list.Count > 0) next[profile] = list; else next.Remove(profile);
                ProfilePointers[profile] = identity; Profiles = next; return true;
            }
        }
        static void InitializeLibrary() {
            var next = new Dictionary<string, List<LibraryProfile>>();
            var pointers = new Dictionary<string, string>();
            for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) {
                string profile = t + "-" + s;
                var pointer = ReadRecord(Path.Combine(LibraryDir, "active-" + profile + ".json"));
                var list = LoadProfile(profile, pointer);
                pointers[profile] = PointerIdentity(pointer);
                if (list.Count > 0) next[profile] = list;
            }
            // Publish a ready snapshot briefly; hashing the entire library happens above.
            lock (LibrarySync) {
                foreach (var item in pointers) ProfilePointers[item.Key] = item.Value;
                Profiles = next;
            }
        }
        static Dictionary<string, object> ReadCompressed(string path) {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException("压缩记录过大");
            using (var stream = File.OpenRead(path)) using (var gzip = new GZipStream(stream, CompressionMode.Decompress)) using (var memory = new MemoryStream()) {
                byte[] buffer = new byte[8192]; int n;
                while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0) { memory.Write(buffer, 0, n); if (memory.Length > 16 * 1024 * 1024) throw new IOException("关系记录过大"); }
                return Obj(Json().DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())));
            }
        }
        static void WriteCompressed(string path, object record) {
            string temporary = TemporaryPath(path);
            try {
                using (var file = File.Create(temporary)) using (var gzip = new GZipStream(file, CompressionMode.Compress)) {
                    byte[] bytes = Encoding.UTF8.GetBytes(Json().Serialize(record)); gzip.Write(bytes, 0, bytes.Length);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static bool ValidRecord(Dictionary<string, object> record, string key, string fingerprint) {
            return record != null && Str(record, "key") == key && Str(record, "fingerprint") == fingerprint && DateValue(Str(record, "fetchedAt")) != DateTime.MinValue && ValidDetail(Obj(Get(record, "data")), key);
        }
        static Dictionary<string, object> LocalDetail(string key) {
            if (!ValidKey(key)) throw new ArgumentException("查询条件无效");
            RefreshProfile(ProfileOf(key));
            bool damaged; var focused = FocusedResult(key, out damaged);
            if (focused != null) return focused;
            var result = ReadLocalDetail(key, true);
            if (damaged && Get(result, "data") != null) {
                var cache = Obj(Json().DeserializeObject(Json().Serialize(result["cache"])));
                cache["isFallback"] = true; cache["warning"] = "当前英雄的更新记录不可用，已使用原有完整数据。"; result["cache"] = cache;
            }
            return result;
        }
        static Dictionary<string, object> ReadLocalDetail(string key, bool mayRefresh) {
            List<LibraryProfile> list; bool damaged = false, skipped = false;
            if (Profiles.TryGetValue(ProfileOf(key), out list)) foreach (var profile in list) {
                if (!profile.Entries.ContainsKey(key)) { skipped = true; continue; }
                try {
                    string path = Path.Combine(profile.Directory, key + ".json.gz");
                    if (FileHash(File.ReadAllBytes(path)) != Str(profile.Entries, key)) throw new IOException("记录校验失败：" + key);
                    var record = ReadCompressed(path);
                    if (!ValidRecord(record, key, Str(profile.Manifest, "fingerprint"))) throw new IOException("查询记录与条件不一致：" + key);
                    bool ignored; profile.Damaged.TryRemove(key, out ignored);
                    bool fallback = damaged || skipped || profile.Fallback;
                    return new Dictionary<string, object> { { "success", true }, { "data", record["data"] }, { "cache", new {
                        source = profile.Source, fetchedAt = Str(record, "fetchedAt"), sourceGeneratedAt = Str(profile.Manifest, "sourceGeneratedAt"), fingerprint = Str(profile.Manifest, "fingerprint"), needsRefresh = false,
                        localUpdatedAt = ProfileUpdatedAt(profile), isFallback = fallback,
                        warning = fallback ? "优先使用的数据未能提供此结果，已使用备用数据。可在数据管理中更新或恢复程序包。" : ""
                    } } };
                } catch (Exception ex) {
                    // Another local process may publish and retire old data during this read.
                    if (mayRefresh && RefreshProfile(ProfileOf(key))) return ReadLocalDetail(key, false);
                    damaged = true; profile.Damaged[key] = true; Log(ex);
                }
            }
            // Keep 1.2 user records as an exact-query fallback, never as a different filter profile.
            foreach (string directory in new[] { CacheDir, Path.Combine(Root, "data", "seeds") }) {
                var record = ReadRecord(Path.Combine(directory, key + ".json"));
                if (record == null || !ValidDetail(Obj(Get(record, "data")), key)) continue;
                bool bundled = directory != CacheDir;
                return new Dictionary<string, object> { { "success", true }, { "data", record["data"] }, { "cache", new {
                    source = "legacy", origin = bundled ? "bundled" : "downloaded", isFallback = true,
                    fetchedAt = Str(record, "fetchedAt"), sourceGeneratedAt = RecordedTime(Str(record, "sourceGeneratedAt")),
                    localUpdatedAt = bundled ? "" : RecordedTime(Str(record, "fetchedAt")), needsRefresh = false,
                    warning = "这是旧版保存的单条结果；数据管理可补齐此筛选组合的完整离线库。"
                } } };
            }
            return new Dictionary<string, object> { { "success", true }, { "data", null }, { "cache", new {
                source = "missing", reason = damaged ? "damaged" : "not_saved", needsRefresh = false, warning = damaged ? "这条本地数据损坏，且没有可用备份。请更新离线库或重新解压程序包。" : "本地尚未保存这个筛选组合的结果。可恢复默认条件，或在“数据管理”中下载此组合；不会用其他条件的结果代替。"
            } } };
        }
        static Dictionary<string, object> LocalCatalog() {
            RefreshProfile("1-5");
            List<LibraryProfile> list;
            if (Profiles.TryGetValue("1-5", out list)) return Envelope(Obj(list[0].Manifest["catalog"]), list[0].Source);
            var fallback = ReadRecord(Path.Combine(Root, "data", "catalog.json"));
            if (fallback == null) throw new IOException("缺少英雄目录");
            return Envelope(fallback, "bundled", "完整离线库未能加载，请恢复 data 文件夹。");
        }
        static object LibraryInfo(string profile) {
            RefreshProfile(profile);
            List<LibraryProfile> list; Profiles.TryGetValue(profile, out list);
            var current = list == null ? null : list[0];
            var checkedSource = ReadRecord(Path.Combine(LibraryDir, "source-state.json"));
            string checkedAt = RecordedTime(Str(checkedSource, "checkedAt"));
            if (!Fingerprint(Str(checkedSource, "fingerprint")) || DateValue(checkedAt) > DateTime.UtcNow.AddMinutes(5)) checkedAt = "";
            int expected = ExpectedKeys(new Dictionary<string, object> { { "data", LocalCatalog()["data"] } }, profile).Count;
            var snapshot = Profiles;
            var savedProfiles = snapshot.Where(p => p.Value[0].Damaged.IsEmpty).Select(p => p.Key).OrderBy(p => p).ToArray();
            string revision = String.Join(";", snapshot.OrderBy(p => p.Key).Select(p => p.Key + ":" + Str(p.Value[0].Manifest, "fingerprint"))) + ";focused:" + FocusRevision();
            lock (JobSync) return new {
                success = true, offline = Offline, profile = profile, complete = current != null && current.Damaged.IsEmpty, saved = current == null ? 0 : current.Entries.Count - current.Damaged.Count,
                expected = expected, automaticUpdates = !Offline && AutomaticUpdatesEnabled,
                sourceGeneratedAt = current == null ? "" : Str(current.Manifest, "sourceGeneratedAt"), builtAt = current == null ? "" : Str(current.Manifest, "builtAt"),
                source = current == null ? "missing" : current.Source, isFallback = current != null && current.Fallback,
                localUpdatedAt = ProfileUpdatedAt(current), sourceCheckedAt = checkedAt,
                profiles = savedProfiles, revision = revision,
                job = new { busy = UpdateBusy, automatic = UpdateAutomatic, kind = UpdateKind, state = UpdateState, message = UpdateMessage, done = UpdateDone, total = UpdateTotal, profile = UpdateProfile, checkedAt = LastUpdate, focusedUpdated = FocusUpdated },
                storage = Storage
            };
        }
        static void StartLibraryUpdate(string profile, bool all) {
            if (Offline) throw new ArgumentException("当前为离线模式。请使用“启动Luna.cmd”后再更新。");
            BeginLibraryUpdate(profile, all, false, 0);
        }
        static void ScheduleStartupUpdate() {
            if (Offline || !AutomaticUpdatesEnabled || Interlocked.CompareExchange(ref StartupUpdateScheduled, 1, 0) != 0) return;
            BeginLibraryUpdate("1-5", false, true, StartupUpdateDelayMs);
        }
        static void BeginLibraryUpdate(string profile, bool all, bool automatic, int delayMs) {
            lock (JobSync) {
                if (UpdateBusy) return;
                if (UpdateCancellation != null) UpdateCancellation.Dispose();
                UpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
                UpdateBusy = true; UpdateAutomatic = automatic; UpdateKind = "library";
                UpdateState = delayMs > 0 ? "scheduled" : "checking";
                UpdateMessage = delayMs > 0 ? "启动后约 1 秒检查版本，优先更新正在查询的英雄；现在即可查本地数据。" : "正在检查原站数据版本…";
                UpdateDone = 0; UpdateTotal = 0; FocusUpdated = 0; UpdateProfile = profile;
                var cancellation = UpdateCancellation;
                var thread = new Thread(() => UpdateLibrary(profile, all, automatic, delayMs, cancellation.Token));
                thread.IsBackground = true; thread.Priority = ThreadPriority.BelowNormal; thread.Start();
            }
        }
        static void CancelLibraryUpdate() { lock (JobSync) { CancelQueuedHeroes(); if (UpdateBusy && UpdateCancellation != null) { UpdateCancellation.Cancel(); UpdateMessage = "正在停止，已完成的下载会保留供下次继续。"; } } }
        static void Progress(string message, string state) { lock (JobSync) { UpdateMessage = message; UpdateState = state; } }
        static void WaitForSource(CancellationToken token) {
            TimeSpan delay;
            lock (RateSync) { var now = DateTime.UtcNow; if (NextRequest < now) NextRequest = now; delay = NextRequest - now; NextRequest = NextRequest.AddMilliseconds(600); }
            if (token.WaitHandle.WaitOne(delay)) token.ThrowIfCancellationRequested();
        }
        static Dictionary<string, object> FetchLibraryJson(string url, CancellationToken token) {
            for (int attempt = 0; ; attempt++) {
                token.ThrowIfCancellationRequested(); WaitForSource(token);
                try {
                    var value = LibraryTransport(url, token);
                    if (Get(value, "success") is bool && !(bool)value["success"]) throw new IOException("原站返回失败状态");
                    return value;
                } catch (OperationCanceledException) { throw; }
                catch (Exception error) {
                    token.ThrowIfCancellationRequested();
                    var web = error as WebException; var response = web == null ? null : web.Response as HttpWebResponse;
                    if (attempt >= 2 || (response != null && (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized))) throw;
                    int seconds = 2 << attempt, advised;
                    if (response != null && int.TryParse(response.Headers["Retry-After"], out advised)) seconds = Math.Max(seconds, Math.Min(60, advised));
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(seconds))) token.ThrowIfCancellationRequested();
                }
            }
        }
        static Dictionary<string, object> SourceStatus(CancellationToken token) {
            var status = FetchLibraryJson(Source + "/api/counter/status", token);
            if (!(Get(status, "ready") is bool) || !(bool)status["ready"] || !Fingerprint(Str(Obj(Get(status, "package")), "sha256")) || DateValue(Str(Obj(Get(status, "meta")), "generatedAt")) == DateTime.MinValue) throw new IOException("原站关系数据尚未就绪");
            RecordSourceFingerprint(Str(Obj(status["package"]), "sha256"));
            return status;
        }
        static Dictionary<string, object> BuildLibraryCatalog(Dictionary<string, object> status, CancellationToken token) {
            var heroes = new Dictionary<int, Dictionary<string, object>>(); string version = "";
            try {
                var raw = FetchLibraryJson(HeroSource, token); version = Str(raw, "version");
                foreach (var item in Arr(Get(raw, "hero"))) {
                    var h = Obj(item); int id = int.Parse(Str(h, "heroId"));
                    heroes[id] = new Dictionary<string, object> { { "id", id }, { "name", Str(h, "title") }, { "title", Str(h, "name") }, { "en", Str(h, "alias") }, { "keywords", Str(h, "keywords") }, { "lanes", new Dictionary<string, object>() } };
                }
                if (heroes.Count == 0) throw new IOException("英雄姓名表不可用");
            } catch (Exception ex) {
                token.ThrowIfCancellationRequested(); Log(ex); heroes.Clear();
                var data = Obj(LocalCatalog()["data"]); version = Str(data, "version");
                foreach (var item in Arr(Get(data, "heroes"))) { var h = new Dictionary<string, object>(Obj(item)); h["lanes"] = new Dictionary<string, object>(); heroes[int.Parse(Str(h, "id"))] = h; }
            }
            foreach (var lane in Lanes) {
                if (PriorityPump != null) PriorityPump(token);
                var response = FetchLibraryJson(Source + "/api/counter/champions?mode=father&lane=" + lane, token);
                if (!IsArray(Get(response, "champions"))) throw new IOException("分路目录不可用：" + lane);
                var rows = Arr(response["champions"]).ToArray();
                var expected = Arr(Get(status, "lanes")).Select(Obj).FirstOrDefault(l => Str(l, "key") == lane);
                if (rows.Length == 0 || expected == null || Str(expected, "championCount") != rows.Length.ToString()) throw new IOException("原站分路目录数量不一致，请稍后更新");
                var ids = new HashSet<int>();
                foreach (var item in rows) {
                    var h = Obj(item); int id = int.Parse(Str(h, "championId"));
                    if (!ids.Add(id)) throw new IOException("原站目录含重复英雄");
                    if (!heroes.ContainsKey(id)) heroes[id] = new Dictionary<string, object> { { "id", id }, { "name", Str(h, "championNameEn") }, { "title", Str(h, "championNameCn") }, { "en", Str(h, "championNameEn") }, { "keywords", "" }, { "lanes", new Dictionary<string, object>() } };
                    Obj(heroes[id]["lanes"])[lane] = Get(h, "displayGames", Get(h, "games", 0)); heroes[id]["sourceTitle"] = Str(h, "championNameCn");
                }
            }
            return Obj(Json().DeserializeObject(Json().Serialize(new { data = new { heroes = heroes.Values.OrderBy(h => Convert.ToInt32(h["id"])).ToArray(), lanes = Lanes.Select((l, i) => new { key = l, label = Labels[i] }).ToArray(), version = version, updatedAt = Stamp() }, fetchedAt = Stamp() })));
        }
        static void UpdateLibrary(string requested, bool all, bool automatic, int delayMs, CancellationToken token) {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\LunaData_" + Hash(Root).Substring(0, 20))) try {
                if (delayMs > 0) token.WaitHandle.WaitOne(delayMs);
                token.ThrowIfCancellationRequested();
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired && automatic) {
                    Progress("其他窗口正在整理或更新，正在后台等待；本地查询不受等待影响。", "waiting");
                    try { acquired = WaitHandle.WaitAny(new WaitHandle[] { mutex, token.WaitHandle }, 30000) == 0; }
                    catch (AbandonedMutexException) { acquired = true; }
                    token.ThrowIfCancellationRequested();
                }
                if (!acquired) throw new IOException("正在整理或更新离线数据，请稍后再试；其他 Luna 窗口也可能正在更新。");
                Progress("正在后台检查原站数据版本；可继续查询本地数据。", "checking");
                var before = SourceStatus(token); string fingerprint = Str(Obj(before["package"]), "sha256");
                InitializeLibrary(); // Include profiles published by another local Luna process.
                MaintainStorage(fingerprint, token);
                var targets = new List<string> { "1-5", requested };
                targets.AddRange(Profiles.Keys);
                if (all) for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) targets.Add(t + "-" + s);
                targets = targets.Distinct().ToList();
                var attempted = new HashSet<string>();
                PriorityPump = ct => { ServiceHeroRequests(ct); RefreshPriorityPairs(before, targets, attempted, 1, ct); };
                ServiceHeroRequests(token);
                // Publish the user's two directions before collecting the full lane catalog.
                RefreshPriorityPairs(before, targets, attempted, 2, token);
                var pending = targets.Where(p => { List<LibraryProfile> list; return !Profiles.TryGetValue(p, out list) || Str(list[0].Manifest, "fingerprint") != fingerprint || !ProfileFilesIntact(list[0]); }).ToList();
                if (pending.Count == 0) { Progress("检查完成：数据版本未变化，未下载新数据。本机更新时间保持不变。", "complete"); return; }
                Progress("正在核对五个分路的完整英雄目录…", "checking");
                var catalog = ResumeCatalog(before, token);
                int total = pending.Sum(p => ExpectedKeys(catalog, p).Count);
                lock (JobSync) UpdateTotal = total;
                foreach (string profile in pending) {
                    token.ThrowIfCancellationRequested();
                    lock (JobSync) UpdateProfile = profile;
                    string directory = Path.Combine(LibraryDir, fingerprint, profile); Directory.CreateDirectory(directory);
                    var entries = new Dictionary<string, object>(); var entriesLock = new object();
                    var keys = ExpectedKeys(catalog, profile);
                    Progress("正在保存完整离线库，可继续查询。中断后再次更新可续传。", "downloading");
                    Parallel.ForEach(keys, new ParallelOptions { MaxDegreeOfParallelism = automatic ? 1 : 2, CancellationToken = token }, key => {
                        if (PriorityPump != null) { if (automatic) PriorityPump(token); else ServiceHeroRequests(token); Progress("正在继续补齐完整离线库；已完成的英雄更新可以先用。", "downloading"); }
                        string path = Path.Combine(directory, key + ".json.gz"); bool valid = false;
                        try { valid = File.Exists(path) && ValidRecord(ReadCompressed(path), key, fingerprint); } catch { }
                        if (!valid) try {
                            var pair = ReadFocusedPair(FocusPair(key));
                            if (pair != null && Str(pair, "fingerprint") == fingerprint) {
                                WriteCompressed(path, Obj(pair["records"])[key]); valid = true;
                            }
                        } catch { }
                        if (!valid) {
                            var parts = key.Split('-');
                            var data = FetchLibraryJson(Source + "/api/counter/detail?championId=" + parts[1] + "&lane=" + parts[2] + "&mode=" + parts[3] + "&thresholdLevel=" + parts[4] + "&sensitivity=" + parts[5], token);
                            if (!ValidDetail(data, key)) throw new IOException("原站返回了不匹配的数据：" + key);
                            token.ThrowIfCancellationRequested(); WriteCompressed(path, new { key = key, fingerprint = fingerprint, fetchedAt = Stamp(), data = data });
                        }
                        string hash = FileHash(File.ReadAllBytes(path)); lock (entriesLock) entries[key] = hash;
                        lock (JobSync) UpdateDone++;
                    });
                    token.ThrowIfCancellationRequested(); Progress("正在验证完整性与原站版本…", "verifying");
                    var after = SourceStatus(token);
                    if (Str(Obj(after["package"]), "sha256") != fingerprint) throw new IOException("下载期间原站版本发生变化。旧库仍可使用，请再次更新以获取新版本。");
                    if (entries.Count != keys.Count) throw new IOException("离线数据尚不完整，不替换已有版本");
                    var manifest = new { schema = 1, source = Source + "/counter", profile = profile, fingerprint = fingerprint, sourceGeneratedAt = Str(Obj(before["meta"]), "generatedAt"), builtAt = Stamp(), expected = keys.Count, entries = entries, catalog = catalog };
                    WriteRecord(Path.Combine(directory, "manifest.json"), manifest);
                    var complete = OpenProfile(directory, profile, "library");
                    if (complete == null || !ProfileFilesIntact(complete)) throw new IOException("新库完整性验证失败，已保留原库");
                    token.ThrowIfCancellationRequested();
                    string pointerPath = Path.Combine(LibraryDir, "active-" + profile + ".json");
                    string previous = PreviousUsableFingerprint(profile, fingerprint);
                    WriteRecord(pointerPath, new { current = fingerprint, previous = previous }); InitializeLibrary();
                }
                Progress("离线库已更新并保存。查询继续使用本地数据，无需升级程序。", "complete");
            } catch (Exception ex) {
                if (token.IsCancellationRequested) Progress("更新已停止；完整旧库仍可用。下次更新会继续已下载的部分。", "cancelled");
                else { Log(ex); Progress((automatic ? "自动更新未完成，原有离线库仍可查询。" : "更新未完成，原有离线库仍可查询。") + ex.GetBaseException().Message + " 可手动重试，或下次正常启动时自动检查并续传。", "error"); }
            } finally {
                PriorityPump = null;
                if (acquired) { MaintainStorage("", Shutdown.Token); mutex.ReleaseMutex(); }
                if (token.IsCancellationRequested) CancelQueuedHeroes();
                FinishUpdateWorker();
            }
        }
        static bool ProfileFilesIntact(LibraryProfile profile) {
            try { return profile.Entries.All(item => { string path = Path.Combine(profile.Directory, item.Key + ".json.gz"); return File.Exists(path) && FileHash(File.ReadAllBytes(path)) == Convert.ToString(item.Value); }); }
            catch { return false; }
        }
    }
}

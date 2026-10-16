using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Luna {
    internal sealed class DataUpdateCoordinator {
        readonly RuntimeOptions options;
        readonly BackgroundWork workers;
        readonly LibraryStore store;
        readonly StorageMaintenance maintenance;
        readonly DataSourceClient source;
        readonly DiagnosticLog diagnostics;
        readonly IRuntimeClock clock;
        internal DataUpdateCoordinator(RuntimeOptions options, BackgroundWork workers, LibraryStore store, StorageMaintenance maintenance, DataSourceClient source, DiagnosticLog diagnostics, bool automaticUpdates, IRuntimeClock clock) {
            this.options = options;
            this.workers = workers;
            this.store = store;
            this.maintenance = maintenance;
            this.source = source;
            this.diagnostics = diagnostics;
            this.clock = clock;
            AutomaticUpdatesEnabled = !options.CommandLineDisablesUpdates && automaticUpdates;
        }
        readonly object JobSync = new object(), RateSync = new object();
        CancellationTokenSource UpdateCancellation;
        bool UpdateBusy, UpdateAutomatic;
        bool AutomaticUpdatesEnabled = true;
        int StartupUpdateScheduled;
        const int StartupUpdateDelayMs = 1000;
        Action<CancellationToken> PriorityPump;
        string UpdateState = "idle", UpdateMessage = "查询只读取本地数据。需要时可在这里更新。", UpdateProfile = "", LastUpdate = "";
        int UpdateDone, UpdateTotal;
        DateTime NextRequest = DateTime.MinValue;
        internal void StartLibraryUpdate(string profile, bool all) {
            if (options.Offline) throw new ArgumentException("当前为离线模式。请使用“启动Luna.cmd”后再更新。");
            BeginLibraryUpdate(profile, all, false, 0);
        }
        internal void ScheduleStartupUpdate() {
            if (options.Offline || !AutomaticUpdatesEnabled || Interlocked.CompareExchange(ref StartupUpdateScheduled, 1, 0) != 0) return;
            BeginLibraryUpdate("1-5", false, true, StartupUpdateDelayMs);
        }
        void BeginLibraryUpdate(string profile, bool all, bool automatic, int delayMs) {
            lock (JobSync) {
                if (UpdateBusy || workers.IsCancellationRequested) return;
                if (UpdateCancellation != null) UpdateCancellation.Dispose();
                UpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(workers.Token);
                UpdateBusy = true; UpdateAutomatic = automatic; UpdateKind = "library";
                UpdateState = delayMs > 0 ? "scheduled" : "checking";
                UpdateMessage = delayMs > 0 ? "启动后约 1 秒检查版本，优先更新正在查询的英雄；现在即可查本地数据。" : "正在检查原站数据版本…";
                UpdateDone = 0; UpdateTotal = 0; FocusUpdated = 0; UpdateProfile = profile;
                var cancellation = UpdateCancellation;
                if (!workers.TryStart(() => UpdateLibrary(profile, all, automatic, delayMs, cancellation.Token), "Luna data update")) {
                    UpdateBusy = false; cancellation.Dispose(); UpdateCancellation = null;
                }
            }
        }
        internal void CancelLibraryUpdate() { lock (JobSync) { CancelQueuedHeroes(); if (UpdateBusy && UpdateCancellation != null) { UpdateCancellation.Cancel(); UpdateMessage = "正在停止，已完成的下载会保留供下次继续。"; } } }
        void Progress(string message, string state) { lock (JobSync) { UpdateMessage = message; UpdateState = state; } }
        void WaitForSource(CancellationToken token) {
            DateTime due;
            lock (RateSync) { var now = clock.UtcNow; if (NextRequest < now) NextRequest = now; due = NextRequest; NextRequest = NextRequest.AddMilliseconds(600); }
            clock.WaitUntil(due, token);
        }
        Dictionary<string, object> FetchLibraryJson(string url, CancellationToken token) {
            for (int attempt = 0; ; attempt++) {
                token.ThrowIfCancellationRequested(); WaitForSource(token);
                try {
                    var value = source.Fetch(url, token);
                    if (DataFormat.Get(value, "success") is bool && !(bool)value["success"]) throw new IOException("原站返回失败状态");
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
        internal Dictionary<string, object> SourceStatus(CancellationToken token) {
            var status = FetchLibraryJson(DataFormat.Source + "/api/counter/status", token);
            if (!(DataFormat.Get(status, "ready") is bool) || !(bool)status["ready"] || !DataFormat.Fingerprint(DataFormat.Str(DataFormat.Obj(DataFormat.Get(status, "package")), "sha256")) || DataFormat.DateValue(DataFormat.Str(DataFormat.Obj(DataFormat.Get(status, "meta")), "generatedAt")) == DateTime.MinValue) throw new IOException("原站关系数据尚未就绪");
            store.RecordSourceFingerprint(DataFormat.Str(DataFormat.Obj(status["package"]), "sha256"));
            return status;
        }
        Dictionary<string, object> BuildLibraryCatalog(Dictionary<string, object> status, CancellationToken token) {
            var heroes = new Dictionary<int, Dictionary<string, object>>(); string version = "";
            try {
                var raw = FetchLibraryJson(DataFormat.HeroSource, token); version = DataFormat.Str(raw, "version");
                foreach (var item in DataFormat.Arr(DataFormat.Get(raw, "hero"))) {
                    var h = DataFormat.Obj(item); int id = int.Parse(DataFormat.Str(h, "heroId"));
                    heroes[id] = new Dictionary<string, object> { { "id", id }, { "name", DataFormat.Str(h, "title") }, { "title", DataFormat.Str(h, "name") }, { "en", DataFormat.Str(h, "alias") }, { "keywords", DataFormat.Str(h, "keywords") }, { "lanes", new Dictionary<string, object>() } };
                }
                if (heroes.Count == 0) throw new IOException("英雄姓名表不可用");
            } catch (Exception ex) {
                token.ThrowIfCancellationRequested(); diagnostics.Log(ex); heroes.Clear();
                var data = DataFormat.Obj(store.LocalCatalog()["data"]); version = DataFormat.Str(data, "version");
                foreach (var item in DataFormat.Arr(DataFormat.Get(data, "heroes"))) { var h = new Dictionary<string, object>(DataFormat.Obj(item)); h["lanes"] = new Dictionary<string, object>(); heroes[int.Parse(DataFormat.Str(h, "id"))] = h; }
            }
            foreach (var lane in DataFormat.Lanes) {
                if (PriorityPump != null) PriorityPump(token);
                var response = FetchLibraryJson(DataFormat.Source + "/api/counter/champions?mode=father&lane=" + lane, token);
                if (!DataFormat.IsArray(DataFormat.Get(response, "champions"))) throw new IOException("分路目录不可用：" + lane);
                var rows = DataFormat.Arr(response["champions"]).ToArray();
                var expected = DataFormat.Arr(DataFormat.Get(status, "lanes")).Select(DataFormat.Obj).FirstOrDefault(l => DataFormat.Str(l, "key") == lane);
                if (rows.Length == 0 || expected == null || DataFormat.Str(expected, "championCount") != rows.Length.ToString()) throw new IOException("原站分路目录数量不一致，请稍后更新");
                var ids = new HashSet<int>();
                foreach (var item in rows) {
                    var h = DataFormat.Obj(item); int id = int.Parse(DataFormat.Str(h, "championId"));
                    if (!ids.Add(id)) throw new IOException("原站目录含重复英雄");
                    if (!heroes.ContainsKey(id)) heroes[id] = new Dictionary<string, object> { { "id", id }, { "name", DataFormat.Str(h, "championNameEn") }, { "title", DataFormat.Str(h, "championNameCn") }, { "en", DataFormat.Str(h, "championNameEn") }, { "keywords", "" }, { "lanes", new Dictionary<string, object>() } };
                    DataFormat.Obj(heroes[id]["lanes"])[lane] = DataFormat.Get(h, "displayGames", DataFormat.Get(h, "games", 0)); heroes[id]["sourceTitle"] = DataFormat.Str(h, "championNameCn");
                }
            }
            return DataFormat.Obj(DataFormat.Json().DeserializeObject(DataFormat.Json().Serialize(new { data = new { heroes = heroes.Values.OrderBy(h => Convert.ToInt32(h["id"])).ToArray(), lanes = DataFormat.Lanes.Select((l, i) => new { key = l, label = DataFormat.Labels[i] }).ToArray(), version = version, updatedAt = DataFormat.Stamp() }, fetchedAt = DataFormat.Stamp() })));
        }
        void UpdateLibrary(string requested, bool all, bool automatic, int delayMs, CancellationToken token) {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\LunaData_" + DataFormat.Hash(options.Root).Substring(0, 20))) try {
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
                var before = SourceStatus(token); string fingerprint = DataFormat.Str(DataFormat.Obj(before["package"]), "sha256");
                store.InitializeLibrary(); // Include profiles published by another local Luna process.
                var targets = new List<string> { "1-5", requested };
                targets.AddRange(store.AvailableProfiles);
                if (all) for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) targets.Add(t + "-" + s);
                targets = targets.Distinct().ToList();
                var attempted = new HashSet<string>();
                PriorityPump = ct => { ServiceHeroRequests(ct); RefreshPriorityPairs(before, targets, attempted, 1, ct); };
                ServiceHeroRequests(token);
                // Publish the user's two directions before collecting the full lane catalog.
                RefreshPriorityPairs(before, targets, attempted, 2, token);
                var pending = targets.Where(p => store.NeedsUpdate(p, fingerprint, automatic)).ToList();
                if (pending.Count == 0) { Progress("检查完成：数据版本未变化，未下载新数据。本机更新时间保持不变。", "complete"); return; }
                Progress("正在核对五个分路的完整英雄目录…", "checking");
                var catalog = ResumeCatalog(before, token);
                int total = pending.Sum(p => DataFormat.ExpectedKeys(catalog, p).Count);
                lock (JobSync) UpdateTotal = total;
                foreach (string profile in pending) {
                    token.ThrowIfCancellationRequested();
                    lock (JobSync) UpdateProfile = profile;
                    string directory = Path.Combine(options.LibraryDir, fingerprint, profile); Directory.CreateDirectory(directory);
                    var entries = new Dictionary<string, object>(); var entriesLock = new object();
                    var keys = DataFormat.ExpectedKeys(catalog, profile);
                    Progress("正在保存完整离线库，可继续查询。中断后再次更新可续传。", "downloading");
                    Parallel.ForEach(keys, new ParallelOptions { MaxDegreeOfParallelism = automatic ? 1 : 2, CancellationToken = token }, key => {
                        if (PriorityPump != null) { if (automatic) PriorityPump(token); else ServiceHeroRequests(token); Progress("正在继续补齐完整离线库；已完成的英雄更新可以先用。", "downloading"); }
                        string path = Path.Combine(directory, key + ".json.gz"); bool valid = false;
                        try { valid = File.Exists(path) && DataFormat.ValidRecord(DataFormat.ReadCompressed(path), key, fingerprint); } catch { }
                        if (!valid) try {
                            var pair = store.ReadFocusedPair(DataFormat.FocusPair(key));
                            if (pair != null && DataFormat.Str(pair, "fingerprint") == fingerprint) {
                                DataFormat.WriteCompressed(path, DataFormat.Obj(pair["records"])[key]); valid = true;
                            }
                        } catch { }
                        if (!valid) {
                            var parts = key.Split('-');
                            var data = FetchLibraryJson(DataFormat.Source + "/api/counter/detail?championId=" + parts[1] + "&lane=" + parts[2] + "&mode=" + parts[3] + "&thresholdLevel=" + parts[4] + "&sensitivity=" + parts[5], token);
                            if (!DataFormat.ValidDetail(data, key)) throw new IOException("原站返回了不匹配的数据：" + key);
                            token.ThrowIfCancellationRequested(); DataFormat.WriteCompressed(path, new { key = key, fingerprint = fingerprint, fetchedAt = DataFormat.Stamp(), data = data });
                        }
                        string hash = DataFormat.RecordFileHash(path); lock (entriesLock) entries[key] = hash;
                        lock (JobSync) UpdateDone++;
                    });
                    token.ThrowIfCancellationRequested(); Progress("正在验证完整性与原站版本…", "verifying");
                    var after = SourceStatus(token);
                    if (DataFormat.Str(DataFormat.Obj(after["package"]), "sha256") != fingerprint) throw new IOException("下载期间原站版本发生变化。旧库仍可使用，请再次更新以获取新版本。");
                    if (entries.Count != keys.Count) throw new IOException("离线数据尚不完整，不替换已有版本");
                    var manifest = new { schema = 1, source = DataFormat.Source + "/counter", profile = profile, fingerprint = fingerprint, sourceGeneratedAt = DataFormat.Str(DataFormat.Obj(before["meta"]), "generatedAt"), builtAt = DataFormat.Stamp(), expected = keys.Count, entries = entries, catalog = catalog };
                    DataFormat.WriteRecord(Path.Combine(directory, "manifest.json"), manifest);
                    store.PublishProfile(profile, fingerprint, token);
                }
                Progress("离线库已更新并保存。查询继续使用本地数据，无需升级程序。", "complete");
            } catch (Exception ex) {
                if (token.IsCancellationRequested) Progress("更新已停止；完整旧库仍可用。下次更新会继续已下载的部分。", "cancelled");
                else { diagnostics.Log(ex); Progress((automatic ? "自动更新未完成，原有离线库仍可查询。" : "更新未完成，原有离线库仍可查询。") + ex.GetBaseException().Message + " 可手动重试，或下次正常启动时自动检查并续传。", "error"); }
            } finally {
                PriorityPump = null;
                if (acquired) { try { maintenance.AfterUpdate(UpdateDone, workers.Token); } finally { mutex.ReleaseMutex(); } }
                if (token.IsCancellationRequested) CancelQueuedHeroes();
                FinishUpdateWorker();
            }
        }
        int FocusUpdated;
        Dictionary<string, object> FetchFocusRecord(string key, string fingerprint, CancellationToken token, bool fresh) {
            string directory = Path.Combine(options.LibraryDir, fingerprint, DataFormat.ProfileOf(key)); if (!fresh) Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, key + ".json.gz");
            if (!fresh) try { var saved = DataFormat.ReadCompressed(path); if (DataFormat.ValidRecord(saved, key, fingerprint)) return saved; } catch { }
            var p = key.Split('-');
            var data = FetchLibraryJson(DataFormat.Source + "/api/counter/detail?championId=" + p[1] + "&lane=" + p[2] + "&mode=" + p[3] + "&thresholdLevel=" + p[4] + "&sensitivity=" + p[5], token);
            if (!DataFormat.ValidDetail(data, key)) throw new IOException("原站返回不匹配的英雄更新记录");
            token.ThrowIfCancellationRequested();
            var row = new Dictionary<string, object> { { "key", key }, { "fingerprint", fingerprint }, { "fetchedAt", DataFormat.Stamp() }, { "data", data } };
            // Published full-profile files are immutable. Manual refreshes are
            // saved only as an atomic pair, never over an active manifest entry.
            if (!fresh) DataFormat.WriteCompressed(path, row); return row;
        }
        internal void RefreshPriorityPairs(Dictionary<string, object> before, IEnumerable<string> targets, HashSet<string> attempted, int limit, CancellationToken token) {
            string fingerprint = DataFormat.Str(DataFormat.Obj(before["package"]), "sha256");
            for (int count = 0; count < limit; count++) {
                string pair = store.RecentFocus().FirstOrDefault(p => targets.Contains(DataFormat.FocusProfile(p)) && !attempted.Contains(p));
                if (pair == null) return;
                attempted.Add(pair); if (store.AlreadyCurrent(pair, fingerprint)) continue;
                var pieces = pair.Split('-');
                var heroes = DataFormat.Arr(DataFormat.Get(DataFormat.Obj(store.LocalCatalog()["data"]), "heroes")).Select(DataFormat.Obj);
                if (!heroes.Any(h => DataFormat.Str(h, "id") == pieces[1] && DataFormat.Obj(DataFormat.Get(h, "lanes")).ContainsKey(pieces[2]))) continue;
                Progress("优先更新正在查询和最近使用的英雄；原来的本地结果仍可查看。", "focused");
                SaveFocusedPair(pair, before, token, false);
            }
        }
        void SaveFocusedPair(string pair, Dictionary<string, object> before, CancellationToken token, bool fresh) {
            string fingerprint = DataFormat.Str(DataFormat.Obj(before["package"]), "sha256");
            var rows = new Dictionary<string, object>(); var sync = new object();
            Parallel.ForEach(DataFormat.FocusKeys(pair), new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, key => {
                var row = FetchFocusRecord(key, fingerprint, token, fresh); lock (sync) rows[key] = row;
            });
            token.ThrowIfCancellationRequested();
            var after = SourceStatus(token);
            if (DataFormat.Str(DataFormat.Obj(after["package"]), "sha256") != fingerprint) throw new IOException("更新期间原站换版，未启用混合版本；已保存的可用结果保持不变。");
            store.PublishFocusedPair(pair, before, rows, fresh, token);
            Interlocked.Increment(ref FocusUpdated);
        }
        internal Dictionary<string, object> ResumeCatalog(Dictionary<string, object> status, CancellationToken token) {
            string fingerprint = DataFormat.Str(DataFormat.Obj(status["package"]), "sha256"), path = Path.Combine(options.LibraryDir, "catalog-cache.json");
            var saved = DataFormat.ReadRecord(path);
            try {
                string payload = DataFormat.Str(saved, "payload");
                if (DataFormat.Str(saved, "fingerprint") == fingerprint && DataFormat.Hash(payload) == DataFormat.Str(saved, "sha256")) {
                    var catalog = DataFormat.Obj(DataFormat.Json().DeserializeObject(payload)); DataFormat.ExpectedKeys(catalog, "1-5");
                    var heroes = DataFormat.Arr(DataFormat.Get(DataFormat.Obj(catalog["data"]), "heroes")).Select(DataFormat.Obj).ToArray();
                    if (DataFormat.Arr(status["lanes"]).Select(DataFormat.Obj).All(l => heroes.Count(h => DataFormat.Obj(DataFormat.Get(h, "lanes")).ContainsKey(DataFormat.Str(l, "key"))).ToString() == DataFormat.Str(l, "championCount"))) return catalog;
                }
            } catch { }
            var built = BuildLibraryCatalog(status, token); string text = DataFormat.Json().Serialize(built);
            DataFormat.WriteRecord(path, new { fingerprint = fingerprint, payload = text, sha256 = DataFormat.Hash(text) }); return built;
        }
        sealed class HeroUpdateJob {
            public string Pair, Key, State = "queued", Message = "已加入优先更新，原有结果仍可查询。", CheckedAt = "", SavedAt = "";
            public long Order;
        }
        readonly Dictionary<string, HeroUpdateJob> HeroJobs = new Dictionary<string, HeroUpdateJob>();
        readonly Queue<string> HeroQueue = new Queue<string>();
        long HeroOrder;
        int HeroDrainActive;
        string UpdateKind = "library";
        const int HeroJobLimit = 24;
        string CheckedHeroPair(string key) {
            if (!DataFormat.ValidKey(key)) throw new ArgumentException("英雄更新条件无效");
            var p = key.Split('-');
            if (!DataFormat.Arr(DataFormat.Get(DataFormat.Obj(store.LocalCatalog()["data"]), "heroes")).Select(DataFormat.Obj).Any(h => DataFormat.Str(h, "id") == p[1] && DataFormat.Obj(DataFormat.Get(h, "lanes")).ContainsKey(p[2])))
                throw new ArgumentException("当前英雄没有这个分路的数据");
            return DataFormat.FocusPair(key);
        }
        internal object HeroUpdateInfo(string key) {
            string pair = CheckedHeroPair(key);
            lock (JobSync) {
                HeroUpdateJob job; HeroJobs.TryGetValue(pair, out job);
                return new { success = true, key = key, pair = pair, busy = job != null && (job.State == "queued" || job.State == "running"),
                    state = job == null ? "idle" : job.State, message = job == null ? "只更新当前分路、当前筛选条件的严父和大儿。" : job.Message,
                    checkedAt = job == null ? "" : job.CheckedAt, savedAt = job == null ? "" : job.SavedAt };
            }
        }
        internal object StartHeroUpdate(string key) {
            if (workers.IsCancellationRequested) throw new OperationCanceledException("Luna is stopping", workers.Token);
            if (options.Offline) throw new ArgumentException("离线模式不能联网更新，请从普通入口启动。");
            string pair = CheckedHeroPair(key);
            store.RegisterFocus(key);
            lock (JobSync) {
                HeroUpdateJob prior;
                if (workers.IsCancellationRequested) throw new OperationCanceledException("Luna is stopping", workers.Token);
                if (HeroJobs.TryGetValue(pair, out prior) && (prior.State == "queued" || prior.State == "running")) return HeroUpdateInfo(key);
                while (HeroJobs.Count >= HeroJobLimit) {
                    var oldest = HeroJobs.Values.Where(j => j.State != "queued" && j.State != "running").OrderBy(j => j.Order).FirstOrDefault();
                    if (oldest == null) throw new ArgumentException("已有多项英雄更新，请等待当前任务完成。");
                    HeroJobs.Remove(oldest.Pair);
                }
                HeroJobs[pair] = new HeroUpdateJob { Pair = pair, Key = key, Order = ++HeroOrder };
                HeroQueue.Enqueue(pair);
                if (!UpdateBusy) BeginHeroWorker();
                return HeroUpdateInfo(key);
            }
        }
        void BeginHeroWorker() {
            if (workers.IsCancellationRequested) { CancelQueuedHeroes(); return; }
            if (UpdateBusy || HeroQueue.Count == 0) return;
            if (UpdateCancellation != null) UpdateCancellation.Dispose();
            UpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(workers.Token);
            UpdateBusy = true; UpdateAutomatic = false; UpdateKind = "hero";
            UpdateState = "checking"; UpdateMessage = "正在联网更新英雄，查询仍读取已有本地数据。";
            UpdateDone = 0; UpdateTotal = 0; FocusUpdated = 0;
            var cancellation = UpdateCancellation;
            if (!workers.TryStart(() => RunHeroWorker(cancellation.Token), "Luna hero update")) {
                UpdateBusy = false; cancellation.Dispose(); UpdateCancellation = null; CancelQueuedHeroes();
            }
        }
        void CancelQueuedHeroes() {
            lock (JobSync) {
                while (HeroQueue.Count > 0) {
                    string pair = HeroQueue.Dequeue(); HeroUpdateJob job;
                    if (HeroJobs.TryGetValue(pair, out job) && job.State == "queued") {
                        job.State = "cancelled"; job.Message = "更新已停止，原有数据仍可使用。";
                    }
                }
            }
        }
        void FinishUpdateWorker() {
            lock (JobSync) {
                UpdateBusy = false; LastUpdate = DataFormat.Stamp();
                if (UpdateCancellation != null) { UpdateCancellation.Dispose(); UpdateCancellation = null; }
                // An explicit click arriving as a whole-library job finishes is not lost.
                if (!workers.IsCancellationRequested && HeroQueue.Count > 0) BeginHeroWorker();
            }
        }
        void RunHeroWorker(CancellationToken token) {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\LunaData_" + DataFormat.Hash(options.Root).Substring(0, 20))) try {
                try { acquired = WaitHandle.WaitAny(new WaitHandle[] { mutex, token.WaitHandle }, 30000) == 0; }
                catch (AbandonedMutexException) { acquired = true; }
                token.ThrowIfCancellationRequested();
                if (!acquired) throw new IOException("其他 Luna 窗口正在更新，请稍后再试。");
                while (true) {
                    token.ThrowIfCancellationRequested(); ServiceHeroRequests(token);
                    lock (JobSync) { if (HeroQueue.Count == 0) break; }
                }
                lock (JobSync) {
                    var last = HeroJobs.Values.OrderByDescending(j => j.Order).FirstOrDefault();
                    UpdateState = last != null && last.State == "error" ? "error" : "complete";
                    UpdateMessage = last == null ? "英雄更新已结束。" : last.Message;
                }
            } catch (Exception ex) {
                lock (JobSync) {
                    foreach (var job in HeroJobs.Values.Where(j => j.State == "queued" || j.State == "running")) {
                        job.State = token.IsCancellationRequested ? "cancelled" : "error";
                        job.Message = token.IsCancellationRequested ? "更新已停止，原有数据仍可使用。" : "英雄更新未完成，原有数据仍可使用。" + ex.GetBaseException().Message;
                    }
                    HeroQueue.Clear();
                }
                Progress(token.IsCancellationRequested ? "更新已停止，原有数据仍可使用。" : "英雄更新未完成，原有数据仍可使用。" + ex.GetBaseException().Message, token.IsCancellationRequested ? "cancelled" : "error");
            } finally {
                if (acquired) { try { maintenance.AfterUpdate(1, workers.Token); } finally { mutex.ReleaseMutex(); } }
                FinishUpdateWorker();
            }
        }
        void ServiceHeroRequests(CancellationToken token) {
            if (Interlocked.CompareExchange(ref HeroDrainActive, 1, 0) != 0) return;
            try {
                for (int n = 0; n < HeroJobLimit; n++) {
                    HeroUpdateJob job;
                    lock (JobSync) {
                        if (HeroQueue.Count == 0) return;
                        string pair = HeroQueue.Dequeue();
                        if (!HeroJobs.TryGetValue(pair, out job) || job.State != "queued") continue;
                        job.State = "running"; job.Message = "正在联网获取当前英雄的严父和大儿，已有结果仍可查看。";
                    }
                    try {
                        token.ThrowIfCancellationRequested();
                        Progress("正在优先处理单英雄更新；本地查询不受影响。", "hero");
                        var before = SourceStatus(token);
                        lock (JobSync) job.CheckedAt = DataFormat.Stamp();
                        // An explicit update fetches two fresh responses even when the
                        // bundled edition already matches. It never rewrites committed files.
                        SaveFocusedPair(job.Pair, before, token, true);
                        var saved = store.ReadFocusedPair(job.Pair);
                        lock (JobSync) {
                            job.SavedAt = DataFormat.Str(saved, "savedAt"); job.State = "complete";
                            job.Message = "此英雄的严父与大儿已联网更新并保存，当前分路和筛选条件下可离线使用。";
                        }
                    } catch (Exception ex) {
                        lock (JobSync) {
                            job.State = token.IsCancellationRequested ? "cancelled" : "error";
                            job.Message = token.IsCancellationRequested ? "更新已停止，原有数据仍可使用。" : "此英雄更新未完成，继续使用原有数据。" + ex.GetBaseException().Message;
                        }
                        if (token.IsCancellationRequested) { CancelQueuedHeroes(); throw; }
                        diagnostics.Log(ex);
                    }
                }
            } finally { Interlocked.Exchange(ref HeroDrainActive, 0); }
        }

        internal bool AutomaticUpdates { get { return AutomaticUpdatesEnabled; } }
        internal object Status { get { lock (JobSync) return new { busy = UpdateBusy, automatic = UpdateAutomatic, kind = UpdateKind, state = UpdateState, message = UpdateMessage, done = UpdateDone, total = UpdateTotal, profile = UpdateProfile, checkedAt = LastUpdate, focusedUpdated = FocusUpdated }; } }
    }
}

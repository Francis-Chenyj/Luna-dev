using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        sealed class HeroUpdateJob {
            public string Pair, Key, State = "queued", Message = "已加入优先更新，原有结果仍可查询。", CheckedAt = "", SavedAt = "";
            public long Order;
        }
        static readonly Dictionary<string, HeroUpdateJob> HeroJobs = new Dictionary<string, HeroUpdateJob>();
        static readonly Queue<string> HeroQueue = new Queue<string>();
        static long HeroOrder;
        static int HeroDrainActive;
        static string UpdateKind = "library";
        const int HeroJobLimit = 24;

        static string CheckedHeroPair(string key) {
            if (!ValidKey(key)) throw new ArgumentException("英雄更新条件无效");
            var p = key.Split('-');
            if (!Arr(Get(Obj(LocalCatalog()["data"]), "heroes")).Select(Obj).Any(h => Str(h, "id") == p[1] && Obj(Get(h, "lanes")).ContainsKey(p[2])))
                throw new ArgumentException("当前英雄没有这个分路的数据");
            return FocusPair(key);
        }
        static object HeroUpdateInfo(string key) {
            string pair = CheckedHeroPair(key);
            lock (JobSync) {
                HeroUpdateJob job; HeroJobs.TryGetValue(pair, out job);
                return new { success = true, key = key, pair = pair, busy = job != null && (job.State == "queued" || job.State == "running"),
                    state = job == null ? "idle" : job.State, message = job == null ? "只更新当前分路、当前筛选条件的严父和大儿。" : job.Message,
                    checkedAt = job == null ? "" : job.CheckedAt, savedAt = job == null ? "" : job.SavedAt };
            }
        }
        static object StartHeroUpdate(string key) {
            if (Offline) throw new ArgumentException("离线模式不能联网更新，请从普通入口启动。");
            string pair = CheckedHeroPair(key);
            RegisterFocus(key);
            lock (JobSync) {
                HeroUpdateJob prior;
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
        // Called while holding JobSync. The single-hero worker does not schedule a full library.
        static void BeginHeroWorker() {
            if (Shutdown.IsCancellationRequested || UpdateBusy || HeroQueue.Count == 0) return;
            if (UpdateCancellation != null) UpdateCancellation.Dispose();
            UpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
            UpdateBusy = true; UpdateAutomatic = false; UpdateKind = "hero";
            UpdateState = "checking"; UpdateMessage = "正在联网更新英雄，查询仍读取已有本地数据。";
            UpdateDone = 0; UpdateTotal = 0; FocusUpdated = 0;
            var cancellation = UpdateCancellation;
            var worker = new Thread(() => RunHeroWorker(cancellation.Token));
            worker.IsBackground = true; worker.Priority = ThreadPriority.BelowNormal; worker.Start();
        }
        static void CancelQueuedHeroes() {
            lock (JobSync) {
                while (HeroQueue.Count > 0) {
                    string pair = HeroQueue.Dequeue(); HeroUpdateJob job;
                    if (HeroJobs.TryGetValue(pair, out job) && job.State == "queued") {
                        job.State = "cancelled"; job.Message = "更新已停止，原有数据仍可使用。";
                    }
                }
            }
        }
        static void FinishUpdateWorker() {
            lock (JobSync) {
                UpdateBusy = false; LastUpdate = Stamp();
                // An explicit click arriving as a whole-library job finishes is not lost.
                if (!Shutdown.IsCancellationRequested && HeroQueue.Count > 0) BeginHeroWorker();
            }
        }
        static void RunHeroWorker(CancellationToken token) {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\LunaData_" + Hash(Root).Substring(0, 20))) try {
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
                if (acquired) { MaintainStorage("", Shutdown.Token); mutex.ReleaseMutex(); }
                FinishUpdateWorker();
            }
        }
        static void ServiceHeroRequests(CancellationToken token) {
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
                        lock (JobSync) job.CheckedAt = Stamp();
                        // An explicit update fetches two fresh responses even when the
                        // bundled edition already matches. It never rewrites committed files.
                        SaveFocusedPair(job.Pair, before, token, true);
                        var saved = ReadFocusedPair(job.Pair);
                        lock (JobSync) {
                            job.SavedAt = Str(saved, "savedAt"); job.State = "complete";
                            job.Message = "此英雄的严父与大儿已联网更新并保存，当前分路和筛选条件下可离线使用。";
                        }
                    } catch (Exception ex) {
                        lock (JobSync) {
                            job.State = token.IsCancellationRequested ? "cancelled" : "error";
                            job.Message = token.IsCancellationRequested ? "更新已停止，原有数据仍可使用。" : "此英雄更新未完成，继续使用原有数据。" + ex.GetBaseException().Message;
                        }
                        if (token.IsCancellationRequested) { CancelQueuedHeroes(); throw; }
                        Log(ex);
                    }
                }
            } finally { Interlocked.Exchange(ref HeroDrainActive, 0); }
        }
    }
}

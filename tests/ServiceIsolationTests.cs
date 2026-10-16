using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Luna;

// All scenarios use real service instances and injected upstream functions.
// No reflection, Program singleton, personal cache or external connection is used.
internal static class ServiceIsolationTests {
    const string Key = "detail-266-top-father-1-5";
    const string Son = "detail-266-top-son-1-5";
    static int failures;
    static Dictionary<string, object> Map(object value) { return DataFormat.Obj(DataFormat.Json().DeserializeObject(DataFormat.Json().Serialize(value))); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Value(ApplicationRuntime app, string key = Key) { return DataFormat.Str(DataFormat.Obj(app.Queries.LocalDetail(key)["data"]), "testValue"); }
    static bool Busy(ApplicationRuntime app) { return (bool)Map(app.Updates.Status)["busy"]; }
    static void Idle(ApplicationRuntime app) { Check(app.Workers.WaitForIdle(TimeSpan.FromSeconds(15)), "Workers failed to drain"); Check(!Busy(app), "Data worker left busy state"); }
    static void Until(Func<bool> condition, string message) { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < 5000) Thread.Sleep(10); Check(condition(), message); }
    static string NewRoot() { string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    static Dictionary<string, object> Detail(string key, string value) {
        var p = key.Split('-'); return Map(new { success = true, champion = new { championId = int.Parse(p[1]) }, lane = p[2], mode = p[3], sections = new object[0], testValue = value });
    }
    static void Baseline(string root, string value) {
        string dir = Path.Combine(root, "data/library/1-5"); Directory.CreateDirectory(dir); Directory.CreateDirectory(Path.Combine(root, "cache"));
        var catalog = Map(new { data = new { heroes = new[] { new { id = 266, name = value, title = "Aatrox", en = "Aatrox", keywords = "", lanes = DataFormat.Lanes.ToDictionary(l => l, l => 1) } },
            lanes = DataFormat.Lanes.Select(l => new { key = l, label = l }).ToArray(), version = "test", updatedAt = "2026-09-01T00:00:00Z" }, fetchedAt = "2026-09-01T00:00:00Z" });
        var entries = new Dictionary<string, object>(); string fingerprint = new string('a', 64);
        foreach (string key in DataFormat.ExpectedKeys(catalog, "1-5")) {
            string file = Path.Combine(dir, key + ".json.gz");
            DataFormat.WriteCompressed(file, new { key = key, fingerprint = fingerprint, fetchedAt = "2026-09-01T00:00:00Z", data = Detail(key, value) });
            entries[key] = DataFormat.RecordFileHash(file);
        }
        DataFormat.WriteRecord(Path.Combine(dir, "manifest.json"), new { schema = 1, profile = "1-5", fingerprint = fingerprint,
            sourceGeneratedAt = "2026-09-01T00:00:00Z", builtAt = "2026-09-01T00:00:00Z", expected = entries.Count, entries = entries, catalog = catalog });
    }
    sealed class Source : IDisposable {
        internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim(), BeforePublish = new ManualResetEventSlim();
        internal bool Block, BlockPublication;
        internal int Calls, StatusCalls;
        internal string Value = "updated";
        internal Action RequestObserved;
        internal Dictionary<string, object> Fetch(string url, CancellationToken token) {
            Interlocked.Increment(ref Calls); token.ThrowIfCancellationRequested();
            if (RequestObserved != null) RequestObserved();
            if (url.EndsWith("/status")) {
                if (Interlocked.Increment(ref StatusCalls) == 2 && BlockPublication) { BeforePublish.Set(); Release.Wait(token); }
                return Map(new { success = true, ready = true, package = new { sha256 = new string('b', 64) }, meta = new { generatedAt = "2026-10-16T00:00:00Z" }, lanes = DataFormat.Lanes.Select(l => new { key = l, championCount = 1 }).ToArray() });
            }
            if (url.Contains("/detail?")) {
                Entered.Set(); if (Block) Release.Wait(token);
                var q = HttpUtility.ParseQueryString(new Uri(url).Query);
                return Detail("detail-266-" + q["lane"] + "-" + q["mode"] + "-" + q["thresholdLevel"] + "-" + q["sensitivity"], Value);
            }
            throw new IOException("Unexpected fixture endpoint: " + new Uri(url).AbsolutePath);
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); BeforePublish.Dispose(); }
    }
    static ApplicationRuntime Create(string root, Source source, bool offline = false) {
        var app = new ApplicationRuntime(new RuntimeOptions(root, offline, true), source.Fetch, (url, token) => { throw new IOException("No release feed in this fixture"); });
        app.Library.InitializeLibrary(); return app;
    }
    static void SnapshotOwnership() {
        string first = NewRoot(), second = NewRoot(); Baseline(first, "first"); Baseline(second, "second");
        using (var source = new Source()) using (var a = Create(first, source)) using (var b = Create(second, source)) {
            Check(Value(a) == "first" && Value(b) == "second", "Repositories share their indexes");
            var catalog = a.Library.LocalCatalog();
            var heroes = DataFormat.Arr(DataFormat.Obj(catalog["data"])["heroes"]).Select(DataFormat.Obj).ToArray(); heroes[0]["name"] = "changed by caller";
            var fresh = DataFormat.Arr(DataFormat.Obj(a.Library.LocalCatalog()["data"])["heroes"]).Select(DataFormat.Obj).First();
            Check(DataFormat.Str(fresh, "name") == "first", "Returned catalog exposes the live repository index");
            var summary = Map(a.LibraryInfo("1-5")); summary["source"] = "changed";
            Check(DataFormat.Str(Map(a.LibraryInfo("1-5")), "source") == "bundled", "Status exposes mutable repository state");
            Check(source.Calls == 0, "Local queries made upstream requests");
        }
    }
    static void IndependentJobs() {
        string first = NewRoot(), second = NewRoot(); Baseline(first, "first"); Baseline(second, "second");
        using (var x = new Source { Block = true }) using (var y = new Source { Value = "second-new" })
        using (var a = Create(first, x)) using (var b = Create(second, y)) {
            a.Updates.StartHeroUpdate(Key); Check(x.Entered.Wait(5000), "First update did not block");
            Check(!Busy(b) && Value(a) == "first", "Busy state leaked or query waited for network");
            b.Updates.StartHeroUpdate(Key); Idle(b);
            Check(Value(b) == "second-new" && Busy(a), "Unrelated instance could not publish independently");
            a.Updates.CancelLibraryUpdate(); Idle(a);
            Check(Value(a) == "first" && Value(b) == "second-new", "Cancellation affected the other instance or replaced old data");
            a.Dispose(); b.Updates.StartHeroUpdate("detail-266-jungle-father-1-5"); Idle(b);
            Check(Value(b, "detail-266-jungle-father-1-5") == "second-new", "Disposing one runtime stopped another runtime");
        }
    }
    static void SameDirectoryCoordination() {
        string root = NewRoot(); Baseline(root, "old");
        using (var x = new Source { Block = true }) using (var y = new Source { Value = "new" })
        using (var a = Create(root, x)) using (var b = Create(root, y)) {
            a.Updates.StartHeroUpdate(Key); Check(x.Entered.Wait(5000), "First writer did not acquire directory ownership");
            b.Updates.StartHeroUpdate(Key); Thread.Sleep(120);
            Check(y.Calls == 0 && Busy(b) && Value(b) == "old", "Directory mutex was lost or query blocked behind it");
            a.Updates.CancelLibraryUpdate(); Idle(a); Idle(b);
            Check(Value(a) == "new" && Value(b) == "new", "Writer handoff did not publish a complete shared result");
        }
    }
    static void PublishWithMaintenance() {
        string root = NewRoot(); Baseline(root, "old");
        using (var source = new Source { BlockPublication = true, Value = "new" }) using (var app = Create(root, source)) {
            app.Updates.StartHeroUpdate(Key); Check(source.BeforePublish.Wait(6000), "Update did not reach pre-publication barrier");
            app.Storage.ScheduleStorageMaintenance(); Until(() => (bool)Map(app.Storage.SpaceInfo())["waiting"], "Cleanup did not respect the writer lock");
            string personal = Path.Combine(root, "cache", "personal.txt"); File.WriteAllText(personal, "keep");
            int reads = 0; var errors = new List<string>();
            var reader = Task.Run(() => {
                do { var value = Value(app); if (value != "old" && value != "new") errors.Add(value); Interlocked.Increment(ref reads); } while (Busy(app));
            });
            source.Release.Set(); Idle(app); Check(reader.Wait(5000), "Query reader stalled");
            Check(reads > 0 && errors.Count == 0 && Value(app) == "new" && Value(app, Son) == "new", "Readers saw partial publication");
            Check(File.ReadAllText(personal) == "keep", "Maintenance deleted unrelated data");
        }
        using (var source = new Source()) using (var reopened = Create(root, source, true)) {
            Check(Value(reopened) == "new" && Value(reopened, Son) == "new" && source.Calls == 0, "Offline restart lost the published pair");
        }
    }
    static void StopRejectsNewJobs() {
        string root = NewRoot(); Baseline(root, "old");
        using (var source = new Source()) using (var app = Create(root, source)) {
            app.Workers.Stop(); bool rejected = false;
            try { app.Updates.StartHeroUpdate(Key); } catch (OperationCanceledException) { rejected = true; }
            var job = Map(app.Updates.HeroUpdateInfo(Key));
            Check(rejected && !(bool)job["busy"] && source.Calls == 0, "Shutdown accepted a permanently queued hero job");
            Check(!app.Workers.TryStart(() => { throw new Exception("Late worker started"); }, "late"), "Shutdown accepted new background work");
        }
    }
    static void DrainAllOwnedWorkers() {
        string root = NewRoot(); Baseline(root, "old");
        using (var source = new Source { Block = true }) using (var entered = new ManualResetEventSlim()) {
            using (var app = new ApplicationRuntime(new RuntimeOptions(root), source.Fetch, (url, token) => { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return "[]"; })) {
                app.Library.InitializeLibrary(); app.Updates.StartHeroUpdate(Key); app.AppUpdates.StartAppUpdate("check");
                Check(source.Entered.Wait(5000) && entered.Wait(5000), "Both independent updater workers must start");
                app.Workers.Stop(); Idle(app);
                Check(!(bool)Map(app.AppUpdates.AppUpdateInfo())["busy"], "Application updater was not drained");
                Check(Value(app) == "old", "Stopping unfinished updates changed usable local data");
            }
        }
    }
    static void WorkerRegistrationRace() {
        int cleanups = 0; var errors = new List<Exception>();
        using (var workers = new BackgroundWork(ex => { lock (errors) errors.Add(ex); })) {
            workers.WhenDrained(() => Interlocked.Increment(ref cleanups));
            Parallel.For(0, 12, i => workers.TryStart(() => { workers.Token.WaitHandle.WaitOne(); workers.Token.ThrowIfCancellationRequested(); }, "isolation race"));
            workers.Stop(); Check(workers.WaitForIdle(TimeSpan.FromSeconds(3)), "Registered workers did not leave");
            Check(cleanups == 1 && errors.Count == 0, "Lifetime cleanup ran twice or raced a live worker");
        }
        Check(cleanups == 1, "Dispose repeated lifecycle cleanup");
    }
    static void UpdateCacheOwnership() {
        string root = NewRoot(); Baseline(root, "old");
        string cache = Path.Combine(root, "cache/app-update"); Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, "package.zip"); File.WriteAllText(archive, "old download"); File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddDays(-8));
        using (var entered = new ManualResetEventSlim())
        using (var a = new ApplicationRuntime(new RuntimeOptions(root), null, (url, token) => { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return "[]"; }))
        using (var b = new ApplicationRuntime(new RuntimeOptions(root))) {
            a.AppUpdates.StartAppUpdate("check"); Check(entered.Wait(5000), "Updater did not acquire its directory");
            b.AppUpdates.MaintainAppDownloads();
            Check(File.Exists(archive), "Another instance cleaned an active updater's download");
            a.AppUpdates.CancelAppUpdate(); Idle(a);
            b.AppUpdates.MaintainAppDownloads();
            Check(!File.Exists(archive), "Expired download was not cleaned after ownership was released");
        }
    }
    static void ProductionRequestPacing() {
        string root = NewRoot(); Baseline(root, "old");
        var times = new List<long>(); var watch = Stopwatch.StartNew();
        using (var source = new Source { RequestObserved = () => times.Add(watch.ElapsedMilliseconds) })
        using (var app = Create(root, source)) {
            // Deliberately omit a test clock: this checks the production default.
            app.Updates.StartHeroUpdate(Key); Idle(app);
            Check(Value(app) == "updated" && times.Count == 4, "Real-clock refresh did not publish one complete pair");
            for (int i = 1; i < times.Count; i++)
                Check(times[i] - times[i - 1] >= 500, "Production request spacing fell below the 600 ms policy (100 ms tolerance)");
        }
        using (var entered = new ManualResetEventSlim()) using (var cancellation = new CancellationTokenSource()) {
            bool canceled = false; var clock = new SystemRuntimeClock();
            var waiting = Task.Run(() => {
                entered.Set();
                try { clock.WaitUntil(clock.UtcNow.AddMinutes(1), cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
            });
            Check(entered.Wait(3000), "Clock waiter did not start"); cancellation.Cancel();
            Check(waiting.Wait(3000) && canceled, "Production clock did not interrupt its wait on cancellation");
        }
    }
    sealed class PausedClock : IRuntimeClock, IDisposable {
        readonly DateTime now = DateTime.UtcNow;
        internal readonly ManualResetEventSlim Waiting = new ManualResetEventSlim();
        public DateTime UtcNow { get { return now; } }
        public void WaitUntil(DateTime due, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if (due > now) { Waiting.Set(); token.WaitHandle.WaitOne(); }
            token.ThrowIfCancellationRequested();
        }
        public void Dispose() { Waiting.Dispose(); }
    }
    static void CancellationDuringRateWait() {
        string root = NewRoot(); Baseline(root, "old");
        using (var clock = new PausedClock()) using (var source = new Source())
        using (var app = new ApplicationRuntime(new RuntimeOptions(root), source.Fetch, clock: clock)) {
            app.Library.InitializeLibrary(); app.Updates.StartHeroUpdate(Key);
            Check(clock.Waiting.Wait(3000), "Refresh never waited for its next request slot");
            app.Updates.CancelLibraryUpdate(); Idle(app);
            Check(source.Calls == 1 && Value(app) == "old", "Cancellation issued a queued request or replaced local data");
            Check(DataFormat.Str(Map(app.Updates.HeroUpdateInfo(Key)), "state") == "cancelled", "Rate wait left its job active");
        }
    }
    static void Run(string name, Action test, string selected) {
        if (selected != "" && selected != name) return;
        try { test(); Console.WriteLine("PASS: " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL: " + name + "\n" + ex); }
    }
    static int Main(string[] args) {
        string selected = args.Length == 0 ? "" : args[0];
        Run("snapshot-ownership", SnapshotOwnership, selected);
        Run("independent-jobs-and-cancellation", IndependentJobs, selected);
        Run("same-directory-writer-handoff", SameDirectoryCoordination, selected);
        Run("publish-query-maintenance-and-restart", PublishWithMaintenance, selected);
        Run("shutdown-rejects-new-jobs", StopRejectsNewJobs, selected);
        Run("drain-both-updaters", DrainAllOwnedWorkers, selected);
        Run("worker-registration-and-disposal", WorkerRegistrationRace, selected);
        Run("updater-cache-ownership", UpdateCacheOwnership, selected);
        Run("production-request-pacing-and-clock-cancellation", ProductionRequestPacing, selected);
        Run("cancellation-during-rate-wait", CancellationDuringRateWait, selected);
        return failures == 0 ? 0 : 1;
    }
}

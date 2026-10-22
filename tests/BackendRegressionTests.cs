using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using Luna;

// Faults are created in an isolated fixture directory. No private-state access,
// installed app, external connection or personal cache is used by these tests.
internal static class BackendRegressionTests {
    const string Key = "detail-266-top-father-1-5";
    static int failures;
    static Dictionary<string, object> Map(object value) { return DataFormat.Obj(DataFormat.Json().DeserializeObject(DataFormat.Json().Serialize(value))); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Until(Func<bool> condition, string message) { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < 5000) Thread.Sleep(10); Check(condition(), message); }
    static void Idle(ApplicationRuntime app) { Check(app.Workers.WaitForIdle(TimeSpan.FromSeconds(15)), "Fixture workers did not drain"); Check(!(bool)Map(app.Updates.Status)["busy"], "Finished update retained its busy state"); }
    static string NewRoot() { string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    static Dictionary<string, object> Detail(string key) {
        var parts = key.Split('-');
        return Map(new { success = true, champion = new { championId = 266 }, lane = parts[2], mode = parts[3], sections = new object[0] });
    }
    static void Baseline(string root) {
        string directory = Path.Combine(root, "data/library/1-5"); Directory.CreateDirectory(directory); Directory.CreateDirectory(Path.Combine(root, "cache"));
        var catalog = Map(new { data = new { heroes = new[] { new { id = 266, name = "Fixture", en = "Aatrox", lanes = DataFormat.Lanes.ToDictionary(lane => lane, lane => 1) } },
            lanes = DataFormat.Lanes.Select(lane => new { key = lane, label = lane }).ToArray() } });
        var entries = new Dictionary<string, object>(); string fingerprint = new string('a', 64);
        foreach (string key in DataFormat.ExpectedKeys(catalog, "1-5")) {
            string path = Path.Combine(directory, key + ".json.gz");
            DataFormat.WriteCompressed(path, new { key = key, fingerprint = fingerprint, fetchedAt = "2026-01-01T00:00:00Z", data = Detail(key) });
            entries[key] = DataFormat.RecordFileHash(path);
        }
        DataFormat.WriteRecord(Path.Combine(directory, "manifest.json"), new { schema = 1, profile = "1-5", fingerprint = fingerprint, sourceGeneratedAt = "2026-01-01T00:00:00Z",
            builtAt = "2026-01-01T00:00:00Z", expected = entries.Count, entries = entries, catalog = catalog });
    }
    sealed class Source : IDisposable {
        internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        internal bool Block;
        internal int Calls;
        internal Dictionary<string, object> Fetch(string url, CancellationToken token) {
            Interlocked.Increment(ref Calls); token.ThrowIfCancellationRequested();
            if (url.EndsWith("/status")) return Map(new { success = true, ready = true, package = new { sha256 = new string('b', 64) }, meta = new { generatedAt = "2026-02-01T00:00:00Z" },
                lanes = DataFormat.Lanes.Select(lane => new { key = lane, championCount = 1 }).ToArray() });
            if (url == DataFormat.HeroSource) return Map(new { version = "fixture", hero = new[] { new { heroId = "266", title = "Fixture", name = "Aatrox", alias = "Aatrox", keywords = "" } } });
            if (url.Contains("/champions?")) return Map(new { success = true, champions = new[] { new { championId = 266, championNameEn = "Aatrox", championNameCn = "Fixture", games = 1 } } });
            if (url.Contains("/detail?")) {
                Entered.Set(); if (Block) Release.Wait(token);
                var query = HttpUtility.ParseQueryString(new Uri(url).Query);
                return Detail("detail-266-" + query["lane"] + "-" + query["mode"] + "-" + query["thresholdLevel"] + "-" + query["sensitivity"]);
            }
            throw new IOException("Unexpected fixture request");
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    static ApplicationRuntime Create(string root, Source source, TestClock clock) {
        var app = new ApplicationRuntime(new RuntimeOptions(root, false, true), source.Fetch,
            (url, token) => { throw new IOException("Unexpected release request"); }, clock: clock);
        app.Library.InitializeLibrary(); return app;
    }
    static void MaintenanceReceiptFailure(bool completeLibrary) {
        string root = NewRoot(); Baseline(root); var clock = new TestClock();
        string receipt = Path.Combine(root, "cache/maintenance-state.json"); File.WriteAllText(receipt, "{}");
        using (var source = new Source()) using (var app = Create(root, source, clock)) {
            using (var occupied = new FileStream(receipt, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (completeLibrary) app.Updates.StartLibraryUpdate("1-5", false); else app.Updates.StartHeroUpdate(Key);
                Idle(app);
                Check(DataFormat.Str(Map(app.Updates.Status), "state") == "complete", "Maintenance failure changed the successful data result");
                Check((bool)Map(app.Storage.Status)["deferred"], "Failed maintenance receipt was reported as successful");
                Check(app.Queries.LocalDetail(Key)["data"] != null, "Maintenance failure made the published result unavailable");
            }
            clock.Advance(TimeSpan.FromSeconds(59)); app.Storage.ScheduleStorageMaintenance(); Idle(app);
            Check((bool)Map(app.Storage.Status)["deferred"], "Deferred maintenance retried before its cooldown");
            clock.Advance(TimeSpan.FromSeconds(2)); app.Storage.ScheduleStorageMaintenance(); Idle(app);
            Check(!(bool)Map(app.Storage.Status)["deferred"], "Maintenance did not recover after the occupied file was released");
            Check(DataFormat.DateValue(DataFormat.Str(DataFormat.ReadRecord(receipt), "checkedAt")) != DateTime.MinValue, "Recovered maintenance did not save its receipt");
            int calls = source.Calls;
            if (completeLibrary) app.Updates.StartLibraryUpdate("1-5", false); else app.Updates.StartHeroUpdate(Key);
            Idle(app);
            Check(source.Calls > calls && DataFormat.Str(Map(app.Updates.Status), "state") == "complete", "A later update remained queued after maintenance failed");
        }
    }
    static string Checkpoint(string root) { return Path.Combine(root, "cache/audit-state.json"); }
    static void SetCompletion(string root, DateTime date) {
        var saved = DataFormat.ReadRecord(Checkpoint(root)); saved["completedAt"] = date.ToString("o"); DataFormat.WriteRecord(Checkpoint(root), saved);
    }
    static int Damaged(string root) { return DataFormat.Arr(DataFormat.ReadRecord(Checkpoint(root))["damaged"]).Count(); }
    static void ExpiredAudit() {
        string root = NewRoot(); Baseline(root); string path = Path.Combine(root, "data/library/1-5/" + Key + ".json.gz"); byte[] intact = File.ReadAllBytes(path);
        using (var app = new ApplicationRuntime(new RuntimeOptions(root, true, true))) {
            app.Library.InitializeLibrary(); app.Library.AuditLibrary(CancellationToken.None); Check(Damaged(root) == 0, "Baseline audit found damage");
            File.WriteAllText(path, "damage after a completed audit");
            app.Library.AuditLibrary(CancellationToken.None); Check(Damaged(root) == 0, "Fresh completion was needlessly rescanned");
            SetCompletion(root, DateTime.UtcNow.AddDays(-2)); app.Library.AuditLibrary(CancellationToken.None);
            Check(Damaged(root) == 1, "Expired completed audit skipped new damage");
            File.WriteAllBytes(path, intact); SetCompletion(root, DateTime.UtcNow.AddDays(-2)); app.Library.AuditLibrary(CancellationToken.None);
            Check(Damaged(root) == 0, "Expired audit did not recognize a repaired record");
            File.WriteAllText(path, "damage after a clock correction"); SetCompletion(root, DateTime.UtcNow.AddDays(2)); app.Library.AuditLibrary(CancellationToken.None);
            Check(Damaged(root) == 1, "Future completion timestamp suppressed integrity checking");
        }
    }
    static void UnfinishedAuditResume() {
        string root = NewRoot(); Baseline(root); string directory = Path.Combine(root, "data/library/1-5");
        string[] paths = Directory.GetFiles(directory, "*.json.gz").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        byte[] first = File.ReadAllBytes(paths[0]); File.WriteAllText(paths[0], "first damaged record");
        using (var app = new ApplicationRuntime(new RuntimeOptions(root, true, true))) {
            app.Library.InitializeLibrary(); app.Library.AuditLibrary(CancellationToken.None); Check(Damaged(root) == 1, "Initial damaged record was not checkpointed");
        }
        // Model a persisted interruption immediately after the first checked record.
        var saved = DataFormat.ReadRecord(Checkpoint(root)); saved["completedAt"] = ""; saved["next"] = 1; DataFormat.WriteRecord(Checkpoint(root), saved);
        File.WriteAllBytes(paths[0], first); File.WriteAllText(paths[1], "damage beyond the saved cursor");
        using (var app = new ApplicationRuntime(new RuntimeOptions(root, true, true))) {
            app.Library.InitializeLibrary(); app.Library.AuditLibrary(CancellationToken.None);
            Check(Damaged(root) == 2, "Unfinished audit lost saved damage or restarted instead of resuming");
            saved = DataFormat.ReadRecord(Checkpoint(root));
            Check(DataFormat.Str(saved, "next") == DataFormat.Str(saved, "total") && DataFormat.Str(saved, "completedAt") != "", "Resumed audit did not finish its checkpoint");
        }
    }
    static void CaseAliasCoordination() {
        string root = NewRoot(); Baseline(root);
        using (var first = new Source { Block = true }) using (var second = new Source())
        using (var a = Create(root.ToLowerInvariant(), first, new TestClock())) using (var b = Create(root.ToUpperInvariant(), second, new TestClock())) {
            a.Updates.StartHeroUpdate(Key); Check(first.Entered.Wait(5000), "First writer did not begin");
            b.Updates.StartHeroUpdate(Key); b.Storage.ScheduleStorageMaintenance();
            Until(() => (bool)Map(b.Storage.SpaceInfo())["waiting"], "Case-alias maintenance did not respect writer ownership");
            Check(!second.Entered.Wait(200) && second.Calls == 0 && (bool)Map(b.Updates.Status)["busy"], "Case-alias updater bypassed the directory mutex");
            Check(b.Queries.LocalDetail(Key)["data"] != null, "Waiting for directory ownership blocked local queries");
            a.Updates.CancelLibraryUpdate(); Idle(a); Idle(b);
            Check(DataFormat.Str(Map(b.Updates.HeroUpdateInfo(Key)), "state") == "complete", "Case-alias writer did not continue after ownership was released");
            Check(DataFormat.Str(Map(a.Queries.LocalDetail(Key)["cache"]), "source") == "focused", "Case-alias publication was not visible to the first runtime");
        }
    }
    static int Main(string[] args) {
        var cases = new Dictionary<string, Action> {
            { "hero-maintenance-failure", () => MaintenanceReceiptFailure(false) },
            { "library-maintenance-failure", () => MaintenanceReceiptFailure(true) },
            { "expired-audit", ExpiredAudit },
            { "unfinished-audit-resume", UnfinishedAuditResume },
            { "case-alias-coordination", CaseAliasCoordination }
        };
        if (args.Length > 0 && args[0] != "" && !cases.ContainsKey(args[0])) { Console.Error.WriteLine("Unknown regression case: " + args[0]); return 1; }
        foreach (var entry in cases) {
            if (args.Length > 0 && args[0] != "" && args[0] != entry.Key) continue;
            try { entry.Value(); Console.WriteLine("PASS: " + entry.Key); }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL: " + entry.Key + " — " + ex); }
        }
        return failures == 0 ? 0 : 1;
    }
}

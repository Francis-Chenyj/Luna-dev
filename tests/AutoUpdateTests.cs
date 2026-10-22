using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

internal static class AutoUpdateTests {
    static Luna.ApplicationRuntime App;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Library = Path.Combine(Root, "cache", "library");
    static readonly string B = new string('b', 64);
    static readonly string[] Lanes = { "top", "jungle", "middle", "bottom", "support" };
    static Dictionary<string, object> Manifest, Catalog;
    static Dictionary<string, Dictionary<string, object>> Records;
    static string[] Keys;
    static FixtureSource Source;
    static void Restart(bool offline = false, bool manual = false) {
        if (App != null) App.Dispose();
        App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(Root, offline, manual), Source.Fetch, clock: new TestClock());
        App.Library.InitializeLibrary();
    }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static Dictionary<string, object> ConvertObject(object value) { lock (Json) return Map(Json.DeserializeObject(Json.Serialize(value))); }
    static string Serialize(object value) { lock (Json) return Json.Serialize(value); }
    static Dictionary<string, object> Info() { return ConvertObject(App.LibraryInfo("1-5")); }
    static Dictionary<string, object> Job() { return Map(Info()["job"]); }
    static Dictionary<string, object> Query(string key) { return ConvertObject(App.Queries.LocalDetail(key)); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pass(string message) { Console.WriteLine("PASS: " + message); }
    static void WaitUntil(Func<bool> ready, int timeout, string message) {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.ElapsedMilliseconds > timeout) throw new Exception(message); Thread.Sleep(10); }
    }
    static void WaitJob() { WaitUntil(() => !(bool)Job()["busy"], 45000, "Background job timed out"); Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(5)), "Worker cleanup did not finish"); }
    static void NewLaunch() { Restart(); App.Updates.ScheduleStartupUpdate(); }
    static void VerifyDefault(string source, bool measure) {
        var samples = new List<double>();
        foreach (string key in Keys) {
            var watch = Stopwatch.StartNew(); var result = Query(key); samples.Add(watch.Elapsed.TotalMilliseconds);
            Check(Serialize(result["data"]) == Serialize(Records[key]["data"]), "Changed relationship: " + key);
            var cache = Map(result["cache"]);
            Check(cache["source"].ToString() == source && !(bool)cache["isFallback"], "Unexpected provenance: " + key);
        }
        samples.Sort(); Check(samples.Last() < 5000, "A local query exceeded five seconds");
        if (measure) Console.WriteLine("TIMING: " + source + " " + samples.Count + " real records, median/P95/max ms = " + samples[samples.Count / 2].ToString("F2") + "/" + samples[(int)(samples.Count * .95)].ToString("F2") + "/" + samples.Last().ToString("F2"));
    }
    static void AddSavedProfile() {
        string fingerprint = Manifest["fingerprint"].ToString(), directory = Path.Combine(Library, fingerprint, "2-4");
        Directory.CreateDirectory(directory); var entries = new Dictionary<string, object>();
        foreach (string key in Keys) {
            string nextKey = key.Substring(0, key.Length - 3) + "2-4", path = Path.Combine(directory, nextKey + ".json.gz");
            Luna.DataFormat.WriteCompressed(path, new { key = nextKey, fingerprint = fingerprint, fetchedAt = Manifest["builtAt"], data = Records[key]["data"] });
            entries[nextKey] = Luna.DataFormat.FileHash(File.ReadAllBytes(path));
        }
        // Synthetic second profile reuses the real payloads only to exercise update scope.
        Luna.DataFormat.WriteRecord(Path.Combine(directory, "manifest.json"), new { schema = 1, profile = "2-4", fingerprint = fingerprint, sourceGeneratedAt = Manifest["sourceGeneratedAt"], builtAt = Manifest["builtAt"], expected = Keys.Length, entries = entries, catalog = Catalog });
        Luna.DataFormat.WriteRecord(Path.Combine(Library, "active-2-4.json"), new { current = fingerprint, previous = "" });
        App.Library.InitializeLibrary();
    }
    static int Main() {
        try {
            Directory.CreateDirectory(Library);
            string bundled = Path.Combine(Root, "data", "library", "1-5");
            Manifest = Map(Luna.DataFormat.ReadRecord(Path.Combine(bundled, "manifest.json"))); Catalog = Map(Manifest["catalog"]);
            Keys = Map(Manifest["entries"]).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Records = Keys.ToDictionary(k => k, k => Map(Luna.DataFormat.ReadCompressed(Path.Combine(bundled, k + ".json.gz"))));
            var source = new FixtureSource { Fingerprint = Manifest["fingerprint"].ToString() };
            Source = source; Restart(); AddSavedProfile();

            Restart(true); App.Updates.ScheduleStartupUpdate();
            Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(3)) && !(bool)Job()["busy"] && source.Calls == 0, "Offline launch scheduled networking");
            Restart(false, true); App.Updates.ScheduleStartupUpdate();
            Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(3)) && source.Calls == 0, "Manual-only launch scheduled networking");
            App.SavePreference("autoUpdate", "false"); Restart(); App.Updates.ScheduleStartupUpdate();
            Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(3)) && !(bool)Job()["busy"] && source.Calls == 0, "Disabled automatic updates scheduled networking");
            App.SavePreference("autoUpdate", "true"); Restart();
            App.Updates.ScheduleStartupUpdate(); App.Updates.ScheduleStartupUpdate();
            Check((bool)Job()["automatic"] && Job()["state"].ToString() == "scheduled", "Startup did not schedule one delayed job");
            Thread.Sleep(100); Check(source.Calls == 0, "Startup delay was skipped");
            Check(Query(Keys[0])["data"] != null, "Startup timer blocked the first local query");
            App.Updates.CancelLibraryUpdate(); WaitJob(); VerifyDefault("bundled", true);
            Check(source.Calls == 0 && Job()["state"].ToString() == "cancelled", "Cancelling pending startup made network requests");
            App.Updates.ScheduleStartupUpdate(); Check(!(bool)Job()["busy"], "Cancelled automatic task requeued itself");
            App.Updates.StartLibraryUpdate("1-5", false); WaitJob();
            Check(!(bool)Job()["automatic"] && Job()["state"].ToString() == "complete" && source.Details == 0, "Manual update no longer works after cancelling automatic task");
            Pass("offline/manual-only modes, startup delay, one job per launch, cancellation and manual override");

            source.Reset(); source.FailStatus = true; NewLaunch(); WaitJob();
            Check(Job()["state"].ToString() == "error" && source.Calls == 3, "Failed source check was not bounded to three attempts");
            VerifyDefault("bundled", true); Check(Map(Query(Keys[0])["cache"])["localUpdatedAt"].ToString() == "", "Failed update relabeled bundled data");
            source.FailStatus = false;
            Pass("automatic source failure stops with original complete data and provenance intact");

            string mutexName = "Local\\LunaData_" + Luna.DataFormat.DirectoryLockIdentity(Root);
            using (var mutex = new Mutex(false, mutexName)) {
                Check(mutex.WaitOne(0), "Fixture mutex unexpectedly occupied");
                try {
                    source.Reset(); NewLaunch(); WaitUntil(() => Job()["state"].ToString() == "waiting", 9000, "Automatic updater failed to wait for maintenance");
                    VerifyDefault("bundled", true); App.Updates.CancelLibraryUpdate(); WaitJob();
                    Check(source.Calls == 0, "Waiting updater bypassed mutex or did not cancel");
                } finally { mutex.ReleaseMutex(); }
            }
            Pass("background mutex waiting stays cancellable and does not block local queries");

            source.Reset(); source.Fingerprint = B; source.BlockDetails = true; source.FailKey = Keys[8]; NewLaunch();
            Check(source.DetailStarted.Wait(12000), "Automatic download never started");
            VerifyDefault("bundled", true); source.ReleaseDetails.Set(); WaitJob();
            Check(Job()["state"].ToString() == "error", "Interrupted download unexpectedly published");
            int partial = Directory.GetFiles(Path.Combine(Library, B, "1-5"), "*.json.gz").Length;
            Check(partial > 0 && partial < Keys.Length, "Failed download did not retain resumable staging");
            VerifyDefault("bundled", true);
            Pass("blocked/failed download leaves every real default-profile relationship immediately readable");

            source.Reset(); source.BlockDetails = false; source.FailKey = null; NewLaunch();
            var durations = new List<double>(); int reads = 0;
            while ((bool)Job()["busy"]) {
                var watch = Stopwatch.StartNew(); var value = Query(Keys[reads++ % Keys.Length]); durations.Add(watch.Elapsed.TotalMilliseconds);
                Check(value["data"] != null, "Publication exposed an incomplete result");
                Check(!(bool)Map(value["cache"])["isFallback"], "Healthy publication forced fallback");
                if (reads > 30000) throw new Exception("Automatic resume timed out"); Thread.Sleep(2);
            }
            Check(Job()["state"].ToString() == "complete", Job()["message"].ToString());
            Check(source.Details == 2 * Keys.Length - partial, "Resume repeated valid records or downloaded an unexpected profile");
            Check(source.RequestedProfiles.SetEquals(new[] { "1-5", "2-4" }), "Automatic update fetched an unsaved filter profile");
            VerifyDefault("library", true);
            Check(durations.Max() < 5000, "Query blocked on background validation/publication");
            Console.WriteLine("TIMING: concurrent automatic resume/publication, " + reads + " reads, max " + durations.Max().ToString("F2") + " ms");
            Pass("next launch resumes missing records; atomically publishes default and saved profile only");

            string savedAt = Map(Query(Keys[0])["cache"])["localUpdatedAt"].ToString();
            source.Reset(); NewLaunch(); WaitJob();
            Check(source.Calls == 1 && source.Details == 0 && Map(Query(Keys[0])["cache"])["localUpdatedAt"].ToString() == savedAt, "Unchanged startup re-downloaded data or changed local update time");
            source.Reset(); NewLaunch(); App.Workers.Stop(); WaitJob(); Check(source.Calls == 0, "Application exit did not cancel the startup timer");
            Pass("unchanged data requires one status check; exit cancels scheduled networking");
            Console.WriteLine("All automatic-update checks passed in an isolated fixture; no real external network was used."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (App != null) App.Dispose(); if (Source != null) { Source.DetailStarted.Dispose(); Source.ReleaseDetails.Dispose(); } }
    }
    sealed class FixtureSource {
        public string Fingerprint, FailKey;
        public bool FailStatus, BlockDetails;
        public int Calls, Details;
        public readonly HashSet<string> RequestedProfiles = new HashSet<string>();
        public readonly ManualResetEventSlim DetailStarted = new ManualResetEventSlim(), ReleaseDetails = new ManualResetEventSlim();
        public void Reset() { Calls = Details = 0; RequestedProfiles.Clear(); DetailStarted.Reset(); ReleaseDetails.Reset(); }
        public Dictionary<string, object> Fetch(string url, CancellationToken token) {
            Interlocked.Increment(ref Calls);
            // The injected clock advances scheduled waits; production keeps real pacing.
            var heroes = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).ToArray();
            if (url.EndsWith("/status")) {
                if (FailStatus) throw new IOException("Simulated source outage");
                return ConvertObject(new { success = true, ready = true, package = new { sha256 = Fingerprint }, meta = new { generatedAt = DateTime.Parse(Manifest["sourceGeneratedAt"].ToString()).ToUniversalTime().AddHours(1).ToString("o") }, lanes = Lanes.Select(l => new { key = l, championCount = heroes.Count(h => Map(h["lanes"]).ContainsKey(l)) }).ToArray() });
            }
            if (url.Contains("hero_list.js")) return ConvertObject(new { hero = heroes.Select(h => new { heroId = h["id"], title = h["name"], name = h["title"], alias = h["en"], keywords = h["keywords"] }).ToArray(), version = "fixture" });
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            if (url.Contains("/champions?")) return ConvertObject(new { success = true, champions = heroes.Where(h => Map(h["lanes"]).ContainsKey(q["lane"])).Select(h => new { championId = h["id"], championNameEn = h["name"], championNameCn = h["title"], games = 1 }).ToArray() });
            if (url.Contains("/detail?")) {
                string profile = q["thresholdLevel"] + "-" + q["sensitivity"];
                string key = "detail-" + q["championId"] + "-" + q["lane"] + "-" + q["mode"] + "-1-5";
                Interlocked.Increment(ref Details); lock (RequestedProfiles) RequestedProfiles.Add(profile);
                if (BlockDetails) { DetailStarted.Set(); WaitHandle.WaitAny(new[] { ReleaseDetails.WaitHandle, token.WaitHandle }); token.ThrowIfCancellationRequested(); }
                if (key == FailKey) throw new IOException("Simulated download interruption");
                return Map(Records[key]["data"]);
            }
            throw new IOException("Unexpected fixture URL");
        }
    }
}

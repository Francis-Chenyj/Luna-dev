using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

internal static class FocusTests {
    static readonly TestApplication App = new TestApplication(AppDomain.CurrentDomain.BaseDirectory);
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string A = new string('a', 64), B = new string('b', 64), C = new string('c', 64);
    const string Pair = "focus-6-top-1-5", Key = "detail-6-top-father-1-5", Son = "detail-6-top-son-1-5";
    static Dictionary<string, object> Manifest, Catalog;
    static Dictionary<string, Dictionary<string, object>> Records;
    static object Call(string name, params object[] args) { try { return App.Call(name, args); } catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; } }
    static void Set(string name, object value) { App.Set(name, value); }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static string Text(object value) { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(value); }
    static Dictionary<string, object> ConvertObject(object value) { return Map(new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(Text(value))); }
    static Dictionary<string, object> Info() { return ConvertObject(App.Runtime.LibraryInfo("1-5")); }
    static Dictionary<string, object> Job() { return Map(Info()["job"]); }
    static Dictionary<string, object> Query(string key) { return ConvertObject(App.Runtime.Queries.LocalDetail(key)); }
    static string Source(string key) { return Map(Query(key)["cache"])["source"].ToString(); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Wait(Func<bool> condition, int ms, string message) { var w = Stopwatch.StartNew(); while (!condition()) { if (w.ElapsedMilliseconds > ms) throw new Exception(message); Thread.Sleep(10); } }
    static void WaitJob() { Wait(() => !(bool)Job()["busy"], 60000, "Job timed out"); }
    static void Pass(string message) { Console.WriteLine("PASS: " + message); }
    static void Baseline() {
        string bundled = Path.Combine(Root, "data", "library", "1-5");
        Manifest = Map(Luna.DataFormat.ReadRecord(Path.Combine(bundled, "manifest.json"))); Catalog = Map(Manifest["catalog"]);
        Records = Map(Manifest["entries"]).Keys.ToDictionary(k => k, k => Map(Luna.DataFormat.ReadCompressed(Path.Combine(bundled, k + ".json.gz"))));
        Directory.CreateDirectory(Path.Combine(Root, "cache", "library")); App.Runtime.Library.InitializeLibrary();
    }
    static int Main(string[] args) {
        try {
            Baseline();
            if (args.Contains("--live")) {
                // Isolated probe: keep the public catalog, but remove the in-memory complete-profile
                // index so the existing current snapshot does not correctly skip the two requests.
                App.Set("Profiles", Activator.CreateInstance(App.Field("Profiles").GetType()));
                Luna.DataFormat.WriteRecord(Path.Combine(Root, "data", "catalog.json"), Catalog);
                var liveWatch = Stopwatch.StartNew(); var liveBefore = Map(App.Runtime.Updates.SourceStatus(CancellationToken.None));
                App.Runtime.Library.RegisterFocus(Key);
                App.Runtime.Updates.RefreshPriorityPairs(liveBefore, new[] { "1-5" }, new HashSet<string>(), 1, CancellationToken.None);
                var pair = Map(App.Runtime.Library.ReadFocusedPair(Pair));
                Check(pair != null && Map(pair["records"]).Count == 2, "Live pair not saved");
                Console.WriteLine("LIVE: two real directions plus before/after source checks, " + liveWatch.Elapsed.TotalSeconds.ToString("F2") + " seconds; saved bytes " + new FileInfo(Path.Combine(Root, "cache", "focus", Pair + ".json.gz")).Length);
                Console.WriteLine("LIVE ROOT: " + Root); return 0;
            }
            var source = new Fixture(); Set("LibraryTransport", new Func<string, CancellationToken, Dictionary<string, object>>(source.Fetch));
            App.Runtime.Library.RegisterFocus("detail-266-top-father-1-5"); App.Runtime.Library.RegisterFocus(Key);
            int calls = source.Calls; Check(Query(Key)["data"] != null && source.Calls == calls, "Priority registration/query contacted upstream");
            source.BlockKey = Son;
            var watch = Stopwatch.StartNew(); App.Runtime.Updates.ScheduleStartupUpdate();
            Check(source.BlockStarted.Wait(10000), "Focused pair did not start first");
            Check(Source(Key) == "bundled" && Source(Son) == "bundled", "One downloaded side became visible prematurely");
            source.Release.Set(); Wait(() => Source(Key) == "focused", 10000, "Validated pair was not available before full library");
            Check(Source(Son) == "focused" && (bool)Job()["busy"], "Pair publication was incomplete or waited for full library");
            Console.WriteLine("TIMING: startup through first validated priority pair " + watch.Elapsed.TotalMilliseconds.ToString("F0") + " ms (fixture transport; production rate limit skipped)");
            App.Runtime.Updates.CancelLibraryUpdate(); WaitJob();
            var pairBytes = File.ReadAllBytes(Path.Combine(Root, "cache", "focus", Pair + ".json.gz"));
            string savedAt = Map(Query(Key)["cache"])["localUpdatedAt"].ToString();
            Set("FocusRecent", null); App.Runtime.Library.InitializeLibrary();
            Check(Source(Key) == "focused" && Map(Query(Key)["cache"])["localUpdatedAt"].ToString() == savedAt, "Restart lost the published partial progress");
            Pass("current pair precedes full collection; both directions publish atomically and survive cancellation/restart");

            source.Reset(); source.FailSon = true; source.Fingerprint = C;
            var before = source.Status();
            try { App.Runtime.Updates.RefreshPriorityPairs(before, new[] { "1-5" }, new HashSet<string>(), 1, CancellationToken.None); throw new Exception("Expected failed second direction"); } catch (Exception) { Check(source.SonAttempts == 3, "Failure did not use bounded retry"); }
            Check(File.ReadAllBytes(Path.Combine(Root, "cache", "focus", Pair + ".json.gz")).SequenceEqual(pairBytes), "Failed update replaced previously complete pair");
            Pass("failed second direction retains the previous pair and its original dates");

            source.Reset(); source.Fingerprint = C; source.SwitchStatus = true;
            try { App.Runtime.Updates.RefreshPriorityPairs(source.Status(), new[] { "1-5" }, new HashSet<string>(), 1, CancellationToken.None); throw new Exception("Expected changed source rejection"); } catch (Exception ex) { Check(ex.Message.Contains("换版"), "Unexpected source change failure: " + ex.Message); }
            Check(Source(Key) == "focused" && Map(Query(Key)["cache"])["fingerprint"].ToString() == B, "Mixed source edition published");
            Pass("source changes during the pair never publish a mixed edition");

            source.Reset(); source.Fingerprint = B;
            App.Runtime.Updates.RefreshPriorityPairs(source.Status(), new[] { "1-5" }, new HashSet<string>(), 1, CancellationToken.None);
            Check(source.Details == 0, "Restart refetched the already validated focused pair");
            var cacheFile = Path.Combine(Root, "cache", "library", "catalog-cache.json");
            string payload = Text(Catalog);
            Luna.DataFormat.WriteRecord(cacheFile, new { fingerprint = B, payload = payload, sha256 = Luna.DataFormat.Hash(payload) });
            calls = source.Calls; App.Runtime.Updates.ResumeCatalog(source.Status(), CancellationToken.None);
            Check(source.Calls == calls, "Matching catalog was fetched again");
            var durations = new List<double>();
            foreach (var key in Records.Keys) { var t = Stopwatch.StartNew(); var result = Query(key); durations.Add(t.Elapsed.TotalMilliseconds); Check(Text(result["data"]) == Text(Records[key]["data"]), "Relationship changed: " + key); }
            Check(source.Calls == calls && durations.Max() < 5000, "Local reads blocked or used network");
            Console.WriteLine("TIMING: 856 local records including focused overlays, max " + durations.Max().ToString("F2") + " ms");
            Pass("resume skips saved pairs and catalog; all 856 local relationships remain exact without networking");

            string focusedPath = Path.Combine(Root, "cache", "focus", Pair + ".json.gz"); File.WriteAllText(focusedPath, "broken");
            var fallback = Query(Key); Check(Map(fallback["cache"])["source"].ToString() == "bundled" && (bool)Map(fallback["cache"])["isFallback"], "Damaged priority data lost fallback provenance");
            File.WriteAllBytes(focusedPath, pairBytes);
            Check(Query("detail-6-top-father-5-1")["data"] == null, "Focused pair leaked to another condition");
            Set("Offline", true); calls = source.Calls; Check(Source(Key) == "focused" && source.Calls == calls, "Offline focused read contacted upstream"); Set("Offline", false);
            Pass("corrupt data falls back visibly; profiles stay isolated and saved focused data works offline");

            // Test bounded storage using valid small, application-owned pairs.
            var focusRoot = Path.Combine(Root, "cache", "focus"); File.WriteAllText(Path.Combine(focusRoot, "personal.txt"), "keep");
            for (int id = 1000; id < 1070; id++) {
                string pair = "focus-" + id + "-top-1-5"; var rows = new Dictionary<string, object>();
                foreach (string mode in new[] { "father", "son" }) { string key = "detail-" + id + "-top-" + mode + "-1-5"; rows[key] = new { key = key, fingerprint = B, fetchedAt = "2026-10-11T08:00:00Z", data = new { success = true, champion = new { championId = id }, lane = "top", mode = mode, sections = new object[0] } }; }
                string body = Text(rows);
                Luna.DataFormat.WriteCompressed(Path.Combine(focusRoot, pair + ".json.gz"), new { owner = "Luna.Focus.1", pair = pair, fingerprint = B, sourceGeneratedAt = "2026-10-11T08:00:00Z", savedAt = "2026-10-11T08:00:00Z", payload = body, sha256 = Luna.DataFormat.Hash(body) });
            }
            App.Runtime.Library.MaintainFocused(); Check(Directory.GetFiles(focusRoot, "focus-*.json.gz").Length == 64 && File.Exists(focusedPath) && File.ReadAllText(Path.Combine(focusRoot, "personal.txt")) == "keep", "Retention evicted recent data or touched a personal file");
            Pass("priority cache remains bounded to 64 pairs and preserves recent/user files");
            foreach (string key in new[] { Key, Son }) File.Delete(Path.Combine(Root, "cache", "library", B, "1-5", key + ".json.gz"));
            source.Reset(); source.Fingerprint = B; App.Runtime.Updates.StartLibraryUpdate("1-5", false); WaitJob();
            Check(Job()["state"].ToString() == "complete" && source.SonAttempts == 0 && Source(Key) == "library", "Whole-library resume failed to reuse the separately verified pair after staging removal");
            Pass("whole-library completion reuses validated focused data even after its staging files were removed");
            Console.WriteLine("All focused-update checks passed; no external network was used."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    sealed class Fixture {
        public string Fingerprint = B, BlockKey;
        public bool FailSon, SwitchStatus;
        public int Calls, Details, SonAttempts;
        public readonly ManualResetEventSlim BlockStarted = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        public Dictionary<string, object> Status() {
            var heroes = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).ToArray();
            return ConvertObject(new { success = true, ready = true, package = new { sha256 = Fingerprint }, meta = new { generatedAt = "2026-10-12T00:00:00Z" }, lanes = new[] { "top", "jungle", "middle", "bottom", "support" }.Select(l => new { key = l, championCount = heroes.Count(h => Map(h["lanes"]).ContainsKey(l)) }).ToArray() });
        }
        public void Reset() { Calls = Details = SonAttempts = 0; FailSon = SwitchStatus = false; BlockKey = null; }
        public Dictionary<string, object> Fetch(string url, CancellationToken token) {
            Interlocked.Increment(ref Calls); Set("NextRequest", DateTime.MinValue);
            var uri = new Uri(url); var q = HttpUtility.ParseQueryString(uri.Query);
            if (uri.AbsolutePath.EndsWith("/status")) { var s = Status(); if (SwitchStatus) Map(s["package"])["sha256"] = A; return s; }
            if (uri.AbsolutePath.EndsWith("/detail")) {
                string key = "detail-" + q["championId"] + "-" + q["lane"] + "-" + q["mode"] + "-" + q["thresholdLevel"] + "-" + q["sensitivity"];
                Interlocked.Increment(ref Details);
                if (key == Son) { Interlocked.Increment(ref SonAttempts); if (FailSon) throw new IOException("temporary fixture failure"); }
                if (key == BlockKey) { BlockStarted.Set(); Release.Wait(token); }
                Thread.Sleep(4); return Map(Records[key]["data"]);
            }
            if (uri.AbsolutePath.EndsWith("hero_list.js")) return ConvertObject(new { version = "fixture", hero = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).Select(h => new { heroId = h["id"], title = h["name"], name = h["title"], alias = h["en"], keywords = h["keywords"] }).ToArray() });
            if (uri.AbsolutePath.EndsWith("/champions")) return ConvertObject(new { champions = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).Where(h => Map(h["lanes"]).ContainsKey(q["lane"])).Select(h => new { championId = h["id"], championNameCn = h["title"], championNameEn = h["en"], displayGames = Map(h["lanes"])[q["lane"]] }).ToArray() });
            throw new IOException("Unexpected fixture URL: " + uri.AbsolutePath);
        }
    }
}

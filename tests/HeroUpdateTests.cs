using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

internal static class HeroUpdateTests {
    static Luna.ApplicationRuntime App;
    static string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Key = "detail-6-top-father-1-5", Son = "detail-6-top-son-1-5";
    static Dictionary<string, object> Manifest, Catalog;
    static Dictionary<string, Dictionary<string, object>> Records;
    static Fixture SourceFixture;
    static void Restart(Fixture source, bool offline = false) {
        if (App != null) App.Dispose();
        SourceFixture = source;
        App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(Root, offline), source.Fetch, clock: new TestClock());
        App.Library.InitializeLibrary();
    }
    static Dictionary<string, object> Map(object x) { return (Dictionary<string, object>)x; }
    static string Text(object x) { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(x); }
    static Dictionary<string, object> Copy(object x) { return Map(new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(Text(x))); }
    static Dictionary<string, object> Query(string key) { return Copy(App.Queries.LocalDetail(key)); }
    static Dictionary<string, object> Hero(string key) { return Copy(App.Updates.HeroUpdateInfo(key)); }
    static Dictionary<string, object> Job() { return Map(Copy(App.LibraryInfo("1-5"))["job"]); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Wait(Func<bool> ready, string message) { var timer = Stopwatch.StartNew(); while (!ready()) { if (timer.ElapsedMilliseconds > 30000) throw new Exception(message); Thread.Sleep(10); } }
    static void Done() { Wait(() => !(bool)Job()["busy"], "Job did not end"); Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(5)), "Worker cleanup did not finish"); }
    static void Pass(string text) { Console.WriteLine("PASS: " + text); }
    static string FocusFile() { return Path.Combine(Root, "cache", "focus", "focus-6-top-1-5.json.gz"); }
    static int Main() {
        try {
            string bundled = Path.Combine(Root, "data", "library", "1-5");
            Manifest = Map(Luna.DataFormat.ReadRecord(Path.Combine(bundled, "manifest.json"))); Catalog = Map(Manifest["catalog"]);
            Records = Map(Manifest["entries"]).Keys.ToDictionary(k => k, k => Map(Luna.DataFormat.ReadCompressed(Path.Combine(bundled, k + ".json.gz"))));
            Directory.CreateDirectory(Path.Combine(Root, "cache", "library"));
            var fixture = new Fixture { Fingerprint = Manifest["fingerprint"].ToString(), Generated = Manifest["sourceGeneratedAt"].ToString() };
            Restart(fixture);
            fixture.BlockKey = Son;
            App.Updates.StartHeroUpdate(Key); Check(fixture.Started.Wait(10000), "Manual request did not reach both directions");
            App.Updates.StartHeroUpdate(Key);
            Check(Map(Query(Key)["cache"])["source"].ToString() == "bundled", "One side became active before the other side finished");
            fixture.Release.Set(); Done();
            Check(Hero(Key)["state"].ToString() == "complete", "Manual update failed");
            Check(fixture.DetailCalls == 2 && fixture.StatusCalls == 2 && fixture.CatalogCalls == 0, "Single hero downloaded an entire library or duplicate requests");
            Check(Map(Query(Key)["cache"])["source"].ToString() == "focused" && Map(Query(Son)["cache"])["source"].ToString() == "focused", "Same-edition explicit update did not supersede bundled pair");
            var saved = File.ReadAllBytes(FocusFile()); string savedAt = Map(Query(Key)["cache"])["localUpdatedAt"].ToString();
            Pass("same-edition explicit refresh makes exactly two detail requests; coalesced clicks; atomic pair and downloaded provenance");

            // Model an already committed downloaded full profile with the same edition.
            string committed = Path.Combine(Root, "cache", "library", fixture.Fingerprint, "1-5");
            Directory.CreateDirectory(committed);
            foreach (string file in Directory.GetFiles(bundled)) File.Copy(file, Path.Combine(committed, Path.GetFileName(file)), true);
            Luna.DataFormat.WriteRecord(Path.Combine(Root, "cache", "library", "active-1-5.json"), new { current = fixture.Fingerprint, previous = "" });
            var originalHashes = Directory.GetFiles(committed).ToDictionary(p => p, p => Luna.DataFormat.FileHash(File.ReadAllBytes(p)).ToString());
            App.Library.InitializeLibrary(); fixture.Reset(); App.Updates.StartHeroUpdate(Key); Done();
            Check(originalHashes.All(p => Luna.DataFormat.FileHash(File.ReadAllBytes(p.Key)).ToString() == p.Value), "Explicit refresh rewrote immutable manifest or detail files");
            Check(Map(Query(Key)["cache"])["source"].ToString() == "focused", "Manual data was hidden behind an unchanged full profile");
            saved = File.ReadAllBytes(FocusFile()); savedAt = Map(Query(Key)["cache"])["localUpdatedAt"].ToString();
            Pass("committed complete-profile hashes stay untouched by a fresh same-edition hero request");

            fixture.Reset(); fixture.FailSon = true; App.Updates.StartHeroUpdate(Key); Done();
            Check(Hero(Key)["state"].ToString() == "error" && fixture.SonCalls == 3, "Failure was not bounded");
            Check(File.ReadAllBytes(FocusFile()).SequenceEqual(saved) && Map(Query(Key)["cache"])["localUpdatedAt"].ToString() == savedAt, "Failed refresh lost previous pair/date");
            fixture.Reset(); fixture.ChangeOnSecondStatus = true; App.Updates.StartHeroUpdate(Key); Done();
            Check(Hero(Key)["state"].ToString() == "error" && File.ReadAllBytes(FocusFile()).SequenceEqual(saved), "Changed upstream edition published a mixed pair");
            Pass("failed second response and changing source version both preserve old pair and dates");

            fixture.Reset(); string other = "detail-6-top-father-4-2"; App.Updates.StartHeroUpdate(other); Done();
            Check(Map(Query(other)["cache"])["source"].ToString() == "focused", "Exact requested filter profile was not saved");
            Check(File.ReadAllBytes(FocusFile()).SequenceEqual(saved), "Different filter profile overwrote default pair");
            Restart(fixture, true);
            Check(Query(Key)["data"] != null && Query(other)["data"] != null, "Offline reload lost saved pairs");
            try { App.Updates.StartHeroUpdate(Key); throw new Exception("Offline update accepted"); } catch (ArgumentException) { }
            Pass("nondefault filters stay isolated; restart/offline lookup keeps validated results");

            // A single-hero request interrupts neither the full job nor its local readers.
            fixture.Reset(); fixture.Fingerprint = new string('b', 64); fixture.Generated = "2026-10-12T00:00:00Z";
            App.Dispose();
            Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "priority-and-retention");
            string freshBundle = Path.Combine(Root, "data/library/1-5"); Directory.CreateDirectory(freshBundle);
            foreach (string file in Directory.GetFiles(bundled)) File.Copy(file, Path.Combine(freshBundle, Path.GetFileName(file)));
            Restart(fixture);
            fixture.BlockKey = Records.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
            App.Updates.StartLibraryUpdate("1-5", false); Check(fixture.Started.Wait(10000), "Full-library fixture did not block");
            App.Updates.StartHeroUpdate("detail-266-top-father-1-5");
            Wait(() => Hero("detail-266-top-father-1-5")["state"].ToString() == "complete", "Priority hero request was starved by full-library download");
            Check((bool)Job()["busy"] && Query("detail-266-top-father-1-5")["data"] != null, "Priority hero waited for full library or damaged results");
            App.Updates.CancelLibraryUpdate(); fixture.Release.Set(); Done();
            Pass("manual hero refresh is serviced during a whole-library update without restarting the collection");

            fixture.Reset();
            var history = Records.Keys.Where(k => k.Contains("-father-")).Take(28).ToArray();
            foreach (string key in history) { App.Updates.StartHeroUpdate(key); Done(); Check(Hero(key)["state"].ToString() == "complete", "History fixture failed to complete"); }
            Check(history.Take(4).All(k => Hero(k)["state"].ToString() == "idle") && history.Skip(4).All(k => Hero(k)["state"].ToString() == "complete"), "Completed job history did not evict oldest states at its 24-entry bound");
            var samples = new List<double>();
            foreach (string key in Records.Keys) { var t = Stopwatch.StartNew(); Check(Query(key)["data"] != null, "Local record missing"); samples.Add(t.Elapsed.TotalMilliseconds); }
            Check(samples.Max() < 5000, "Local query blocked");
            Console.WriteLine("TIMING: 856 local records, max " + samples.Max().ToString("F2") + " ms (controlled transport; no external network).");
            Pass("manual job state remains bounded; all 856 local records remain readable");
            Console.WriteLine("All manual-hero checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (App != null) App.Dispose(); if (SourceFixture != null) { SourceFixture.Started.Dispose(); SourceFixture.Release.Dispose(); } }
    }
    sealed class Fixture {
        public string Fingerprint, Generated, BlockKey;
        public bool FailSon, ChangeOnSecondStatus;
        public int DetailCalls, StatusCalls, CatalogCalls, SonCalls;
        public ManualResetEventSlim Started = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        public void Reset() { DetailCalls = StatusCalls = CatalogCalls = SonCalls = 0; FailSon = ChangeOnSecondStatus = false; BlockKey = null; Started.Reset(); Release.Reset(); }
        public Dictionary<string, object> Fetch(string url, CancellationToken token) {
            var uri = new Uri(url); var q = HttpUtility.ParseQueryString(uri.Query);
            if (uri.AbsolutePath.EndsWith("/status")) {
                int n = Interlocked.Increment(ref StatusCalls);
                return Copy(new { success = true, ready = true, package = new { sha256 = ChangeOnSecondStatus && n > 1 ? new string('c', 64) : Fingerprint }, meta = new { generatedAt = Generated }, lanes = new[] { "top", "jungle", "middle", "bottom", "support" }.Select(l => new { key = l, championCount = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).Count(h => Map(h["lanes"]).ContainsKey(l)) }).ToArray() });
            }
            if (uri.AbsolutePath.EndsWith("/detail")) {
                string key = "detail-" + q["championId"] + "-" + q["lane"] + "-" + q["mode"] + "-" + q["thresholdLevel"] + "-" + q["sensitivity"];
                Interlocked.Increment(ref DetailCalls);
                if (key == Son) { Interlocked.Increment(ref SonCalls); if (FailSon) throw new IOException("Test-only second-direction failure"); }
                if (key == BlockKey) { Started.Set(); Release.Wait(token); }
                Thread.Sleep(3);
                string original = "detail-" + q["championId"] + "-" + q["lane"] + "-" + q["mode"] + "-1-5";
                return Map(Records[original]["data"]);
            }
            Interlocked.Increment(ref CatalogCalls);
            var heroes = ((object[])Map(Catalog["data"])["heroes"]).Select(Map);
            if (uri.AbsolutePath.EndsWith("hero_list.js")) return Copy(new { version = "fixture", hero = heroes.Select(h => new { heroId = h["id"], title = h["name"], name = h["title"], alias = h["en"], keywords = h["keywords"] }).ToArray() });
            return Copy(new { champions = heroes.Where(h => Map(h["lanes"]).ContainsKey(q["lane"])).Select(h => new { championId = h["id"], championNameCn = h["title"], championNameEn = h["en"], displayGames = Map(h["lanes"])[q["lane"]] }).ToArray() });
        }
    }
}

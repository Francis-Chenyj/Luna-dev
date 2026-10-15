using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class StorageTests {
    static readonly Type App = typeof(Luna.Program);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Library = Path.Combine(Root, "cache", "library");
    static readonly string[] Lanes = { "top", "jungle", "middle", "bottom", "support" };
    const string Key = "detail-266-top-father-1-5";
    static readonly string A = Id(1), B = Id(2), C = Id(3), D = Id(4);
    static string Id(int value) { return value.ToString("x64"); }
    static object Call(string name, params object[] args) {
        try { return App.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static void Set(string name, object value) { App.GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value); }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static Dictionary<string, object> ConvertObject(object value) { lock (Json) return Map(Json.DeserializeObject(Json.Serialize(value))); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pass(string message) { Console.WriteLine("PASS: " + message); }
    static string ProfilePath(string fingerprint, string profile = "1-5") { return Path.Combine(Library, fingerprint, profile); }
    static void RemoveFixture(string path) {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || full.TrimEnd('\\') == Root.TrimEnd('\\')) throw new Exception("Fixture path escaped temporary root");
        if (!Directory.Exists(full)) return;
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(full, false); return; }
        foreach (string file in Directory.GetFiles(full)) File.Delete(file);
        foreach (string child in Directory.GetDirectories(full)) RemoveFixture(child);
        Directory.Delete(full, false);
    }
    static Dictionary<string, object> Catalog() {
        return ConvertObject(new { data = new { heroes = new[] { new { id = 266, name = "Aatrox", lanes = Lanes.ToDictionary(l => l, l => 1) } }, lanes = Lanes.Select(l => new { key = l }).ToArray() } });
    }
    static void CreateProfile(string fingerprint, string profile = "1-5", bool complete = true, int daysAgo = 0, bool bundled = false) {
        string directory = bundled ? Path.Combine(Root, "data", "library", profile) : ProfilePath(fingerprint, profile);
        Directory.CreateDirectory(directory);
        var catalog = Catalog(); var keys = (List<string>)Call("ExpectedKeys", catalog, profile);
        var entries = new Dictionary<string, object>(); var stamp = DateTime.UtcNow.AddDays(-daysAgo).ToString("o");
        foreach (string key in complete ? keys : keys.Take(2)) {
            var p = key.Split('-'); string path = Path.Combine(directory, key + ".json.gz");
            Call("WriteCompressed", path, new { key = key, fingerprint = fingerprint, fetchedAt = stamp,
                data = new { success = true, champion = new { championId = 266 }, lane = p[2], mode = p[3], sections = new object[0], testValue = fingerprint } });
            entries[key] = Call("FileHash", File.ReadAllBytes(path)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysAgo));
        }
        if (complete) Call("WriteRecord", Path.Combine(directory, "manifest.json"), new { schema = 1, fingerprint = fingerprint, profile = profile, sourceGeneratedAt = stamp, expected = keys.Count, entries = entries, catalog = catalog });
        Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddDays(-daysAgo));
    }
    static void Pointer(string current, string previous = "", string profile = "1-5") {
        Call("WriteRecord", Path.Combine(Library, "active-" + profile + ".json"), new { current = current, previous = previous });
    }
    static void Reset() {
        RemoveFixture(Path.Combine(Root, "cache")); RemoveFixture(Path.Combine(Root, "data"));
        Directory.CreateDirectory(Library); CreateProfile(Id(0), "1-5", true, 30, true); Call("InitializeLibrary");
    }
    static void Clean(string source = "") { Call("MaintainStorage", source, CancellationToken.None); }
    static Dictionary<string, object> Report() { return Map(ConvertObject(Call("LibraryInfo", "1-5"))["storage"]); }
    static string Value() { return Map(Map(Call("LocalDetail", Key))["data"])["testValue"].ToString(); }
    static void WaitMaintenance() {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 10000) {
            if ((int)App.GetField("StorageWorker", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == 0) return;
            Thread.Sleep(10);
        }
        throw new Exception("Maintenance worker did not finish");
    }
    static int Main() {
        try {
            Reset(); CreateProfile(A); CreateProfile(B); CreateProfile(C); Pointer(C, B); Call("InitializeLibrary");
            File.WriteAllText(Path.Combine(Root, "cache", "detail-266-top-father-1-5.json"), "legacy sentinel");
            string bundledHash = (string)Call("FileHash", File.ReadAllBytes(Path.Combine(Root, "data", "library", "1-5", "manifest.json")));
            Clean(); Check(!Directory.Exists(ProfilePath(A)) && Directory.Exists(ProfilePath(B)) && Directory.Exists(ProfilePath(C)), "Did not keep exactly active/previous editions");
            Check(Value() == C && (long)Convert.ToInt64(Report()["freedBytes"]) > 0, "Cleanup changed current result or omitted freed-byte count");
            Check(File.ReadAllText(Path.Combine(Root, "cache", "detail-266-top-father-1-5.json")) == "legacy sentinel", "Legacy cache was modified");
            Check((string)Call("FileHash", File.ReadAllBytes(Path.Combine(Root, "data", "library", "1-5", "manifest.json"))) == bundledHash, "Bundled data was modified");
            File.WriteAllText(Path.Combine(ProfilePath(C), Key + ".json.gz"), "broken");
            Check(Value() == B && (string)Call("PreviousUsableFingerprint", "1-5", D) == B, "Corrupt current displaced usable backup");
            Pass("prunes old editions, preserves bundled/legacy files and keeps usable corruption fallback");

            Reset(); CreateProfile(A); Pointer(A); CreateProfile(B, "1-5", false, 2); CreateProfile(C, "1-5", false, 1);
            Clean(); Check(!Directory.Exists(ProfilePath(B)) && Directory.Exists(ProfilePath(C)), "Migration did not keep only newest resumable edition");
            CreateProfile(D, "1-5", false, 9); Clean(C); Check(!Directory.Exists(ProfilePath(D)), "Expired staging was retained");
            Call("RecordSourceFingerprint", B); Clean(); Check(!Directory.Exists(ProfilePath(C)) && Directory.Exists(ProfilePath(A)), "Known new source did not invalidate old staging or deleted active data");
            CreateProfile(C, "1-5", false, 1); Call("WriteRecord", Path.Combine(Library, "source-state.json"), new { fingerprint = B, checkedAt = DateTime.UtcNow.AddDays(-10).ToString("o") });
            Clean(); Check(Directory.Exists(ProfilePath(C)), "Stale upstream observation deleted recent resumable work");
            Pass("retains at most one recent partial edition per profile; expires after 7 days; uses only fresh upstream observations");

            Reset(); CreateProfile(A); CreateProfile(B); CreateProfile(C); Pointer(C, B);
            File.WriteAllText(Path.Combine(ProfilePath(C), Key + ".json.gz"), "broken"); Clean();
            Check(Directory.Exists(ProfilePath(A)) && (bool)Report()["deferred"], "Unsafe cleanup while active library was damaged");
            CreateProfile(C); File.WriteAllText(Path.Combine(Library, "active-1-5.json"), "broken"); Clean();
            Check(Directory.Exists(ProfilePath(A)), "Malformed activation pointer caused data deletion");
            File.Delete(Path.Combine(Library, "active-1-5.json")); Clean(); Check(Directory.Exists(ProfilePath(A)), "Unpublished complete data deleted without an activation pointer");
            Pass("damaged libraries, malformed pointers and unpublished complete editions are preserved conservatively");

            Reset(); CreateProfile(A); CreateProfile(B); Pointer(B);
            string note = Path.Combine(ProfilePath(A), "my-note.txt"); File.WriteAllText(note, "keep me"); Clean();
            Check(File.ReadAllText(note) == "keep me" && Directory.Exists(ProfilePath(A)), "User-added file or containing directory was removed");
            string stray = Path.Combine(Library, "personal"); Directory.CreateDirectory(stray); File.WriteAllText(Path.Combine(stray, "keep.txt"), "keep");
            string oldTemp = Path.Combine(ProfilePath(B), Key + ".json.gz." + new string('a', 32) + ".tmp"); File.WriteAllText(Luna.Deployment.IoPath(oldTemp), "orphan"); File.SetLastWriteTimeUtc(Luna.Deployment.IoPath(oldTemp), DateTime.UtcNow.AddDays(-2));
            string shortTemp = Path.Combine(ProfilePath(B), ".luna-" + new string('b', 32) + ".tmp");
            string pointerTemp = Path.Combine(Library, ".luna-" + new string('c', 32) + ".tmp");
            string activeTemp = Path.Combine(ProfilePath(B), ".luna-" + new string('d', 32) + ".tmp");
            foreach (string file in new[] { shortTemp, pointerTemp }) { File.WriteAllText(file, "orphan"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2)); }
            File.WriteAllText(activeTemp, "recent");
            Clean(); Check(!File.Exists(oldTemp) && File.Exists(Path.Combine(stray, "keep.txt")), "Temporary cleanup crossed ownership boundary");
            Check(!File.Exists(shortTemp) && !File.Exists(pointerTemp) && File.Exists(activeTemp), "Short-name temp retention failed");
            Check(!(bool)Call("LibraryPath", Path.Combine(Library, "..", "outside.txt")), "Path traversal passed cleanup boundary");
            Pass("user files and unknown directories survive; stale owned temp files are cleaned; traversal rejected");

            Reset(); CreateProfile(A); CreateProfile(B); Pointer(B);
            string locked = Path.Combine(ProfilePath(A), Key + ".json.gz");
            using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                Clean(); Check(File.Exists(locked) && File.Exists(Path.Combine(ProfilePath(A), "manifest.json")) && (bool)Report()["deferred"], "Locked file was removed or its retry marker lost");
            }
            Clean(); Check(!Directory.Exists(ProfilePath(A)) && Value() == B, "Next pass did not finish a deferred cleanup");
            Pass("locked files defer safely and are removed after release without affecting queries");

            Reset(); CreateProfile(A); CreateProfile(B); Pointer(B, A); Call("InitializeLibrary");
            CreateProfile(C); Pointer(C, B); Clean(); Check(Value() == C, "Another process's pointer change was not noticed before query");
            Pass("existing windows refresh changed disk pointers after another process publishes and cleans data");

            Reset(); long minBytes = long.MaxValue, maxBytes = 0; var stopwatch = Stopwatch.StartNew();
            for (int version = 1; version <= 100; version++) {
                string current = Id(version); CreateProfile(current); Pointer(current, version > 1 ? Id(version - 1) : ""); Clean();
                Check(Directory.GetDirectories(Library).Length <= 2 && Value() == current, "History grew past two editions or query failed at version " + version);
                long bytes = Convert.ToInt64(Report()["downloadedBytes"]);
                if (version >= 2) { minBytes = Math.Min(minBytes, bytes); maxBytes = Math.Max(maxBytes, bytes); }
            }
            Check(maxBytes - minBytes < 1024, "Storage accumulated with repeated editions");
            Pass("100 successive dataset editions stay bounded to two; byte range " + minBytes + ".." + maxBytes + "; " + stopwatch.ElapsedMilliseconds + " ms");

            Reset();
            for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) {
                string profile = t + "-" + s; int offset = (t * 5 + s) * 3;
                CreateProfile(Id(offset), profile); CreateProfile(Id(offset + 1), profile); CreateProfile(Id(offset + 2), profile);
                Pointer(Id(offset + 2), Id(offset + 1), profile);
            }
            var queryTimes = new List<long>(); var stopQueries = new CancellationTokenSource();
            Check(Value() == Id(32), "Default query not ready before multi-profile maintenance");
            var queryTask = Task.Run(() => {
                while (!stopQueries.IsCancellationRequested) {
                    var timer = Stopwatch.StartNew(); Check(Value() == Id(32), "Query changed during maintenance");
                    queryTimes.Add(timer.ElapsedMilliseconds); Thread.Sleep(1);
                }
            });
            try { Clean(); } finally { stopQueries.Cancel(); queryTask.Wait(); stopQueries.Dispose(); }
            for (int t = 1; t <= 5; t++) for (int s = 1; s <= 5; s++) {
                string profile = t + "-" + s; int offset = (t * 5 + s) * 3;
                Check(!Directory.Exists(ProfilePath(Id(offset), profile)) && Directory.Exists(ProfilePath(Id(offset + 1), profile)) && Directory.Exists(ProfilePath(Id(offset + 2), profile)), "Mixed-edition profile retention failed " + profile);
            }
            Check(queryTimes.Count > 0 && queryTimes.Max() < 5000, "Maintenance blocked local queries");
            Pass("all 25 profiles retain their own two editions; concurrent queries max " + queryTimes.Max() + " ms");

            Reset(); CreateProfile(A); CreateProfile(B); Pointer(B);
            string mutexName = "Local\\LunaData_" + ((string)Call("Hash", Root)).Substring(0, 20);
            using (var mutex = new Mutex(false, mutexName)) {
                mutex.WaitOne();
                Set("StorageNextCheck", 0L); Call("ScheduleStorageMaintenance"); WaitMaintenance();
                Check(Directory.Exists(ProfilePath(A)), "Maintenance ignored active updater's mutex");
                mutex.ReleaseMutex();
            }
            Set("StorageNextCheck", 0L); Call("ScheduleStorageMaintenance"); WaitMaintenance();
            Check(!Directory.Exists(ProfilePath(A)) && Value() == B, "Startup/background maintenance did not resume after updater finished");
            Pass("background maintenance uses updater's cross-process lock and resumes without network requests");

            Reset(); string outside = Path.Combine(Root, "outside-library"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "do not touch");
            string junction = Path.Combine(Library, A);
            using (var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"New-Item -ItemType Junction -Path '" + junction.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })) {
                process.WaitForExit(); Check(process.ExitCode == 0, "Could not create junction fixture: " + process.StandardError.ReadToEnd());
            }
            Clean(); Check(File.ReadAllText(Path.Combine(outside, "sentinel.txt")) == "do not touch" && !(bool)Call("LibraryPath", Path.Combine(junction, "sentinel.txt")), "Junction crossed cleanup boundary");
            Directory.Delete(junction, false);
            Pass("junction targets outside the library are not traversed or deleted");
            Console.WriteLine("All storage checks passed in isolated temporary fixtures."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

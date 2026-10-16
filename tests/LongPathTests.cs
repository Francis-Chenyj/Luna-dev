using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;

internal static class LongPathTests {
    static readonly TestApplication App = new TestApplication(AppDomain.CurrentDomain.BaseDirectory);
    static object Call(string name, params object[] args) {
        try { return App.Call(name, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static int Main() {
        try {
            string parent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "library");
            string filename = "detail-266-middle-father-1-5.json.gz";
            int target = Math.Max(280, parent.Length + filename.Length + 32);
            string folder = parent;
            while (folder.Length + filename.Length + 2 < target) folder = Path.Combine(folder, new string('b', Math.Min(48, target - folder.Length - filename.Length - 2)));
            Directory.CreateDirectory(folder);
            Console.WriteLine("Fixture directory " + folder.Length + " exists: " + Directory.Exists(folder));
            string path = Path.Combine(folder, filename);
            if (path.Length <= 260) throw new Exception("Test must exercise an actual path longer than MAX_PATH");
            Luna.DataFormat.WriteCompressed(path, new { value = "first" });
            Luna.DataFormat.WriteCompressed(path, new { value = "second" });
            var saved = (System.Collections.Generic.Dictionary<string, object>)Luna.DataFormat.ReadCompressed(path);
            if (saved["value"].ToString() != "second") throw new Exception("Long-path atomic replacement failed");
            string record = Path.Combine(folder, "manifest.json");
            Luna.DataFormat.WriteRecord(record, new { value = "first" }); Luna.DataFormat.WriteRecord(record, new { value = "second" });
            var meta = (System.Collections.Generic.Dictionary<string, object>)Luna.DataFormat.ReadRecord(record);
            if (meta["value"].ToString() != "second") throw new Exception("Long-path metadata replacement failed");
            Console.WriteLine("PASS: compressed and metadata atomic replacement beyond MAX_PATH; final path " + path.Length + ", temporary path " + Luna.DataFormat.TemporaryPath(path).ToString().Length + " characters.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

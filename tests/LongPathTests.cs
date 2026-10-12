using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;

internal static class LongPathTests {
    static object Call(string name, params object[] args) {
        try { return typeof(Luna.Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static int Main() {
        try {
            string parent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "library", new string('a', 64));
            string filename = "detail-266-middle-father-1-5.json.gz";
            string folder = Path.Combine(parent, new string('b', 240 - parent.Length - filename.Length - 6), "1-5");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, filename);
            if (path.Length + 37 <= 260 || path.Length >= 260) throw new Exception("Test must reproduce a valid final path whose old temp name exceeded 260 characters");
            Call("WriteCompressed", path, new { value = "first" });
            Call("WriteCompressed", path, new { value = "second" });
            var saved = (System.Collections.Generic.Dictionary<string, object>)Call("ReadCompressed", path);
            if (saved["value"].ToString() != "second") throw new Exception("Long-path atomic replacement failed");
            string record = Path.Combine(folder, "manifest.json");
            Call("WriteRecord", record, new { value = "first" }); Call("WriteRecord", record, new { value = "second" });
            var meta = (System.Collections.Generic.Dictionary<string, object>)Call("ReadRecord", record);
            if (meta["value"].ToString() != "second") throw new Exception("Long-path metadata replacement failed");
            Console.WriteLine("PASS: compressed and metadata write/read/atomic replacement near Windows path boundary; final path " + path.Length + ", old temporary path " + (path.Length + 37) + ", new temporary path " + Call("TemporaryPath", path).ToString().Length + " characters. Does not claim unrestricted long-path support.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

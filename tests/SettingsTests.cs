using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Web.Script.Serialization;

internal static class SettingsTests {
    static readonly Type App = typeof(Luna.Program);
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string FilePath = Path.Combine(Root, "cache", "settings.json");
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static object Call(string name, params object[] args) {
        try { return App.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)Json.DeserializeObject(Json.Serialize(value)); }
    static Dictionary<string, object> Info() { return Map(Call("SettingsInfo")); }
    static Dictionary<string, object> Prefs() { return Map(Info()["preferences"]); }
    static void Set(string name, object value) { App.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
    static void Pass(string text) { Console.WriteLine("PASS: " + text); }
    static int Main(string[] args) {
        try {
            if (args.Length == 3 && args[0] == "--writer") { for (int i = 0; i < 20; i++) Call("SavePreference", args[1], args[2]); return 0; }
            Check((bool)Prefs()["autoUpdate"] && (string)Prefs()["language"] == "system" && (string)Prefs()["theme"] == "system", "Default preferences changed");
            Call("SavePreference", "language", "en"); Call("SavePreference", "theme", "light"); Call("SavePreference", "autoUpdate", "false");
            Check((string)Prefs()["language"] == "en" && (string)Prefs()["theme"] == "light" && !(bool)Prefs()["autoUpdate"], "Independent saves lost a choice");
            Check(File.Exists(FilePath) && Info()["warning"].ToString() == "", "Preferences did not persist"); Pass("defaults and independent atomic saves");
            foreach (string[] pair in new[] { new[] { "language", "../en" }, new[] { "theme", "blue" }, new[] { "autoUpdate", "1" }, new[] { "path", "C:/outside" }, new[] { "language", (string)null } }) Reject(() => Call("SavePreference", pair[0], pair[1]));
            Check((string)Prefs()["theme"] == "light", "Invalid save changed the file"); Pass("allowlisted keys and values");
            var children = new[] { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--writer theme dark") { UseShellExecute = false, CreateNoWindow = true }), Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--writer language zh-CN") { UseShellExecute = false, CreateNoWindow = true }) };
            foreach (var child in children) { Check(child.WaitForExit(15000) && child.ExitCode == 0, "Concurrent settings writer failed"); child.Dispose(); }
            Check((string)Prefs()["language"] == "zh-CN" && (string)Prefs()["theme"] == "dark" && !(bool)Prefs()["autoUpdate"], "Cross-process update lost a field"); Pass("two processes share preferences without lost updates");
            File.WriteAllText(FilePath, "{broken"); Check(Info()["warning"].ToString() == "settings.unreadable" && !(bool)Prefs()["autoUpdate"], "Corrupt preferences must fail without network updates");
            Call("SavePreference", "theme", "light"); Check(Directory.GetFiles(Path.GetDirectoryName(FilePath), "settings.invalid-*.json").Any(p => File.ReadAllText(p) == "{broken"), "Corrupt file was not preserved");
            const string future = "{\"schema\":99,\"private\":\"keep-me\"}"; File.WriteAllText(FilePath, future); Reject(() => Call("SavePreference", "theme", "dark")); Check(File.ReadAllText(FilePath) == future, "Unsupported format overwritten");
            File.WriteAllText(FilePath, "{\"schema\":1,\"language\":\"en\",\"theme\":\"system\",\"autoUpdate\":true}"); Pass("corrupt recovery and future-format preservation");
            Set("Offline", true); Set("AutomaticUpdatesEnabled", true); Check(!(bool)Info()["automaticUpdatesThisRun"] && Info()["mode"].ToString() == "offline", "Offline bypass");
            Call("ScheduleStartupUpdate"); Check((int)App.GetField("StartupUpdateScheduled", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == 0, "Offline scheduled networking");
            Set("Offline", false); Set("CommandLineDisablesUpdates", true); Set("AutomaticUpdatesEnabled", false); Call("SavePreference", "autoUpdate", "true");
            Check(!(bool)Info()["automaticUpdatesThisRun"] && Info()["mode"].ToString() == "manual", "Saved choice changed this launch"); Pass("offline and command-line overrides; next-launch semantics");
            Call("RecordDiagnostic", new IOException("C:/private/person token=secret https://private/?key=hidden"));
            Call("RecordDiagnostic", new InvalidDataException("account-private"));
            var diagnostic = Map(Call("DiagnosticInfo")); var text = Json.Serialize(diagnostic);
            Check(diagnostic.Keys.OrderBy(x => x).SequenceEqual(new[] { "automaticUpdatesThisRun", "errors", "mode", "success", "version" }), "Unexpected diagnostic field");
            Check(!text.Contains("private") && !text.Contains("secret") && !text.Contains("hidden") && text.Contains("storage") && text.Contains("data"), "Diagnostic privacy violation"); Pass("diagnostics omit exception messages and paths");
            foreach (string value in new[] { "../LICENSE", "C:/Windows", "https://example.com", "", null }) { Reject(() => Call("FolderFor", value)); Reject(() => Call("ReadAboutDocument", value)); }
            Check((string)Call("FolderFor", "data") == Path.Combine(Root, "cache"), "Wrong data directory");
            File.WriteAllText(Path.Combine(Root, "LICENSE"), "MIT <script>inert document text</script>");
            Check(Map(Call("ReadAboutDocument", "license"))["text"].ToString().Contains("<script>"), "Document data altered"); Pass("fixed folder and document allowlists");
            Console.WriteLine("All settings checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

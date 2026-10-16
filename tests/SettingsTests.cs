using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

internal static class SettingsTests {
    static Luna.ApplicationRuntime App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(AppDomain.CurrentDomain.BaseDirectory));
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string FilePath = Path.Combine(Root, "cache", "settings.json");
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)Json.DeserializeObject(Json.Serialize(value)); }
    static Dictionary<string, object> Info() { return Map(App.SettingsInfo()); }
    static Dictionary<string, object> Prefs() { return Map(Info()["preferences"]); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
    static void Pass(string text) { Console.WriteLine("PASS: " + text); }
    static int Main(string[] args) {
        try {
            if (args.Length == 3 && args[0] == "--writer") { for (int i = 0; i < 20; i++) App.SavePreference(args[1], args[2]); return 0; }
            var explicitDark = Map(Luna.Program.FrameColors( "dark", false, false));
            var explicitLight = Map(Luna.Program.FrameColors( "light", true, false));
            Check((int)explicitDark["Dark"] == 1 && (int)explicitLight["Dark"] == 0, "App theme must take priority over Windows theme");
            Check(Json.Serialize(explicitDark) == Json.Serialize(Map(Luna.Program.FrameColors( "system", true, false))) && Json.Serialize(explicitLight) == Json.Serialize(Map(Luna.Program.FrameColors( "system", false, false))), "System frame theme did not follow Windows");
            foreach (string theme in new[] { "dark", "light", "system" }) {
                var contrast = Map(Luna.Program.FrameColors( theme, true, true));
                Check((int)contrast["Dark"] == 0 && (int)contrast["Caption"] == -1 && (int)contrast["Text"] == -1, "High contrast must retain system caption/text colors");
            }
            Pass("native frame follows app/system theme and respects Windows high contrast");
            var frame = new Luna.Program.FrameAttributeState(); int writes = 0, last = 0;
            Func<int, int> write = v => { writes++; last = v; return 0; };
            frame.Apply(123, () => null, write, -1);
            Check(writes == 1 && frame.Applied == 123, "Unsupported GET must still allow SET");
            frame.Apply(123, () => null, write, -1); Check(writes == 1, "Write-only attribute needlessly repeated");
            frame.Apply(456, () => null, write, -1); Check(writes == 2 && last == 456, "Write-only theme cannot switch");
            frame.Restore(() => null, write); Check(last == -1, "Write-only attribute did not reset to system default");
            var failedFrame = new Luna.Program.FrameAttributeState(); failedFrame.Apply(1, () => null, v => -1, 0);
            Check(!failedFrame.Applied.HasValue && !failedFrame.Original.HasValue, "Failed native write claimed success");
            frame.Apply(100, () => 50, write, -1); int before = writes;
            frame.Restore(() => 999, write); Check(writes == before, "Restoration overwrote another owner's change");
            Pass("write-only DWM attributes, theme changes, native failures and ownership");
            foreach (string saved in new[] { "ask", "tray", "exit", "invalid", null }) {
                Check(Luna.Program.ResolveCloseAction(saved, true, false) == "exit" && Luna.Program.ResolveCloseAction(saved, false, true) == "exit", "Explicit/session exit must bypass tray preferences");
                Check(Luna.Program.ResolveCloseAction(saved, false, false) == (saved == "tray" || saved == "exit" ? saved : "ask"), "Close preference resolution failed");
            }
            Check(!Luna.Program.CanAskPageToClose(true, false), "Loaded HTML with failed JavaScript must use the native close fallback");
            Check(!Luna.Program.CanAskPageToClose(false, true), "A lost renderer must use the native close fallback");
            Check(Luna.Program.CanAskPageToClose(true, true), "A ready application must keep the accessible close dialog");
            Check((string)Prefs()["closeAction"] == "ask", "First launch must ask");
            App.SavePreference("closeAction", "tray"); App.SavePreference("theme", "dark");
            Check((string)Prefs()["closeAction"] == "tray", "Unrelated preference lost remembered tray choice");
            App.SavePreference("closeAction", "ask"); Reject(() => App.SavePreference("closeAction", "minimize"));
            App.SavePreference("theme", "system");
            Pass("close policy defaults, explicit exit, session end and remembered-choice persistence");
            Check((bool)Prefs()["autoUpdate"] && (string)Prefs()["language"] == "system" && (string)Prefs()["theme"] == "system", "Default preferences changed");
            App.SavePreference("language", "en"); App.SavePreference("theme", "light"); App.SavePreference("autoUpdate", "false");
            Check((string)Prefs()["language"] == "en" && (string)Prefs()["theme"] == "light" && !(bool)Prefs()["autoUpdate"], "Independent saves lost a choice");
            Check(File.Exists(FilePath) && Info()["warning"].ToString() == "", "Preferences did not persist"); Pass("defaults and independent atomic saves");
            foreach (string[] pair in new[] { new[] { "language", "../en" }, new[] { "theme", "blue" }, new[] { "autoUpdate", "1" }, new[] { "path", "C:/outside" }, new[] { "language", (string)null } }) Reject(() => App.SavePreference(pair[0], pair[1]));
            Check((string)Prefs()["theme"] == "light", "Invalid save changed the file"); Pass("allowlisted keys and values");
            var children = new[] { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--writer theme dark") { UseShellExecute = false, CreateNoWindow = true }), Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--writer language zh-CN") { UseShellExecute = false, CreateNoWindow = true }) };
            foreach (var child in children) { Check(child.WaitForExit(15000) && child.ExitCode == 0, "Concurrent settings writer failed"); child.Dispose(); }
            Check((string)Prefs()["language"] == "zh-CN" && (string)Prefs()["theme"] == "dark" && !(bool)Prefs()["autoUpdate"], "Cross-process update lost a field"); Pass("two processes share preferences without lost updates");
            File.WriteAllText(FilePath, "{broken"); Check(Info()["warning"].ToString() == "settings.unreadable" && !(bool)Prefs()["autoUpdate"], "Corrupt preferences must fail without network updates");
            App.SavePreference("theme", "light"); Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(FilePath), "quarantine"), "luna-*.bad").Any(p => File.ReadAllText(p) == "{broken"), "Corrupt file was not preserved");
            const string future = "{\"schema\":99,\"private\":\"keep-me\"}"; File.WriteAllText(FilePath, future); Reject(() => App.SavePreference("theme", "dark")); Check(File.ReadAllText(FilePath) == future, "Unsupported format overwritten");
            File.WriteAllText(FilePath, "{\"schema\":1,\"language\":\"en\",\"theme\":\"system\",\"autoUpdate\":true}"); Check((string)Prefs()["closeAction"] == "ask", "Old settings must migrate to Ask"); Pass("corrupt recovery, old-settings migration and future-format preservation");
            int network = 0;
            Func<string, System.Threading.CancellationToken, Dictionary<string, object>> unexpected = (url, token) => { System.Threading.Interlocked.Increment(ref network); throw new IOException("Unexpected request"); };
            App.Dispose(); App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(Root, true), unexpected);
            Check(!(bool)Info()["automaticUpdatesThisRun"] && Info()["mode"].ToString() == "offline", "Offline bypass");
            App.Updates.ScheduleStartupUpdate(); Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(3)) && network == 0, "Offline scheduled networking");
            App.Dispose(); App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(Root, false, true), unexpected); App.SavePreference("autoUpdate", "true");
            Check(!(bool)Info()["automaticUpdatesThisRun"] && Info()["mode"].ToString() == "manual", "Saved choice changed this launch");
            App.Updates.ScheduleStartupUpdate(); Check(App.Workers.WaitForIdle(TimeSpan.FromSeconds(3)) && network == 0, "Manual launch scheduled networking");
            App.Dispose(); App = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(Root), unexpected);
            Check((bool)Info()["automaticUpdatesThisRun"], "Next launch ignored saved automatic-update preference");
            Pass("offline and command-line overrides; next-launch semantics");
            App.Diagnostics.RecordDiagnostic(new IOException("C:/private/person token=secret https://private/?key=hidden"));
            App.Diagnostics.RecordDiagnostic(new InvalidDataException("account-private"));
            var diagnostic = Map(App.DiagnosticInfo()); var text = Json.Serialize(diagnostic);
            Check(diagnostic.Keys.OrderBy(x => x).SequenceEqual(new[] { "automaticUpdatesThisRun", "errors", "mode", "success", "version" }), "Unexpected diagnostic field");
            Check(!text.Contains("private") && !text.Contains("secret") && !text.Contains("hidden") && text.Contains("storage") && text.Contains("data"), "Diagnostic privacy violation"); Pass("diagnostics omit exception messages and paths");
            foreach (string value in new[] { "../LICENSE", "C:/Windows", "https://example.com", "", null }) { Reject(() => App.Preferences.FolderFor(value)); Reject(() => App.Preferences.ReadAboutDocument(value)); }
            Check((string)App.Preferences.FolderFor("data") == Path.Combine(Root, "cache"), "Wrong data directory");
            File.WriteAllText(Path.Combine(Root, "LICENSE"), "MIT <script>inert document text</script>");
            Check(Map(App.Preferences.ReadAboutDocument("license"))["text"].ToString().Contains("<script>"), "Document data altered"); Pass("fixed folder and document allowlists");
            Console.WriteLine("All settings checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { App.Dispose(); }
    }
}

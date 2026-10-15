using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

// Runs the actual application entry point in a new process, using deterministic
// upstream responses so cross-launch downloads can be counted without changing
// the real service or the user's files. Never included in the Windows runtime.
internal static class SessionHarness {
    static readonly Type App = typeof(Luna.Program);
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    static Dictionary<string, object> Manifest, Catalog;
    static string Session;
    static readonly string Fingerprint = new string('d', 64);
    static object Call(string name, params object[] args) { return App.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
    static void Set(string name, object value) { App.GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value); }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static Dictionary<string, object> Data(object value) { return Map(Json.DeserializeObject(Json.Serialize(value))); }
    [STAThread] static int Main(string[] args) {
        try {
            Session = args[0]; args = args.Skip(1).ToArray();
            Manifest = Map(Call("ReadRecord", Path.Combine(Root, "data/library/1-5/manifest.json"))); Catalog = Map(Manifest["catalog"]);
            Directory.CreateDirectory(Path.Combine(Root, "cache"));
            Call("WriteRecord", Path.Combine(Root, "cache/settings.json"), new { schema = 1, language = "zh-CN", theme = "dark", closeAction = "exit", autoUpdate = true, appAutoCheck = false, appAutoDownload = false });
            Set("LibraryTransport", new Func<string, CancellationToken, Dictionary<string, object>>(Fetch));
            Call("Main", (object)args); return 0;
        } catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "session-error.txt"), ex.ToString()); return 1; }
    }
    static Dictionary<string, object> Fetch(string url, CancellationToken token) {
        // Keep production request spacing (600 ms). The fixture only replaces responses.
        var heroes = ((object[])Map(Catalog["data"])["heroes"]).Select(Map).ToArray();
        if (url.EndsWith("/status")) return Data(new { success = true, ready = true, package = new { sha256 = Fingerprint }, meta = new { generatedAt = DateTime.Parse(Manifest["sourceGeneratedAt"].ToString()).ToUniversalTime().AddHours(2).ToString("o") }, lanes = new[] { "top", "jungle", "middle", "bottom", "support" }.Select(l => new { key = l, championCount = heroes.Count(h => Map(h["lanes"]).ContainsKey(l)) }).ToArray() });
        if (url.Contains("hero_list.js")) return Data(new { hero = heroes.Select(h => new { heroId = h["id"], title = h["name"], name = h["title"], alias = h["en"], keywords = h["keywords"] }).ToArray(), version = "test" });
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        if (url.Contains("/champions?")) return Data(new { success = true, champions = heroes.Where(h => Map(h["lanes"]).ContainsKey(q["lane"])).Select(h => new { championId = h["id"], championNameEn = h["name"], championNameCn = h["title"], games = 1 }).ToArray() });
        if (url.Contains("/detail?")) {
            token.ThrowIfCancellationRequested();
            string key = "detail-" + q["championId"] + "-" + q["lane"] + "-" + q["mode"] + "-1-5";
            File.AppendAllText(Path.Combine(Root, "requests.csv"), Session + "," + key + Environment.NewLine, Encoding.UTF8);
            return Map(Map(Call("ReadCompressed", Path.Combine(Root, "data/library/1-5", key + ".json.gz")))["data"]);
        }
        throw new IOException("Unexpected fixture request");
    }
}

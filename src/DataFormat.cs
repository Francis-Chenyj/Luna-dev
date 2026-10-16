using File = Luna.LocalFile;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Luna {
    internal static class DataFormat {
        internal const string Source = "https://www.malzaharai.top";
        internal const string HeroSource = "https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js";
        internal static readonly IList<string> Lanes = Array.AsReadOnly(new[] { "top", "jungle", "middle", "bottom", "support" });
        internal static readonly IList<string> Labels = Array.AsReadOnly(new[] { "上路", "打野", "中路", "下路", "辅助" });
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }; }
        internal static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        internal static object Get(Dictionary<string, object> value, string key, object fallback = null) { object item; return value != null && value.TryGetValue(key, out item) ? item : fallback; }
        internal static string Str(Dictionary<string, object> value, string key, string fallback = "") { return Convert.ToString(Get(value, key, fallback), CultureInfo.InvariantCulture); }
        internal static IEnumerable<object> Arr(object value) { return (value as IEnumerable ?? new object[0]).Cast<object>(); }
        internal static string Stamp() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        internal static string Hash(string text) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        internal static Dictionary<string, object> ReadRecord(string path) {
            try { if (!SafeLocalPath(path) || !File.Exists(Deployment.IoPath(path)) || new FileInfo(path).Length > RecordByteLimit) return null; return Obj(Json().DeserializeObject(File.ReadAllText(Deployment.IoPath(path), Encoding.UTF8))); } catch { return null; }
        }
        internal static string TemporaryPath(string path) { return Path.Combine(Path.GetDirectoryName(path), ".luna-" + Guid.NewGuid().ToString("N") + ".tmp"); }
        internal static bool ShortTempName(string name) { return System.Text.RegularExpressions.Regex.IsMatch(name, @"^\.luna-[a-f0-9]{32}\.tmp$"); }
        internal static void WriteRecord(string path, object record) {
            string temp = TemporaryPath(path);
            try {
                File.WriteAllText(Deployment.IoPath(temp), Json().Serialize(record), new UTF8Encoding(false));
                if (File.Exists(Deployment.IoPath(path))) File.Replace(Deployment.IoPath(temp), Deployment.IoPath(path), null); else File.Move(Deployment.IoPath(temp), Deployment.IoPath(path));
            } finally { if (File.Exists(Deployment.IoPath(temp))) File.Delete(Deployment.IoPath(temp)); }
        }
        internal static Dictionary<string, object> Envelope(Dictionary<string, object> record, string source, string warning = "", bool needsRefresh = false) {
            return new Dictionary<string, object> {
                { "success", true }, { "data", Json().DeserializeObject(Json().Serialize(Get(record, "data"))) },
                { "cache", new { source = source, fetchedAt = Str(record, "fetchedAt"), warning = warning, needsRefresh = needsRefresh } }
            };
        }
        internal static string FileHash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static bool Fingerprint(string value) { return Regex.IsMatch(value ?? "", @"^[a-f0-9]{64}$"); }
        internal static bool ValidKey(string key) { return Regex.IsMatch(key ?? "", @"^detail-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-(father|son)-[1-5]-[1-5]$"); }
        internal static string ProfileOf(string key) { var pieces = key.Split('-'); return pieces[4] + "-" + pieces[5]; }
        internal static DateTime DateValue(string value) { DateTime result; return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out result) ? result.ToUniversalTime() : DateTime.MinValue; }
        internal static bool IsArray(object value) { return value is object[] || value is System.Collections.ArrayList; }
        internal static bool ValidDetail(Dictionary<string, object> data, string key) {
            if (!ValidKey(key) || !(Get(data, "success") is bool) || !(bool)data["success"]) return false;
            var pieces = key.Split('-');
            if (Str(Obj(Get(data, "champion")), "championId") != pieces[1] || Str(data, "lane") != pieces[2] || Str(data, "mode") != pieces[3] || !IsArray(Get(data, "sections"))) return false;
            var seen = new HashSet<string>();
            foreach (var section in Arr(data["sections"])) {
                var s = Obj(section); string sectionKey = Str(s, "key");
                // The public son response also uses *_father group keys; data.mode identifies direction.
                if (!Regex.IsMatch(sectionKey, @"^(all_segment|(high_mid|high|high_low|mid|mid_low|low)_segment_(father|son))$") || !seen.Add(sectionKey) || !IsArray(Get(s, "items"))) return false;
                foreach (var item in Arr(s["items"])) {
                    var row = Obj(item); int id;
                    if (!int.TryParse(Str(row, "championId"), out id) || id < 1 || id > 10000) return false;
                }
            }
            return seen.Count <= 7;
        }
        internal static List<string> ExpectedKeys(Dictionary<string, object> catalog, string profile) {
            var keys = new HashSet<string>(); var ids = new HashSet<int>(); var data = Obj(Get(catalog, "data"));
            if (!IsArray(Get(data, "heroes")) || !IsArray(Get(data, "lanes"))) throw new IOException("离线目录格式不完整");
            foreach (var value in Arr(data["heroes"])) {
                var hero = Obj(value); int id;
                if (!int.TryParse(Str(hero, "id"), out id) || id < 1 || id > 10000 || !ids.Add(id)) throw new IOException("英雄编号异常或重复");
                foreach (var lane in Obj(Get(hero, "lanes")).Keys) {
                    if (!Lanes.Contains(lane)) throw new IOException("分路无效");
                    foreach (var mode in new[] { "father", "son" })
                        if (!keys.Add("detail-" + id + "-" + lane + "-" + mode + "-" + profile)) throw new IOException("目录存在重复英雄");
                }
            }
            if (keys.Count == 0) throw new IOException("原站未提供可查询的分路");
            return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
        internal static string RecordedTime(string value) { return DateValue(value) == DateTime.MinValue ? "" : value; }
        internal static Dictionary<string, object> ReadCompressed(string path) {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException("压缩记录过大");
            using (var stream = File.OpenRead(Deployment.IoPath(path))) using (var gzip = new GZipStream(stream, CompressionMode.Decompress)) using (var memory = new MemoryStream()) {
                byte[] buffer = new byte[8192]; int n;
                while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0) { memory.Write(buffer, 0, n); if (memory.Length > 16 * 1024 * 1024) throw new IOException("关系记录过大"); }
                return Obj(Json().DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())));
            }
        }
        internal static void WriteCompressed(string path, object record) {
            string temporary = TemporaryPath(path);
            try {
                using (var file = File.Create(Deployment.IoPath(temporary))) using (var gzip = new GZipStream(file, CompressionMode.Compress)) {
                    byte[] bytes = Encoding.UTF8.GetBytes(Json().Serialize(record)); gzip.Write(bytes, 0, bytes.Length);
                }
                if (File.Exists(Deployment.IoPath(path))) File.Replace(Deployment.IoPath(temporary), Deployment.IoPath(path), null); else File.Move(Deployment.IoPath(temporary), Deployment.IoPath(path));
            } finally { if (File.Exists(Deployment.IoPath(temporary))) File.Delete(Deployment.IoPath(temporary)); }
        }
        internal static bool ValidRecord(Dictionary<string, object> record, string key, string fingerprint) {
            return record != null && Str(record, "key") == key && Str(record, "fingerprint") == fingerprint && DateValue(Str(record, "fetchedAt")) != DateTime.MinValue && ValidDetail(Obj(Get(record, "data")), key);
        }
        internal static bool SafeFocusPath(string path) { try { return PlainPath(path) && (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0); } catch { return false; } }
        internal static bool ValidFocus(string pair) { return Regex.IsMatch(pair ?? "", @"^focus-[1-9][0-9]{0,4}-(top|jungle|middle|bottom|support)-[1-5]-[1-5]$"); }
        internal static string FocusPair(string key) { var p = key.Split('-'); return "focus-" + p[1] + "-" + p[2] + "-" + p[4] + "-" + p[5]; }
        internal static string FocusProfile(string pair) { var p = pair.Split('-'); return p[3] + "-" + p[4]; }
        internal static string[] FocusKeys(string pair) { var p = pair.Split('-'); return new[] { "detail-" + p[1] + "-" + p[2] + "-father-" + p[3] + "-" + p[4], "detail-" + p[1] + "-" + p[2] + "-son-" + p[3] + "-" + p[4] }; }
        internal static bool PlainPath(string path) {
            try {
                var full = Path.GetFullPath(path);
                for (var item = new DirectoryInfo(full); item != null; item = item.Parent)
                    if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            } catch { return false; }
        }
        internal static bool SafeLocalPath(string path) {
            return PlainPath(Path.GetDirectoryName(path)) && (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
        }
        internal const long RecordByteLimit = 16 * 1024 * 1024;
        internal static string RecordFileHash(string path) {
            if (!SafeLocalPath(path)) throw new IOException("Unsafe data path");
            using (var file = File.OpenRead(path)) {
                if (file.Length > RecordByteLimit) throw new IOException("Data record exceeds the size limit");
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            }
        }
        internal static string SystemLanguage() { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"; }
    }
}

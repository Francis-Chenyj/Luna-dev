using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Luna {
    // Independent maintenance executable: no browser SDK or running app required.
    internal static class Deployment {
        internal const string Owner = "Luna.Runtime.1";
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        internal static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        internal static string Text(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
        internal static object[] Array(Dictionary<string, object> map, string key) { object value; return map.TryGetValue(key, out value) ? value as object[] ?? new object[0] : new object[0]; }
        // Call only after normal path validation. Extended paths allow I/O even when
        // the machine-wide LongPathsEnabled policy is off; never change that policy.
        internal static string IoPath(string path) {
            return LocalPaths.Io(path);
        }
        internal static bool Plain(string path) {
            try {
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                for (var d = new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path)); d != null; d = d.Parent)
                    if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            } catch { return false; }
        }
        internal static string Under(string root, string relative) {
            if (String.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p == "" || p == "." || p == ".." || p.TrimEnd(' ', '.') != p || System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) throw new IOException("Invalid package path");
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !Plain(path)) throw new IOException("Unsafe package path");
            return path;
        }
        internal static string HashFile(string path) {
            using (var file = File.OpenRead(IoPath(path))) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        internal static Dictionary<string, object> Read(string path) {
            if (!Plain(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Invalid metadata");
            lock (Json) return Map(Json.DeserializeObject(File.ReadAllText(IoPath(path), Encoding.UTF8)));
        }
        internal static void Write(string path, object data) {
            if (!Plain(path)) throw new IOException("Unsafe metadata path");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".luna-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                string text; lock (Json) text = Json.Serialize(data);
                File.WriteAllText(IoPath(temporary), text, new UTF8Encoding(false));
                if (File.Exists(IoPath(path))) File.Replace(IoPath(temporary), IoPath(path), null); else File.Move(IoPath(temporary), IoPath(path));
            } finally { if (File.Exists(IoPath(temporary))) File.Delete(IoPath(temporary)); }
        }
        internal static bool PayloadPath(string relative) {
            string first = relative.Split('/')[0];
            return new[] { "web", "data", "docs", "scripts", "vendor", "runtimes" }.Contains(first) ||
                new[] { "Luna.exe", "Luna.Maintenance.exe", "Luna.exe.config", "Luna.Maintenance.exe.config", "build-provenance.json", "CONTRIBUTING.md", "SECURITY.md", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "README.md", "README.zh-CN.md", "CHANGELOG.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "release-status.json", "启动Luna.cmd", "离线启动Luna.cmd", "创建Luna桌面快捷方式.cmd", "安装Luna.cmd", "卸载Luna.cmd" }.Contains(relative);
        }
        internal static Dictionary<string, object> Manifest(string root, bool verify) {
            var manifest = Read(Path.Combine(root, "installed-files.json"));
            Version version; if (Text(manifest, "owner") != Owner || !Version.TryParse(Text(manifest, "version"), out version)) throw new IOException("Missing Luna ownership manifest");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Array(manifest, "files")) {
                var row = Map(item); string relative = Text(row, "path"), path = Under(root, relative);
                long bytes;
                if (!PayloadPath(relative) || !seen.Add(relative) || !long.TryParse(Text(row, "bytes"), out bytes) || bytes < 0 || bytes > 128 * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(Text(row, "sha256"), "^[a-f0-9]{64}$")) throw new IOException("Invalid ownership entry");
                if (verify && (!File.Exists(path) || new FileInfo(path).Length != bytes || HashFile(path) != Text(row, "sha256"))) throw new IOException("Package integrity check failed: " + relative);
            }
            if (!seen.Contains("Luna.exe") || !seen.Contains("web/index.html") || !seen.Contains("Luna.Maintenance.exe") || seen.Count > 20000) throw new IOException("Incomplete runtime package");
            return manifest;
        }
        internal static bool Managed(string root) {
            try { RequireManaged(root); return true; } catch { return false; }
        }
        internal static void RequireManaged(string root) {
            if (Directory.Exists(Path.Combine(root, ".git")) || Directory.Exists(Path.Combine(root, "src")))
                throw new IOException("源码目录不能安装更新或卸载 / Source folders are protected");
            try { Manifest(root, false); }
            catch (Exception ex) { throw new IOException("程序包清单无法读取或无效。请重新解压完整程序包，保留 cache 数据。 / Cannot read the runtime manifest. Extract a complete package; keep your cache data. " + ex.Message, ex); }
        }
        internal static Dictionary<string, object> VerifyEnvelope(string json) {
            Dictionary<string, object> envelope; lock (Json) envelope = Map(Json.DeserializeObject(json));
            byte[] payload = Convert.FromBase64String(Text(envelope, "payload")), signature = Convert.FromBase64String(Text(envelope, "signature"));
            if (payload.Length > 1024 * 1024) throw new IOException("Update metadata too large");
            using (var rsa = new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(UpdateTrust.PublicKey);
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), signature)) throw new IOException("Update signature is invalid");
            }
            Dictionary<string, object> release; lock (Json) release = Map(Json.DeserializeObject(Encoding.UTF8.GetString(payload)));
            Version version; long size;
            if (Text(release, "product") != "Luna" || Text(release, "schema") != "1" || !Version.TryParse(Text(release, "version"), out version) || version.Revision >= 0 || !long.TryParse(Text(release, "bytes"), out size) || size < 1 || size > 128 * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(Text(release, "sha256"), "^[a-f0-9]{64}$")) throw new IOException("Invalid release metadata");
            Uri uri;
            if (!Uri.TryCreate(Text(release, "url"), UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/Francis-Chenyj/Luna-dev/releases/download/", StringComparison.Ordinal) || !String.IsNullOrEmpty(uri.UserInfo)) throw new IOException("Untrusted release location");
            return release;
        }
        internal static void ExtractVerified(string archive, string destination, Dictionary<string, object> release) {
            if (new FileInfo(archive).Length != long.Parse(Text(release, "bytes")) || HashFile(archive) != Text(release, "sha256")) throw new IOException("Update package checksum mismatch");
            if (!Plain(destination) || Directory.Exists(destination)) throw new IOException("Staging directory already exists");
            Directory.CreateDirectory(destination);
            using (var zip = ZipFile.OpenRead(IoPath(archive))) {
                long total = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (zip.Entries.Count > 20001) throw new IOException("Too many package files");
                foreach (var item in zip.Entries) {
                    if (!item.FullName.StartsWith("Luna/", StringComparison.Ordinal)) throw new IOException("Invalid archive root");
                    string relative = item.FullName.Substring(5);
                    if (!seen.Add(relative) || !(PayloadPath(relative) || relative == "installed-files.json") || (total += item.Length) > 256 * 1024 * 1024 || item.Length > 128 * 1024 * 1024) throw new IOException("Invalid archive entry");
                    string target = Under(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = item.Open()) using (var output = File.Create(IoPath(target))) {
                        byte[] buffer = new byte[32768]; int n; long copied = 0;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { copied += n; if (copied > item.Length) throw new IOException("Archive size limit exceeded"); output.Write(buffer, 0, n); }
                        if (copied != item.Length) throw new IOException("Truncated archive entry");
                    }
                }
                var manifest = Manifest(destination, true);
                if (Text(manifest, "version") != Text(release, "version") || seen.Count != Array(manifest, "files").Length + 1) throw new IOException("Archive contains unlisted files");
            }
        }
        internal static void AtomicCopy(string from, string to) {
            if (!Plain(from) || !Plain(to)) throw new IOException("Unsafe copy");
            Directory.CreateDirectory(Path.GetDirectoryName(to)); string temporary = Path.Combine(Path.GetDirectoryName(to), ".luna-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.Copy(IoPath(from), IoPath(temporary)); if (File.Exists(IoPath(to))) File.Replace(IoPath(temporary), IoPath(to), null); else File.Move(IoPath(temporary), IoPath(to)); }
            finally { if (File.Exists(IoPath(temporary))) File.Delete(IoPath(temporary)); }
        }
        internal static void DeleteOwnedTree(string root) {
            // Only fully manifested staging/backup directories. Unknown files survive.
            var manifest = Manifest(root, false);
            foreach (var item in Array(manifest, "files")) {
                var row = Map(item); string path = Under(root, Text(row, "path"));
                if (File.Exists(IoPath(path)) && HashFile(path) == Text(row, "sha256")) File.Delete(IoPath(path));
            }
            File.Delete(IoPath(Path.Combine(root, "installed-files.json"))); RemoveManifestDirectories(root, manifest);
        }
        static bool MaintenanceIoFailure(Exception ex) {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException;
        }
        static bool EntirelyOwnedTree(string root) {
            try {
                var manifest = Manifest(root, true);
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(root, "installed-files.json") };
                var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
                foreach (var item in Array(manifest, "files")) {
                    string path = Under(root, Text(Map(item), "path")); files.Add(path);
                    for (string parent = Path.GetDirectoryName(path); parent != null && parent != root; parent = Path.GetDirectoryName(parent)) dirs.Add(parent);
                }
                var pending = new Stack<string>(); pending.Push(root);
                while (pending.Count > 0) {
                    string dir = pending.Pop();
                    foreach (string path in Directory.EnumerateFileSystemEntries(dir)) {
                        if (!Plain(path)) return false;
                        if (Directory.Exists(path)) { if (!dirs.Contains(path)) return false; pending.Push(path); }
                        else if (!files.Contains(path)) return false;
                    }
                }
                return true;
            } catch (Exception ex) { if (!MaintenanceIoFailure(ex)) throw; return false; }
        }
        internal static string PreserveUpdateDirectory(string area, string name) {
            if (name != "rollback" && name != "stage") throw new ArgumentException("Unknown update directory");
            string source = Under(area, name), saved = Under(area, name + "-preserved-" + Guid.NewGuid().ToString("N"));
            Directory.Move(source, saved);
            return saved;
        }
        internal static void DiscardUpdateStage(string area) {
            string stage = Under(area, "stage");
            if (!Directory.Exists(stage)) return;
            if (EntirelyOwnedTree(stage)) DeleteOwnedTree(stage);
            else PreserveUpdateDirectory(area, "stage");
        }
        internal static void VerifyPreparedUpdate(string stage, string archive, Dictionary<string, object> release) {
            if (new FileInfo(archive).Length != long.Parse(Text(release, "bytes")) || HashFile(archive) != Text(release, "sha256")) throw new IOException("Prepared archive changed");
            if (Text(Manifest(stage, true), "version") != Text(release, "version")) throw new IOException("Prepared version changed");
            using (var zip = ZipFile.OpenRead(IoPath(archive))) {
                var entry = zip.GetEntry("Luna/installed-files.json");
                if (entry == null) throw new IOException("Prepared ownership manifest is missing");
                using (var input = entry.Open()) using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != HashFile(Path.Combine(stage, "installed-files.json"))) throw new IOException("Prepared ownership manifest changed");
            }
        }
        static void RemoveManifestDirectories(string root, Dictionary<string, object> manifest) {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string boundary = Path.GetFullPath(root).TrimEnd('\\');
            foreach (var item in Array(manifest, "files")) {
                string current = Path.GetDirectoryName(Under(root, Text(Map(item), "path")));
                while (current != null && current.StartsWith(boundary + "\\", StringComparison.OrdinalIgnoreCase)) { directories.Add(current); current = Path.GetDirectoryName(current); }
            }
            foreach (string path in directories.OrderByDescending(p => p.Length)) if (Plain(path) && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
            if (Plain(boundary) && Directory.Exists(boundary) && !Directory.EnumerateFileSystemEntries(boundary).Any()) Directory.Delete(boundary, false);
        }
        internal static bool NeedsRecovery(string root) {
            string path = Path.Combine(root, "cache", "app-update", "transaction.json");
            if (!File.Exists(path)) return false;
            try {
                string state = Text(Read(path), "state");
                if (state == "committed" || state == "rolledBack") return false;
                if (state == "applying") return true;
            } catch (Exception ex) {
                if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw;
            }
            // Never discard a broken transaction and assume a partly replaced app is safe.
            // An intact installation may continue; otherwise only a verified backup may recover it.
            if (!Plain(path) || new FileInfo(path).Length > 4 * 1024 * 1024)
                throw new IOException("升级记录无法安全读取，请保留 cache 并重新解压完整程序包。 / Unsafe update record; keep cache and extract a complete package.");
            bool intact = false;
            try { Manifest(root, true); intact = true; } catch { }
            if (!intact) {
                try { Manifest(Path.Combine(root, "cache", "app-update", "rollback"), true); }
                catch (Exception ex) { throw new IOException("升级记录损坏，当前程序和备份均未通过完整性校验。请保留 cache，在新文件夹解压完整程序包后迁移数据。 / Update record is damaged and neither installation nor backup verifies. Extract a complete package in a new folder and retain cache for recovery.", ex); }
            }
            string evidence = "transaction.invalid-" + HashFile(path) + ".json";
            string preserved = Path.Combine(Path.GetDirectoryName(path), evidence);
            if (!File.Exists(preserved)) AtomicCopy(path, preserved);
            else if (!Plain(preserved) || HashFile(preserved) != HashFile(path)) throw new IOException("Recovery evidence could not be preserved");
            Write(Path.Combine(Path.GetDirectoryName(path), "result.json"), new { state = "recovered", notice = intact ? "verifiedInstallation" : "restoringBackup", evidence = evidence, at = DateTime.UtcNow.ToString("o") });
            Write(path, new { state = intact ? "committed" : "applying", added = new string[0], recoveredMetadata = true });
            return !intact;
        }
        internal static void Restore(string root) {
            string journalPath = Path.Combine(root, "cache", "app-update", "transaction.json");
            if (!File.Exists(journalPath)) return;
            if (!NeedsRecovery(root)) return;
            var job = Read(journalPath);
            if (Text(job, "state") == "committed" || Text(job, "state") == "rolledBack") return;
            string backup = Path.Combine(root, "cache", "app-update", "rollback");
            var old = Manifest(backup, true);
            var addedHashes = Map(job.ContainsKey("addedHashes") ? job["addedHashes"] : null);
            foreach (var relative in Array(job, "added").Select(Convert.ToString)) {
                string path = Under(root, relative), expected = Text(addedHashes, relative);
                // Never erase a newly added file that the user changed during a failed update.
                if (PayloadPath(relative) && File.Exists(IoPath(path)) && expected.Length == 64 && HashFile(path) == expected) File.Delete(IoPath(path));
            }
            foreach (var item in Array(old, "files")) { string relative = Text(Map(item), "path"); AtomicCopy(Under(backup, relative), Under(root, relative)); }
            AtomicCopy(Path.Combine(backup, "installed-files.json"), Path.Combine(root, "installed-files.json"));
            job["state"] = "rolledBack"; Write(journalPath, job);
            if (job.ContainsKey("recoveredMetadata")) Write(Path.Combine(root, "cache", "app-update", "result.json"), new { state = "recovered", notice = "restoredBackup", at = DateTime.UtcNow.ToString("o") });
        }
        internal static void Apply(string root, string stage, string envelopePath, Func<string, bool> healthCheck) {
            RequireManaged(root);
            var release = VerifyEnvelope(File.ReadAllText(IoPath(envelopePath), Encoding.UTF8));
            var old = Manifest(root, true); var incoming = Manifest(stage, true);
            if (Text(incoming, "version") != Text(release, "version") || new Version(Text(incoming, "version")) <= new Version(Text(old, "version"))) throw new IOException("Update is not newer than this installation");
            string rootStage = Path.GetFullPath(Path.Combine(root, "cache", "app-update", "stage"));
            if (!Path.GetFullPath(stage).Equals(rootStage, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected staging directory");
            // Bind the extracted files to the authenticated ZIP a second time in the helper.
            string archive = Path.Combine(root, "cache", "app-update", "package.zip");
            if (HashFile(archive) != Text(release, "sha256")) throw new IOException("Staged archive changed");
            using (var zip = ZipFile.OpenRead(IoPath(archive))) {
                foreach (var entry in zip.Entries) {
                    string relative = entry.FullName.StartsWith("Luna/") ? entry.FullName.Substring(5) : "";
                    string local = Under(stage, relative);
                    using (var stream = entry.Open()) using (var sha = SHA256.Create()) {
                        if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != HashFile(local)) throw new IOException("Staged files changed");
                    }
                }
            }
            string area = Path.Combine(root, "cache", "app-update"), backup = Path.Combine(area, "rollback");
            // A crash during backup creation must not poison the next attempt. Keep
            // the previous directory intact until the replacement backup verifies.
            string previousBackup = Directory.Exists(backup) ? PreserveUpdateDirectory(area, "rollback") : null;
            Directory.CreateDirectory(backup);
            AtomicCopy(Path.Combine(root, "installed-files.json"), Path.Combine(backup, "installed-files.json"));
            foreach (var item in Array(old, "files")) { string relative = Text(Map(item), "path"); AtomicCopy(Under(root, relative), Under(backup, relative)); }
            Manifest(backup, true);
            var oldPaths = new HashSet<string>(Array(old, "files").Select(x => Text(Map(x), "path")), StringComparer.OrdinalIgnoreCase);
            var newPaths = new HashSet<string>(Array(incoming, "files").Select(x => Text(Map(x), "path")), StringComparer.OrdinalIgnoreCase);
            var added = newPaths.Where(p => !oldPaths.Contains(p)).ToArray();
            if (added.Any(p => File.Exists(Under(root, p)))) throw new IOException("An update would overwrite an unowned file");
            string journal = Path.Combine(area, "transaction.json");
            var addedHashes = Array(incoming, "files").Select(Map).Where(x => added.Contains(Text(x, "path"))).ToDictionary(x => Text(x, "path"), x => (object)Text(x, "sha256"));
            Write(journal, new { state = "applying", added = added, addedHashes = addedHashes });
            try {
                foreach (var item in Array(incoming, "files").OrderBy(x => Text(Map(x), "path") == "Luna.exe" ? 1 : 0)) {
                    string relative = Text(Map(item), "path"); AtomicCopy(Under(stage, relative), Under(root, relative));
                }
                foreach (string relative in oldPaths.Except(newPaths, StringComparer.OrdinalIgnoreCase)) File.Delete(IoPath(Under(root, relative)));
                AtomicCopy(Path.Combine(stage, "installed-files.json"), Path.Combine(root, "installed-files.json"));
                if (!healthCheck(root)) throw new IOException("The new version did not pass its startup query check");
                Write(journal, new { state = "committed", added = added, version = Text(incoming, "version"), at = DateTime.UtcNow.ToString("o") });
            } catch { Restore(root); Write(Path.Combine(area, "result.json"), new { state = "rolledBack", at = DateTime.UtcNow.ToString("o") }); throw; }
            // Commit is the transaction boundary. A locked cache file or receipt must
            // never turn an installed version into a false rollback report.
            bool cleanupPending = false;
            try {
                DeleteOwnedTree(stage); if (File.Exists(IoPath(archive))) File.Delete(IoPath(archive));
                foreach (string saved in Directory.EnumerateDirectories(area, "rollback-preserved-*")) {
                    if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(saved), "^rollback-preserved-[a-f0-9]{32}$") && EntirelyOwnedTree(saved)) DeleteOwnedTree(saved);
                }
                if (previousBackup != null && !Directory.Exists(previousBackup)) previousBackup = null;
            } catch (Exception ex) { if (!MaintenanceIoFailure(ex)) throw; cleanupPending = true; }
            try { Write(Path.Combine(area, "result.json"), new { state = "installed", version = Text(incoming, "version"), cleanupPending = cleanupPending, preservedBackup = previousBackup == null ? "" : Path.GetFileName(previousBackup), at = DateTime.UtcNow.ToString("o") }); }
            catch (Exception ex) { if (!MaintenanceIoFailure(ex)) throw; } // The committed journal remains the durable receipt.
        }
        static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        static string RegistryName(string root) {
            using (var sha = SHA256.Create()) return "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Luna-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant()))).Replace("-", "").Substring(0, 20);
        }
        static void Shortcut(string path, string target, string directory) {
            Type type = Type.GetTypeFromProgID("WScript.Shell"); dynamic shell = Activator.CreateInstance(type), link = shell.CreateShortcut(path);
            link.TargetPath = target; link.Arguments = "--launch"; link.WorkingDirectory = directory; link.IconLocation = Path.Combine(directory, "web", "app.ico"); link.Description = "Luna · FrancisChen"; link.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        internal static void RegisterInstallation(string root) {
            var manifest = Manifest(root, true);
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryName(root))) {
                key.SetValue("DisplayName", "Luna"); key.SetValue("DisplayVersion", Text(manifest, "version")); key.SetValue("Publisher", "FrancisChen");
                key.SetValue("InstallLocation", root); key.SetValue("DisplayIcon", Path.Combine(root, "Luna.exe"));
                key.SetValue("UninstallString", Quote(Path.Combine(root, "Luna.Maintenance.exe")) + " --uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
            }
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Luna.lnk");
            // Never replace another installation's shortcut.
            if (!File.Exists(shortcut)) Shortcut(shortcut, Path.Combine(root, "Luna.Maintenance.exe"), root);
            Write(Path.Combine(root, "cache", "installation.json"), new { owner = Owner, kind = "installed", root = root });
        }
        internal static void Install(string source, string target) {
            var manifest = Manifest(source, true);
            string full = Path.GetFullPath(target).TrimEnd('\\');
            if (!Plain(full) || full.Length < 4 || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())) throw new IOException("Choose a new empty installation folder");
            Directory.CreateDirectory(full);
            foreach (var item in Array(manifest, "files")) { string relative = Text(Map(item), "path"); AtomicCopy(Under(source, relative), Under(full, relative)); }
            AtomicCopy(Path.Combine(source, "installed-files.json"), Path.Combine(full, "installed-files.json"));
            RegisterInstallation(full);
        }
        internal static int Uninstall(string root, bool removeData) {
            RequireManaged(root);
            var manifest = Manifest(root, false); int kept = 0;
            foreach (var item in Array(manifest, "files")) {
                var row = Map(item); string path = Under(root, Text(row, "path"));
                if (!File.Exists(path)) continue;
                if (HashFile(path) != Text(row, "sha256")) { kept++; continue; }
                File.Delete(IoPath(path));
            }
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) }) {
                string path = Path.Combine(folder, "Luna.lnk"); if (!File.Exists(path)) continue;
                Type type = Type.GetTypeFromProgID("WScript.Shell"); dynamic shell = Activator.CreateInstance(type), link = shell.CreateShortcut(path);
                string target = link.TargetPath;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                if (target.Equals(Path.Combine(root, "Luna.exe"), StringComparison.OrdinalIgnoreCase) || target.Equals(Path.Combine(root, "Luna.Maintenance.exe"), StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryName(root))) {
                bool owned = key != null && String.Equals(Convert.ToString(key.GetValue("InstallLocation")), root, StringComparison.OrdinalIgnoreCase);
                if (owned) { key.Close(); Registry.CurrentUser.DeleteSubKeyTree(RegistryName(root), false); }
            }
            // The cache is exclusively runtime-owned; unknown files outside it survive.
            // No links are followed. A full path check precedes every directory removal.
            if (removeData) RemoveCache(Path.Combine(root, "cache"), Path.GetFullPath(Path.Combine(root, "cache")));
            else RemoveCache(Path.Combine(root, "cache", "app-update"), Path.GetFullPath(Path.Combine(root, "cache", "app-update")));
            File.Delete(IoPath(Path.Combine(root, "installed-files.json"))); RemoveManifestDirectories(root, manifest); return kept;
        }
        static void RemoveCache(string path, string boundary) {
            string full = Path.GetFullPath(path);
            if (!(full == boundary || full.StartsWith(boundary + "\\", StringComparison.OrdinalIgnoreCase)) || !Plain(full) || !Directory.Exists(full)) return;
            foreach (string file in Directory.EnumerateFiles(full)) if (Plain(file)) File.Delete(IoPath(file));
            foreach (string dir in Directory.EnumerateDirectories(full)) RemoveCache(dir, boundary);
            if (!Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full, false);
        }
        internal static bool HealthCheck(string root) {
            int port = 0;
            string probeToken = Guid.NewGuid().ToString("N");
            string probeFile = Path.Combine(root, "cache", "app-update", "probe-" + probeToken + ".json");
            // Let the child bind an ephemeral port atomically, then authenticate its
            // published address. A free-port preflight has a bind/release race.
            var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "Luna.exe"), "--offline --maintenance-probe --desktop-probe --probe-token " + probeToken + " --port 0") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root });
            bool ownedPort = false;
            try {
                var watch = Stopwatch.StartNew(); string lastError = "Process exited before readiness";
                while (!process.HasExited && watch.ElapsedMilliseconds < 30000) {
                    try {
                        if (port == 0) {
                            var address = Read(probeFile); int candidate;
                            if (Text(address, "token") != probeToken || Text(address, "pid") != process.Id.ToString() || !int.TryParse(Text(address, "port"), out candidate) || candidate < 1024 || candidate > 65535) throw new IOException("Unexpected health-check address");
                            port = candidate;
                        }
                        string expected = Text(Manifest(root, false), "version");
                        using (var client = new ProbeClient { Proxy = null }) {
                            client.Headers["X-Luna"] = "1";
                            var health = Map(Json.DeserializeObject(Encoding.UTF8.GetString(client.DownloadData("http://127.0.0.1:" + port + "/api/health"))));
                            if (Text(health, "version") != expected) throw new IOException("Unexpected running version: " + Text(health, "version"));
                            if (Text(health, "probeToken") != probeToken) throw new IOException("Unexpected health-check process");
                            ownedPort = true;
                            var window = Map(Json.DeserializeObject(Encoding.UTF8.GetString(client.DownloadData("http://127.0.0.1:" + port + "/api/window"))));
                            if (Text(window, "ready") != "True" || Text(window, "uiReady") != "True") throw new IOException("Native window or application JavaScript is not ready");
                            var catalog = Map(Json.DeserializeObject(Encoding.UTF8.GetString(client.DownloadData("http://127.0.0.1:" + port + "/api/catalog"))));
                            if (Array(Map(catalog["data"]), "heroes").Length == 0) throw new IOException("Empty health-check catalog");
                            var hero = Map(Array(Map(catalog["data"]), "heroes")[0]); string lane = Map(hero["lanes"]).Keys.First();
                            var detail = Map(Json.DeserializeObject(Encoding.UTF8.GetString(client.DownloadData("http://127.0.0.1:" + port + "/api/detail?championId=" + Text(hero, "id") + "&lane=" + lane + "&mode=father&thresholdLevel=1&sensitivity=5"))));
                            return detail.ContainsKey("data") && detail["data"] != null;
                        }
                    } catch (Exception ex) { lastError = ex.GetType().Name + ": " + ex.Message; Thread.Sleep(100); }
                }
                Write(Path.Combine(root, "cache", "app-update", "health-failure.json"), new { at = DateTime.UtcNow.ToString("o"), reason = lastError.Substring(0, Math.Min(500, lastError.Length)) });
                return false;
            } finally {
                try { if (ownedPort) using (var client = new ProbeClient { Proxy = null }) { client.Headers["X-Luna"] = "1"; client.UploadData("http://127.0.0.1:" + port + "/api/quit", "POST", new byte[0]); } } catch { }
                if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(3000); } process.Dispose();
                try { if (File.Exists(probeFile) && Plain(probeFile)) File.Delete(probeFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        sealed class ProbeClient : WebClient {
            protected override WebRequest GetWebRequest(Uri address) { var request = base.GetWebRequest(address); request.Timeout = 1500; return request; }
        }
        static void WaitForExit(string root, int pid) {
            try { using (var process = Process.GetProcessById(pid)) if (!process.WaitForExit(30000)) throw new IOException("Luna is still running"); } catch (ArgumentException) { }
            foreach (var process in Process.GetProcessesByName("Luna")) using (process) {
                try { if (String.Equals(process.MainModule.FileName, Path.Combine(root, "Luna.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Another Luna window is still running. Exit it first."); } catch (System.ComponentModel.Win32Exception) { }
            }
        }
        internal static void StartWorker(string root, string action, bool removeData, int pid) {
            var manifest = Manifest(root, false);
            var helper = Array(manifest, "files").Select(Map).First(x => Text(x, "path") == "Luna.Maintenance.exe");
            if (HashFile(Path.Combine(root, "Luna.Maintenance.exe")) != Text(helper, "sha256")) throw new IOException("Maintenance executable integrity check failed");
            string folder = Path.Combine(Path.GetTempPath(), "Luna-maintenance-" + Guid.NewGuid().ToString("N"));
            // Reuse the already-owned runtime config, keeping packages compatible with 1.6.x allowlists.
            var config = Array(manifest, "files").Select(Map).FirstOrDefault(x => Text(x, "path") == "Luna.exe.config");
            if (config == null || HashFile(Under(root, "Luna.exe.config")) != Text(config, "sha256")) throw new IOException("Maintenance configuration is missing or damaged. Extract a complete runtime package.");
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "Luna.Maintenance.exe"); File.Copy(IoPath(Path.Combine(root, "Luna.Maintenance.exe")), IoPath(exe));
            File.Copy(IoPath(Under(root, "Luna.exe.config")), IoPath(exe + ".config"));
            string job = Path.Combine(folder, "job.json"); Write(job, new { root = Path.GetFullPath(root).TrimEnd('\\'), action = action, removeData = removeData, pid = pid });
            // .NET Framework direct CreateProcess may inherit the HTTP listener.
            // An inherited socket keeps AcceptTcpClient alive while the worker waits
            // for its parent: use the shell launch to avoid that circular wait.
            Process.Start(new ProcessStartInfo(exe, "--job " + Quote(job)) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        [STAThread] internal static int Main(string[] args) {
            Application.EnableVisualStyles(); string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            try {
                if (args.Length == 1 && args[0] == "--verify") { RequireManaged(root); Manifest(root, true); return 0; }
                if (args.Length == 2 && args[0] == "--job") {
                    var job = Read(args[1]); root = Text(job, "root"); WaitForExit(root, int.Parse(Text(job, "pid")));
                    string action = Text(job, "action");
                    using (var mutex = new Mutex(false, "Local\\LunaMaintenance_" + RegistryName(root).Split('-').Last())) {
                        bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("Another maintenance operation is running");
                        try {
                            if (action == "update") {
                                try { Apply(root, Path.Combine(root, "cache", "app-update", "stage"), Path.Combine(root, "cache", "app-update", "release.json"), HealthCheck); }
                                catch { Process.Start(Path.Combine(root, "Luna.exe")); throw; }
                                using (var key = Registry.CurrentUser.OpenSubKey(RegistryName(root))) { if (key != null) { key.Close(); RegisterInstallation(root); } }
                                Process.Start(Path.Combine(root, "Luna.exe"));
                            } else if (action == "recover") { Restore(root); Process.Start(Path.Combine(root, "Luna.exe")); }
                            else if (action == "uninstall") {
                                int kept = Uninstall(root, Text(job, "removeData").Equals("True", StringComparison.OrdinalIgnoreCase));
                                MessageBox.Show(kept == 0 ? "Luna 已卸载。选择保留的数据及额外文件仍在原目录。\nLuna was removed. Retained data and extra files remain in the original folder." : "Luna 已卸载；修改过的程序文件已保留。\nLuna was removed; modified files were preserved.", "Luna");
                            } else throw new IOException("Unknown operation");
                        } finally { mutex.ReleaseMutex(); }
                    }
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--install") {
                    using (var dialog = new FolderBrowserDialog { Description = "选择新的空文件夹安装 Luna / Choose a new empty folder for Luna", ShowNewFolderButton = true }) {
                        if (dialog.ShowDialog() != DialogResult.OK) return 0;
                        Install(root, dialog.SelectedPath); Process.Start(Path.Combine(dialog.SelectedPath, "Luna.exe")); return 0;
                    }
                }
                if (args.Length == 1 && args[0] == "--uninstall") {
                    RequireManaged(root);
                    if (MessageBox.Show("卸载此目录的 Luna？\nRemove Luna from this folder?\n" + root, "Luna", MessageBoxButtons.OKCancel) != DialogResult.OK) return 0;
                    bool remove = MessageBox.Show("同时删除设置、离线数据和最近查询？\nAlso delete settings, offline data and recent queries?", "Luna", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                    StartWorker(root, "uninstall", remove, Process.GetCurrentProcess().Id); return 0;
                }
                if (args.Length == 1 && args[0] == "--launch") { if (NeedsRecovery(root)) StartWorker(root, "recover", false, Process.GetCurrentProcess().Id); else Process.Start(Path.Combine(root, "Luna.exe")); return 0; }
                throw new IOException("Use --install, --uninstall or --launch");
            } catch (Exception ex) {
                if (args.Length == 1 && args[0] == "--verify") { Console.Error.WriteLine(ex.Message); return 1; }
                MessageBox.Show("Luna 操作未完成，原有数据保留。\nOperation did not complete; existing data is retained.\n" + ex.Message, "Luna", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1;
            } finally {
                if (args.Length == 2 && args[0] == "--job") try { ScheduleWorkerCleanup(args[1]); } catch { }
            }
        }
        static void ScheduleWorkerCleanup(string job) {
            // Delete only this isolated worker's executable, config and job after it exits.
            string folder = Path.GetDirectoryName(job), exe = Path.Combine(folder, "Luna.Maintenance.exe");
            if (!folder.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("Luna-maintenance-") || !Plain(folder)) return;
            string escaped = folder.Replace("'", "''"), script = "$p = " + Process.GetCurrentProcess().Id + "; Wait-Process -Id $p -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '" + exe.Replace("'", "''") + "','" + (exe + ".config").Replace("'", "''") + "','" + job.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue; if (-not (Get-ChildItem -LiteralPath '" + escaped + "' -Force)) { Remove-Item -LiteralPath '" + escaped + "' -Force }";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"), "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
    }
}

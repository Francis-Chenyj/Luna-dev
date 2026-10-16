using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace Luna {
    internal sealed class QuarantineStore {
        readonly RuntimeOptions options;
        internal QuarantineStore(RuntimeOptions options) {
            this.options = options;
        }
        readonly object SpaceSync = new object();
        long ExtraFreed;
        string QuarantineDir { get { return Path.Combine(options.CacheDir, "quarantine"); } }
        bool OwnedExceptionalFile(string path) {
            string parent = Path.GetFullPath(Path.GetDirectoryName(path)).TrimEnd('\\'), name = Path.GetFileName(path);
            if (parent.Equals(Path.GetFullPath(options.FocusDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return name.EndsWith(".json.gz", StringComparison.Ordinal) && DataFormat.ValidFocus(name.Substring(0, name.Length - 8));
            return parent.Equals(Path.GetFullPath(options.CacheDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                (name == "settings.json" || Regex.IsMatch(name, @"^settings\.invalid-[a-f0-9]{32}\.json$"));
        }
        internal void QuarantineOwnedFile(string path) {
            lock (SpaceSync) {
                try {
                    if (!OwnedExceptionalFile(path) || !DataFormat.SafeLocalPath(path) || !File.Exists(path) || !DataFormat.PlainPath(QuarantineDir)) return;
                    Directory.CreateDirectory(QuarantineDir);
                    string target = Path.Combine(QuarantineDir, "luna-" + Guid.NewGuid().ToString("N") + ".bad");
                    File.Move(path, target); File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                    PruneQuarantine();
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        void PruneQuarantine() {
            if (!DataFormat.PlainPath(QuarantineDir) || !Directory.Exists(QuarantineDir)) return;
            var files = Directory.EnumerateFiles(QuarantineDir).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^luna-[a-f0-9]{32}\.bad$") && DataFormat.SafeLocalPath(p))
                .Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long kept = 0; int count = 0;
            foreach (var file in files) {
                if (file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7) && count < 20 && kept + file.Length <= 10 * 1024 * 1024) { count++; kept += file.Length; continue; }
                try { long bytes = file.Length; File.Delete(file.FullName); Interlocked.Add(ref ExtraFreed, bytes); } catch { }
            }
        }
        internal void MaintainExceptionalFiles() {
            lock (SpaceSync) {
                if (!DataFormat.PlainPath(options.CacheDir) || !Directory.Exists(options.CacheDir)) return;
                foreach (string path in Directory.EnumerateFiles(options.CacheDir, "settings.invalid-*.json"))
                    if (OwnedExceptionalFile(path)) QuarantineOwnedFile(path);
                PruneQuarantine();
                foreach (string path in Directory.EnumerateFiles(options.CacheDir, "*.tmp")) {
                    if (!DataFormat.ShortTempName(Path.GetFileName(path)) || !DataFormat.SafeLocalPath(path) || File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-1)) continue;
                    try { long bytes = new FileInfo(path).Length; File.Delete(path); Interlocked.Add(ref ExtraFreed, bytes); } catch { }
                }
            }
        }
        internal long FreedBytes { get { return Interlocked.Read(ref ExtraFreed); } }
    }
}

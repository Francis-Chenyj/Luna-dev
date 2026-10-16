using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Luna {
    // Internal filesystem adapter, not an input validator. Ownership/reparse checks
    // stay at callers and always receive ordinary absolute paths. Do not enable a
    // machine-wide policy just to support deep portable installations.
    internal static class LocalPaths {
        internal static string Normal(string path) {
            if (path != null && path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            return path != null && path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }
        internal static string Io(string path) {
            if (path == null || path.Length < 248 || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            string full = Path.GetFullPath(path);
            return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }
    }
    internal static class LocalFile {
        internal static bool Exists(string p) { return File.Exists(LocalPaths.Io(p)); }
        internal static FileAttributes GetAttributes(string p) { return File.GetAttributes(LocalPaths.Io(p)); }
        internal static DateTime GetLastWriteTimeUtc(string p) { return File.GetLastWriteTimeUtc(LocalPaths.Io(p)); }
        internal static void SetLastWriteTimeUtc(string p, DateTime time) { File.SetLastWriteTimeUtc(LocalPaths.Io(p), time); }
        // Writers publish with atomic replacement. Readers must permit that rename
        // while their open handle continues to expose the previous complete file.
        internal static FileStream OpenRead(string p) { return new FileStream(LocalPaths.Io(p), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        internal static FileStream Create(string p) { return File.Create(LocalPaths.Io(p)); }
        internal static byte[] ReadAllBytes(string p) { using (var input = OpenRead(p)) using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); } }
        internal static string ReadAllText(string p) { return ReadAllText(p, Encoding.UTF8); }
        internal static string ReadAllText(string p, Encoding encoding) { using (var input = OpenRead(p)) using (var reader = new StreamReader(input, encoding, true)) return reader.ReadToEnd(); }
        internal static void WriteAllText(string p, string text) { File.WriteAllText(LocalPaths.Io(p), text); }
        internal static void WriteAllText(string p, string text, Encoding encoding) { File.WriteAllText(LocalPaths.Io(p), text, encoding); }
        internal static void AppendAllText(string p, string text, Encoding encoding) { File.AppendAllText(LocalPaths.Io(p), text, encoding); }
        internal static void Delete(string p) { File.Delete(LocalPaths.Io(p)); }
        internal static void Copy(string source, string target) { File.Copy(LocalPaths.Io(source), LocalPaths.Io(target)); }
        internal static void Move(string source, string target) { File.Move(LocalPaths.Io(source), LocalPaths.Io(target)); }
        internal static void Replace(string source, string target, string backup) { File.Replace(LocalPaths.Io(source), LocalPaths.Io(target), LocalPaths.Io(backup)); }
    }
    internal static class LocalDirectory {
        internal static bool Exists(string p) { return Directory.Exists(LocalPaths.Io(p)); }
        internal static void CreateDirectory(string p) { Directory.CreateDirectory(LocalPaths.Io(p)); }
        internal static void Delete(string p, bool recursive) { Directory.Delete(LocalPaths.Io(p), recursive); }
        internal static void Move(string source, string target) { Directory.Move(LocalPaths.Io(source), LocalPaths.Io(target)); }
        internal static DateTime GetLastWriteTimeUtc(string p) { return Directory.GetLastWriteTimeUtc(LocalPaths.Io(p)); }
        internal static IEnumerable<string> EnumerateFiles(string p, string pattern = "*") { return Directory.EnumerateFiles(LocalPaths.Io(p), pattern).Select(LocalPaths.Normal); }
        internal static IEnumerable<string> EnumerateDirectories(string p, string pattern = "*") { return Directory.EnumerateDirectories(LocalPaths.Io(p), pattern).Select(LocalPaths.Normal); }
        internal static IEnumerable<string> EnumerateFileSystemEntries(string p) { return Directory.EnumerateFileSystemEntries(LocalPaths.Io(p)).Select(LocalPaths.Normal); }
        internal static string[] GetFiles(string p, string pattern = "*") { return EnumerateFiles(p, pattern).ToArray(); }
        internal static string[] GetDirectories(string p) { return EnumerateDirectories(p).ToArray(); }
        internal static string[] GetFileSystemEntries(string p) { return EnumerateFileSystemEntries(p).ToArray(); }
    }
    internal sealed class LocalFileInfo {
        readonly FileInfo info;
        internal LocalFileInfo(string path) { info = new FileInfo(LocalPaths.Io(path)); }
        internal long Length { get { return info.Length; } }
        internal string FullName { get { return LocalPaths.Normal(info.FullName); } }
        internal string Name { get { return info.Name; } }
        internal DateTime LastWriteTimeUtc { get { return info.LastWriteTimeUtc; } }
    }
    internal sealed class LocalDirectoryInfo {
        readonly DirectoryInfo info;
        internal LocalDirectoryInfo(string path) { info = new DirectoryInfo(LocalPaths.Io(path)); }
        internal bool Exists { get { return info.Exists; } }
        internal FileAttributes Attributes { get { return info.Attributes; } }
        internal LocalDirectoryInfo Parent { get { return info.Parent == null ? null : new LocalDirectoryInfo(info.Parent.FullName); } }
    }
}

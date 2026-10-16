using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

namespace Luna {
    internal sealed class DiagnosticLog {
        readonly RuntimeOptions options;
        internal DiagnosticLog(RuntimeOptions options) {
            this.options = options;
        }
        readonly object LogSync = new object();
        readonly object DiagnosticSync = new object();
        readonly Dictionary<string, int> DiagnosticErrors = new Dictionary<string, int>();
        internal void Log(Exception ex) {
            RecordDiagnostic(ex);
            try { lock (LogSync) {
                if (!DataFormat.PlainPath(options.LogDirectory)) return;
                Directory.CreateDirectory(options.LogDirectory);
                string path = Path.Combine(options.LogDirectory, "error.log");
                if (!DataFormat.SafeLocalPath(path)) return;
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, DataFormat.Stamp() + " " + ex.Message + Environment.NewLine, Encoding.UTF8);
            } } catch { }
        }
        internal void RecordDiagnostic(Exception error) {
            // Deliberately never retain exception text, paths, account details,
            // request URLs, headers, credentials or stack traces in diagnostics.
            var ex = error.GetBaseException();
            string category = ex is System.Net.WebException || ex is System.Net.Sockets.SocketException ? "network" :
                ex is UnauthorizedAccessException ? "permission" : ex is FormatException || ex is InvalidDataException || ex is ArgumentException ? "data" :
                ex is IOException ? "storage" : "internal";
            lock (DiagnosticSync) { int count; DiagnosticErrors.TryGetValue(category, out count); DiagnosticErrors[category] = Math.Min(9999, count + 1); }
        }
        internal object Errors { get { lock (DiagnosticSync) return DiagnosticErrors.OrderBy(pair => pair.Key).Select(pair => new { category = pair.Key, count = pair.Value }).ToArray(); } }
    }
}

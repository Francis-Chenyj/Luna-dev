using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        const string AppVersion = "1.6.3";
        static string SettingsFile { get { return Path.Combine(CacheDir, "settings.json"); } }
        static string LogDirectory { get { return Path.Combine(CacheDir, "logs"); } }
        static bool CommandLineDisablesUpdates;
        static readonly object DiagnosticSync = new object();
        static readonly Dictionary<string, int> DiagnosticErrors = new Dictionary<string, int>();

        sealed class Preferences {
            public int schema = 1;
            public string language = "system", theme = "system", closeAction = "ask";
            public bool autoUpdate = true;
            public bool appAutoCheck = true, appAutoDownload = false;
        }
        sealed class SettingsException : ArgumentException {
            public readonly string Code;
            public SettingsException(string code) : base(code) { Code = code; }
        }
        static bool SafeLocalPath(string path) {
            return PlainPath(Path.GetDirectoryName(path)) && (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
        }
        static Preferences ReadPreferences(out string warning) {
            warning = "";
            try {
                if (!SafeLocalPath(SettingsFile)) throw new IOException();
                if (!File.Exists(SettingsFile)) return new Preferences();
                if (new FileInfo(SettingsFile).Length > 16384) throw new IOException();
                var record = Json().DeserializeObject(File.ReadAllText(SettingsFile, Encoding.UTF8)) as Dictionary<string, object>;
                if (record == null) throw new IOException();
                if (Str(record, "schema") != "1") { warning = "settings.unsupported"; return new Preferences { autoUpdate = false, appAutoCheck = false }; }
                string language = Str(record, "language"), theme = Str(record, "theme"), closeAction = Str(record, "closeAction", "ask");
                if (!new[] { "system", "zh-CN", "en" }.Contains(language) || !new[] { "system", "dark", "light" }.Contains(theme) || !new[] { "ask", "tray", "exit" }.Contains(closeAction) || !(Get(record, "autoUpdate") is bool)) throw new IOException();
                bool check = Get(record, "appAutoCheck", true).Equals(true);
                // Respect an old explicit opt-out; a legacy download-only preference
                // must not silently turn networking back on during migration.
                return new Preferences { language = language, theme = theme, closeAction = closeAction, autoUpdate = (bool)record["autoUpdate"],
                    appAutoCheck = check, appAutoDownload = check && Get(record, "appAutoDownload", false).Equals(true) };
            } catch { warning = "settings.unreadable"; return new Preferences { autoUpdate = false, appAutoCheck = false }; }
        }
        static string SystemLanguage() { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"; }
        static string RunMode() { return Offline ? "offline" : CommandLineDisablesUpdates ? "manual" : "normal"; }
        static object SettingsInfo() {
            string warning; var preferences = ReadPreferences(out warning);
            return new { success = true, preferences = preferences, warning = warning, systemLanguage = SystemLanguage(), version = AppVersion,
                mode = RunMode(), desktopWindowAvailable = Desktop != null, automaticUpdatesThisRun = !Offline && AutomaticUpdatesEnabled };
        }
        // Update one key under a folder-specific cross-process lock. Tabs and launch
        // modes share preferences, without a stale form overwriting another choice.
        static object SavePreference(string key, string value) {
            if (key == "language" && !new[] { "system", "zh-CN", "en" }.Contains(value) ||
                key == "theme" && !new[] { "system", "dark", "light" }.Contains(value) ||
                key == "closeAction" && !new[] { "ask", "tray", "exit" }.Contains(value) ||
                new[] { "autoUpdate", "appAutoCheck", "appAutoDownload" }.Contains(key) && value != "true" && value != "false" ||
                !new[] { "language", "theme", "autoUpdate", "closeAction", "appAutoCheck", "appAutoDownload" }.Contains(key)) throw new SettingsException("settings.invalid");
            bool updateChanged = false;
            using (var mutex = new Mutex(false, "Local\\LunaSettings_" + Hash(Root.ToLowerInvariant()).Substring(0, 24))) {
                bool acquired;
                try { acquired = mutex.WaitOne(2000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new SettingsException("settings.busy");
                try {
                    string warning; var preferences = ReadPreferences(out warning);
                    if (warning == "settings.unsupported") throw new SettingsException(warning);
                    if (!SafeLocalPath(SettingsFile)) throw new SettingsException("settings.saveFailed");
                    Directory.CreateDirectory(CacheDir);
                    if (key == "language") preferences.language = value;
                    if (key == "theme") preferences.theme = value;
                    if (key == "closeAction") preferences.closeAction = value;
                    if (key == "autoUpdate") preferences.autoUpdate = value == "true";
                    if (key == "appAutoCheck") {
                        updateChanged = preferences.appAutoCheck != (value == "true");
                        preferences.appAutoCheck = value == "true";
                        if (!preferences.appAutoCheck) preferences.appAutoDownload = false;
                    }
                    if (key == "appAutoDownload") {
                        updateChanged = preferences.appAutoDownload != (value == "true");
                        preferences.appAutoDownload = value == "true";
                        if (preferences.appAutoDownload) preferences.appAutoCheck = true;
                    }
                    // Keep an unreadable file for local recovery before replacing it.
                    if (warning != "" && File.Exists(SettingsFile)) QuarantineOwnedFile(SettingsFile);
                    WriteRecord(SettingsFile, preferences);
                    if (Desktop != null) Desktop.RefreshAppearance();
                } catch (SettingsException) { throw; }
                catch (Exception ex) { RecordDiagnostic(ex); throw new SettingsException("settings.saveFailed"); }
                finally { mutex.ReleaseMutex(); }
            }
            // Schedule only after the complete coupled preference is durably saved,
            // and outside the settings mutex. Network work never blocks this request.
            if (updateChanged) AppUpdatePreferenceChanged(key, value == "true");
            return SettingsInfo();
        }
        static string FolderFor(string kind) {
            string path = kind == "logs" ? LogDirectory : kind == "data" ? CacheDir : kind == "updates" ? AppUpdateDir : null;
            if (path == null) throw new SettingsException("folder.invalid");
            if (!PlainPath(path)) throw new SettingsException("folder.unavailable");
            return path;
        }
        static void OpenLocalFolder(string kind) {
            string path = FolderFor(kind);
            try {
                Directory.CreateDirectory(path);
                // Only these two fixed directories can be opened; never execute a
                // user-supplied path, shell command, URL or file.
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + path + "\"") { UseShellExecute = true });
            } catch (Exception ex) { RecordDiagnostic(ex); throw new SettingsException("folder.unavailable"); }
        }
        static object ReadAboutDocument(string name) {
            var files = new Dictionary<string, string> { { "changelog", "CHANGELOG.md" }, { "license", "LICENSE" }, { "third-party", "THIRD_PARTY_NOTICES.md" } };
            string file;
            if (name == null || !files.TryGetValue(name, out file)) throw new SettingsException("document.invalid");
            string path = Path.Combine(Root, file);
            if (!SafeLocalPath(path) || !File.Exists(path) || new FileInfo(path).Length > 512 * 1024) throw new SettingsException("document.unavailable");
            return new { success = true, text = File.ReadAllText(path, Encoding.UTF8) };
        }
        static void RecordDiagnostic(Exception error) {
            // Deliberately never retain exception text, paths, account details,
            // request URLs, headers, credentials or stack traces in diagnostics.
            var ex = error.GetBaseException();
            string category = ex is System.Net.WebException || ex is System.Net.Sockets.SocketException ? "network" :
                ex is UnauthorizedAccessException ? "permission" : ex is FormatException || ex is InvalidDataException || ex is ArgumentException ? "data" :
                ex is IOException ? "storage" : "internal";
            lock (DiagnosticSync) { int count; DiagnosticErrors.TryGetValue(category, out count); DiagnosticErrors[category] = Math.Min(9999, count + 1); }
        }
        static object DiagnosticInfo() {
            lock (DiagnosticSync) return new { success = true, version = AppVersion, mode = RunMode(), automaticUpdatesThisRun = !Offline && AutomaticUpdatesEnabled,
                errors = DiagnosticErrors.OrderBy(pair => pair.Key).Select(pair => new { category = pair.Key, count = pair.Value }).ToArray() };
        }
        static string StartupFailureMessage() {
            string warning; var preferences = ReadPreferences(out warning);
            string language = preferences.language == "system" ? SystemLanguage() : preferences.language;
            return language == "zh-CN" ? "Luna 启动失败。请检查程序文件是否完整、文件夹是否可写、端口是否可用。详细信息保存在程序文件夹的“启动错误.txt”。" :
                "Luna could not start. Check that the app folder is complete and writable and the port is available. Details are saved in 启动错误.txt in the app folder.";
        }
    }
}

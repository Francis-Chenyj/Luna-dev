using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Luna {
    internal sealed class PreferenceStore {
        readonly RuntimeOptions options;
        readonly DiagnosticLog diagnostics;
        readonly QuarantineStore quarantine;
        internal PreferenceStore(RuntimeOptions options, DiagnosticLog diagnostics, QuarantineStore quarantine) {
            this.options = options;
            this.diagnostics = diagnostics;
            this.quarantine = quarantine;
        }
        internal event Action<string, bool, bool> Changed;
        internal sealed class Preferences {
            public int schema = 1;
            public string language = "system", theme = "system", closeAction = "ask";
            public bool autoUpdate = true;
            public bool appAutoCheck = true, appAutoDownload = false;
        }
        internal sealed class SettingsException : ArgumentException {
            public readonly string Code;
            public SettingsException(string code) : base(code) { Code = code; }
        }
        internal const string AppVersion = "1.7.2";
        string SettingsFile { get { return Path.Combine(options.CacheDir, "settings.json"); } }
        internal Preferences ReadPreferences(out string warning) {
            warning = "";
            try {
                if (!DataFormat.SafeLocalPath(SettingsFile)) throw new IOException();
                if (!File.Exists(SettingsFile)) return new Preferences();
                if (new FileInfo(SettingsFile).Length > 16384) throw new IOException();
                var record = DataFormat.Json().DeserializeObject(File.ReadAllText(SettingsFile, Encoding.UTF8)) as Dictionary<string, object>;
                if (record == null) throw new IOException();
                if (DataFormat.Str(record, "schema") != "1") { warning = "settings.unsupported"; return new Preferences { autoUpdate = false, appAutoCheck = false }; }
                string language = DataFormat.Str(record, "language"), theme = DataFormat.Str(record, "theme"), closeAction = DataFormat.Str(record, "closeAction", "ask");
                if (!new[] { "system", "zh-CN", "en" }.Contains(language) || !new[] { "system", "dark", "light" }.Contains(theme) || !new[] { "ask", "tray", "exit" }.Contains(closeAction) || !(DataFormat.Get(record, "autoUpdate") is bool)) throw new IOException();
                bool check = DataFormat.Get(record, "appAutoCheck", true).Equals(true);
                // Respect an old explicit opt-out; a legacy download-only preference
                // must not silently turn networking back on during migration.
                return new Preferences { language = language, theme = theme, closeAction = closeAction, autoUpdate = (bool)record["autoUpdate"],
                    appAutoCheck = check, appAutoDownload = check && DataFormat.Get(record, "appAutoDownload", false).Equals(true) };
            } catch { warning = "settings.unreadable"; return new Preferences { autoUpdate = false, appAutoCheck = false }; }
        }
        internal void SavePreference(string key, string value) {
            if (key == "language" && !new[] { "system", "zh-CN", "en" }.Contains(value) ||
                key == "theme" && !new[] { "system", "dark", "light" }.Contains(value) ||
                key == "closeAction" && !new[] { "ask", "tray", "exit" }.Contains(value) ||
                new[] { "autoUpdate", "appAutoCheck", "appAutoDownload" }.Contains(key) && value != "true" && value != "false" ||
                !new[] { "language", "theme", "autoUpdate", "closeAction", "appAutoCheck", "appAutoDownload" }.Contains(key)) throw new SettingsException("settings.invalid");
            bool updateChanged = false;
            using (var mutex = new Mutex(false, "Local\\LunaSettings_" + DataFormat.Hash(options.Root.ToLowerInvariant()).Substring(0, 24))) {
                bool acquired;
                try { acquired = mutex.WaitOne(2000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new SettingsException("settings.busy");
                try {
                    string warning; var preferences = ReadPreferences(out warning);
                    if (warning == "settings.unsupported") throw new SettingsException(warning);
                    if (!DataFormat.SafeLocalPath(SettingsFile)) throw new SettingsException("settings.saveFailed");
                    Directory.CreateDirectory(options.CacheDir);
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
                    if (warning != "" && File.Exists(SettingsFile)) quarantine.QuarantineOwnedFile(SettingsFile);
                    DataFormat.WriteRecord(SettingsFile, preferences);

                } catch (SettingsException) { throw; }
                catch (Exception ex) { diagnostics.RecordDiagnostic(ex); throw new SettingsException("settings.saveFailed"); }
                finally { mutex.ReleaseMutex(); }
            }
            // Schedule only after the complete coupled preference is durably saved,
            // and outside the settings mutex. Network work never blocks this request.
            var changed = Changed; if (changed != null) changed(key, value == "true", updateChanged);
        }
        internal string FolderFor(string kind) {
            string path = kind == "logs" ? options.LogDirectory : kind == "data" ? options.CacheDir : kind == "updates" ? options.AppUpdateDir : null;
            if (path == null) throw new SettingsException("folder.invalid");
            if (!DataFormat.PlainPath(path)) throw new SettingsException("folder.unavailable");
            return path;
        }
        internal void OpenLocalFolder(string kind) {
            string path = FolderFor(kind);
            try {
                Directory.CreateDirectory(path);
                // Only these two fixed directories can be opened; never execute a
                // user-supplied path, shell command, URL or file.
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + path + "\"") { UseShellExecute = true });
            } catch (Exception ex) { diagnostics.RecordDiagnostic(ex); throw new SettingsException("folder.unavailable"); }
        }
        internal object ReadAboutDocument(string name) {
            var files = new Dictionary<string, string> { { "changelog", "CHANGELOG.md" }, { "license", "LICENSE" }, { "third-party", "THIRD_PARTY_NOTICES.md" } };
            string file;
            if (name == null || !files.TryGetValue(name, out file)) throw new SettingsException("document.invalid");
            string path = Path.Combine(options.Root, file);
            if (!DataFormat.SafeLocalPath(path) || !File.Exists(path) || new FileInfo(path).Length > 512 * 1024) throw new SettingsException("document.unavailable");
            return new { success = true, text = File.ReadAllText(path, Encoding.UTF8) };
        }
        internal string StartupFailureMessage() {
            string warning; var preferences = ReadPreferences(out warning);
            string language = preferences.language == "system" ? DataFormat.SystemLanguage() : preferences.language;
            return language == "zh-CN" ? "Luna 启动失败。请检查程序文件是否完整、文件夹是否可写、端口是否可用。详细信息保存在程序文件夹的“启动错误.txt”。" :
                "Luna could not start. Check that the app folder is complete and writable and the port is available. Details are saved in 启动错误.txt in the app folder.";
        }
    }
}

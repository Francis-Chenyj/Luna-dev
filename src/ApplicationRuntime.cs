using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace Luna {
    // Immutable launch policy and paths. This object contains no job or repository state.
    internal sealed class RuntimeOptions {
        internal readonly string Root, CacheDir, LibraryDir, FocusDir, AppUpdateDir, LogDirectory;
        internal readonly bool Offline, CommandLineDisablesUpdates;
        internal RuntimeOptions(string root, bool offline = false, bool noAutomaticUpdates = false) {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            CacheDir = Path.Combine(Root, "cache"); LibraryDir = Path.Combine(CacheDir, "library");
            FocusDir = Path.Combine(CacheDir, "focus"); AppUpdateDir = Path.Combine(CacheDir, "app-update");
            LogDirectory = Path.Combine(CacheDir, "logs"); Offline = offline; CommandLineDisablesUpdates = noAutomaticUpdates;
        }
        internal string Mode { get { return Offline ? "offline" : CommandLineDisablesUpdates ? "manual" : "normal"; } }
    }

    // Registers workers before they start; shutdown prevents any new registration.
    // Cancellation happens outside the registry lock, since callbacks may finish a worker.
    internal sealed class BackgroundWork : IDisposable {
        readonly object sync = new object();
        readonly HashSet<Thread> threads = new HashSet<Thread>();
        readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        readonly Action<Exception> log;
        readonly CancellationToken token;
        bool stopping, cancelled, released, cleanupFinished;
        Action drained;
        internal BackgroundWork(Action<Exception> log) { this.log = log; token = shutdown.Token; }
        internal CancellationToken Token { get { return token; } }
        internal bool IsCancellationRequested { get { lock (sync) return stopping; } }
        internal bool TryStart(Action action, string name, ApartmentState? apartment = null) {
            lock (sync) {
                if (stopping) return false;
                var thread = new Thread(() => {
                    try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { log(ex); }
                    finally { lock (sync) threads.Remove(Thread.CurrentThread); Complete(); }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = name };
                if (apartment.HasValue) thread.SetApartmentState(apartment.Value);
                threads.Add(thread);
                try { thread.Start(); } catch { threads.Remove(thread); throw; }
                return true;
            }
        }
        internal void Stop() {
            lock (sync) { if (stopping) return; stopping = true; }
            try { shutdown.Cancel(); } catch (Exception ex) { log(ex); }
            finally { lock (sync) cancelled = true; Complete(); }
        }
        internal bool WaitForIdle(TimeSpan timeout) {
            var watch = Stopwatch.StartNew();
            while (true) {
                Thread[] pending;
                lock (sync) {
                    if (threads.Count == 0) {
                        if (!stopping || cleanupFinished) return true;
                        int remaining = (int)Math.Max(0, Math.Min(int.MaxValue, (timeout - watch.Elapsed).TotalMilliseconds));
                        if (remaining == 0) return false;
                        Monitor.Wait(sync, remaining); continue;
                    }
                    pending = new List<Thread>(threads).ToArray();
                }
                foreach (var thread in pending) {
                    if (thread == Thread.CurrentThread) return false;
                    int remaining = (int)Math.Max(0, Math.Min(int.MaxValue, (timeout - watch.Elapsed).TotalMilliseconds));
                    if (!thread.Join(remaining)) return false;
                }
            }
        }
        internal void WhenDrained(Action action) {
            bool now; lock (sync) { now = released; if (!now) drained += action; }
            if (now) action(); else Complete();
        }
        void Complete() {
            Action cleanup;
            lock (sync) {
                if (!cancelled || threads.Count != 0 || released) return;
                released = true; cleanup = drained; drained = null;
            }
            try {
                if (cleanup != null) foreach (Action action in cleanup.GetInvocationList()) {
                    try { action(); } catch (Exception ex) { log(ex); }
                }
            }
            finally { shutdown.Dispose(); lock (sync) { cleanupFinished = true; Monitor.PulseAll(sync); } }
        }
        public void Dispose() {
            Stop();
            if (!WaitForIdle(TimeSpan.FromSeconds(3))) log(new TimeoutException("Background shutdown is still draining; resources remain owned until workers finish."));
        }
    }

    // Composition root only: state stays inside the service that owns it.
    internal sealed class ApplicationRuntime : IDisposable {
        internal readonly RuntimeOptions Options;
        internal readonly BackgroundWork Workers;
        internal readonly DiagnosticLog Diagnostics;
        internal readonly QueryActivity Activity;
        internal readonly QuarantineStore Quarantine;
        internal readonly PreferenceStore Preferences;
        internal readonly LibraryStore Library;
        internal readonly QueryService Queries;
        internal readonly DataSourceClient Source;
        internal readonly ReleaseClient Releases;
        internal readonly AppUpdateService AppUpdates;
        internal readonly StorageMaintenance Storage;
        internal readonly DataUpdateCoordinator Updates;
        internal Func<bool> DesktopAvailable;
        internal Action AppearanceChanged;
        int disposed;

        internal ApplicationRuntime(RuntimeOptions options,
            Func<string, CancellationToken, Dictionary<string, object>> data = null,
            Func<string, CancellationToken, string> release = null,
            Func<string, long, CancellationToken, HttpWebResponse> download = null) {
            Options = options; Diagnostics = new DiagnosticLog(options); Workers = new BackgroundWork(Diagnostics.Log);
            Activity = new QueryActivity(); Quarantine = new QuarantineStore(options);
            Preferences = new PreferenceStore(options, Diagnostics, Quarantine);
            Library = new LibraryStore(options, Diagnostics, Quarantine, Activity); Queries = new QueryService(Library);
            Source = new DataSourceClient(data); Releases = new ReleaseClient(release, download);
            AppUpdates = new AppUpdateService(options, Workers, Preferences, Releases, Activity, Diagnostics);
            Storage = new StorageMaintenance(options, Workers, Library, AppUpdates, Quarantine, Activity, Diagnostics);
            string warning; var saved = Preferences.ReadPreferences(out warning);
            Updates = new DataUpdateCoordinator(options, Workers, Library, Storage, Source, Diagnostics, saved.autoUpdate);
            Preferences.Changed += PreferenceChanged;
            Workers.WhenDrained(Source.Dispose);
        }
        void PreferenceChanged(string key, bool enabled, bool updateChanged) {
            if (AppearanceChanged != null) AppearanceChanged();
            if (updateChanged) AppUpdates.AppUpdatePreferenceChanged(key, enabled);
        }
        internal object LibraryInfo(string profile) {
            var result = Library.LibraryInfo(profile);
            result["offline"] = Options.Offline;
            result["automaticUpdates"] = !Options.Offline && Updates.AutomaticUpdates;
            result["job"] = Updates.Status; result["storage"] = Storage.Status;
            return result;
        }
        internal object SettingsInfo() {
            string warning; var saved = Preferences.ReadPreferences(out warning);
            return new { success = true, preferences = saved, warning = warning, systemLanguage = DataFormat.SystemLanguage(), version = PreferenceStore.AppVersion,
                mode = Options.Mode, desktopWindowAvailable = DesktopAvailable != null && DesktopAvailable(), automaticUpdatesThisRun = !Options.Offline && Updates.AutomaticUpdates };
        }
        internal object SavePreference(string key, string value) { Preferences.SavePreference(key, value); return SettingsInfo(); }
        internal object DiagnosticInfo() { return new { success = true, version = PreferenceStore.AppVersion, mode = Options.Mode,
            automaticUpdatesThisRun = !Options.Offline && Updates.AutomaticUpdates, errors = Diagnostics.Errors }; }
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Preferences.Changed -= PreferenceChanged; Workers.Dispose();
        }
    }
}

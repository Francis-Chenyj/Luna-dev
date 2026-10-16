using File = Luna.LocalFile;
using Directory = Luna.LocalDirectory;
using FileInfo = Luna.LocalFileInfo;
using DirectoryInfo = Luna.LocalDirectoryInfo;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Luna {
    internal static partial class Program {
        static DesktopWindow Desktop;

        // Keep close policy independent of UI lifetime and safe for old preferences.
        internal static string ResolveCloseAction(string saved, bool explicitExit, bool endingSession) {
            return explicitExit || endingSession ? "exit" : saved == "tray" || saved == "exit" ? saved : "ask";
        }
        internal static bool CanAskPageToClose(bool navigationReady, bool applicationReady) {
            return navigationReady && applicationReady;
        }
        static bool RestoreExistingWindow(string address) {
            try {
                Uri uri;
                if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1") return false;
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(uri.GetLeftPart(UriPartial.Authority) + "/api/window/show");
                request.Proxy = null; request.Method = "POST"; request.ContentLength = 0; request.Headers["X-Luna"] = "1"; request.Timeout = 3500;
                using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                    return DataFormat.Get(DataFormat.Obj(DataFormat.Json().DeserializeObject(reader.ReadToEnd())), "available", false).Equals(true);
            } catch { return false; }
        }
        static object WindowInfo() { return Desktop == null ? (object)new { available = false } : Desktop.Status; }
        static void ValidateWindowChoice(string choice, string remember) {
            if ((choice != "tray" && choice != "exit") || (remember != "true" && remember != "false")) throw new PreferenceStore.SettingsException("settings.invalid");
            if (Desktop == null) throw new PreferenceStore.SettingsException("window.unavailable");
            // A failed save must not hide/exit the window or claim the choice was remembered.
            if (remember == "true") Runtime.SavePreference("closeAction", choice);
        }

        sealed class DesktopWindow : Form {
            readonly WebView2 view = new WebView2();
            readonly NotifyIcon tray = new NotifyIcon();
            readonly ContextMenuStrip menu = new ContextMenuStrip();
            readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            readonly FrameAttributeState dark = new FrameAttributeState(), caption = new FrameAttributeState(), text = new FrameAttributeState();
            readonly Label loading = new Label();
            Icon appIcon, trayIcon;
            bool quitting, ready, uiReady, starting, trayNoticeShown;
            bool cacheCleaning, cacheCleanupRequested;
            DateTime nextCacheCheck = DateTime.UtcNow.AddSeconds(12);
            string language = "", lastTheme = "";
            public volatile object Status = new { available = true, ready = false, inTray = false };
            public DesktopWindow() {
                Text = "Luna"; StartPosition = FormStartPosition.CenterScreen;
                if (DesktopProbe) { ShowInTaskbar = false; Opacity = 0; }
                AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
                ClientSize = new Size(1200, 780); MinimumSize = new Size(720, 480);
                appIcon = new Icon(Path.Combine(Root, "web", "app.ico"), new Size(64, 64));
                trayIcon = new Icon(Path.Combine(Root, "web", "app.ico"), new Size(32, 32));
                Icon = appIcon; tray.Icon = trayIcon; tray.Text = "Luna";
                menu.Items.Add("Luna", null, delegate { RestoreWindow(); });
                menu.Items.Add("Exit", null, delegate { Stop(); });
                menu.Opening += delegate { UpdateAppearance(); };
                tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { RestoreWindow(); };
                loading.Dock = DockStyle.Fill; loading.TextAlign = ContentAlignment.MiddleCenter; loading.Text = "Luna…";
                loading.Font = new Font("Segoe UI", 13); Controls.Add(loading);
                view.Dock = DockStyle.Fill; view.Visible = false; Controls.Add(view);
                Shown += async delegate {
                    // Keep a newly launched window within the current monitor at high DPI.
                    var area = Screen.FromControl(this).WorkingArea;
                    float scale = 1;
                    using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
                    MinimumSize = new Size(Math.Min((int)(720 * scale), area.Width - 24), Math.Min((int)(480 * scale), area.Height - 24));
                    Size = new Size(Math.Min((int)(1320 * scale), area.Width - 24), Math.Min((int)(900 * scale), area.Height - 24));
                    Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
                    UpdateAppearance(); await StartView();
                };
                HandleCreated += delegate { UpdateAppearance(); };
                FormClosing += ClosingWindow;
                FormClosed += delegate { Stop(); };
                timer.Interval = 1000; timer.Tick += delegate { UpdateAppearance(); CheckBrowserCache(); }; timer.Start();
            }
            bool Chinese { get { return language == "zh-CN"; } }
            async System.Threading.Tasks.Task StartView() {
                if (starting || quitting) return;
                starting = true;
                try {
                    var profile = Path.Combine(CacheDir, "webview2");
                    if (!DataFormat.PlainPath(profile)) throw new IOException("Unsafe WebView profile path");
                    Directory.CreateDirectory(profile);
                    // Official AnyCPU SDK layout selects the matching x86/x64/ARM64 loader.
                    var env = await CoreWebView2Environment.CreateAsync(null, profile);
                    if (quitting || IsDisposed) return;
                    await view.EnsureCoreWebView2Async(env);
                    if (quitting || IsDisposed) return;
                    var core = view.CoreWebView2;
                    core.Settings.AreDevToolsEnabled = false;
                    core.Settings.AreDefaultContextMenusEnabled = false;
                    core.Settings.IsStatusBarEnabled = false;
                    core.Settings.IsPasswordAutosaveEnabled = false;
                    core.Settings.IsGeneralAutofillEnabled = false;
                    core.Settings.AreHostObjectsAllowed = false;
                    core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                        if (LocalNavigation(e.Uri)) { uiReady = false; UpdateAppearance(); return; }
                        e.Cancel = true; if (e.IsUserInitiated) OpenExternal(e.Uri);
                    };
                    core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
                        e.Handled = true; if (e.IsUserInitiated) OpenExternal(e.Uri);
                    };
                    core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                    core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                    core.DocumentTitleChanged += delegate { Text = core.DocumentTitle; };
                    core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
                        if (!LocalNavigation(e.Source)) return;
                        try { if (e.TryGetWebMessageAsString() == "luna-ui-ready") { uiReady = true; UpdateAppearance(); } } catch (ArgumentException) { }
                    };
                    core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) {
                        if (quitting) return;
                        ready = e.IsSuccess; view.Visible = ready; loading.Visible = !ready;
                        if (!ready) loading.Text = Chinese ? "页面未能打开。请退出后重启 Luna。" : "The page could not open. Exit and restart Luna.";
                        UpdateAppearance();
                    };
                    core.ProcessFailed += delegate { ready = false; uiReady = false; UpdateAppearance(); ShowLoadError(); };
                    // Only disposable browser caches: never localStorage (recent queries),
                    // preferences or matchup data. Cleanup is async and cannot block queries.
                    core.Navigate(Address + "/");
                } catch (Exception ex) { if (!quitting) { Runtime.Diagnostics.RecordDiagnostic(ex); Runtime.Diagnostics.Log(ex); ShowLoadError(); } }
                finally { starting = false; }
            }
            async void ClearBrowserCache() {
                cacheCleaning = true; cacheCleanupRequested = false;
                try {
                    await view.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);
                    string path = Path.Combine(CacheDir, "browser-cleaned.json");
                    if (DataFormat.SafeLocalPath(path)) DataFormat.WriteRecord(path, new { checkedAt = DataFormat.Stamp() });
                }
                catch (Exception ex) { Runtime.Diagnostics.RecordDiagnostic(ex); }
                finally { cacheCleaning = false; }
            }
            void CheckBrowserCache() {
                if (!ready || quitting || cacheCleaning || DateTime.UtcNow < nextCacheCheck || DateTime.UtcNow.Ticks - Runtime.Activity.LastQuery < TimeSpan.FromSeconds(3).Ticks) return;
                nextCacheCheck = DateTime.UtcNow.AddHours(1);
                if (cacheCleanupRequested || DataFormat.DateValue(DataFormat.Str(DataFormat.ReadRecord(Path.Combine(CacheDir, "browser-cleaned.json")), "checkedAt")) < DateTime.UtcNow.AddDays(-1)) ClearBrowserCache();
            }
            internal void RequestCacheCleanup() {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() => { cacheCleanupRequested = true; nextCacheCheck = DateTime.UtcNow.AddSeconds(3); }));
            }
            void ShowLoadError() {
                view.Visible = false; loading.Visible = true;
                loading.Text = Chinese ? "Luna 窗口未能加载。\n请确认 Microsoft Edge WebView2 Runtime 已安装。\n也可用 Luna.exe --browser 启动兼容模式（不支持托盘）。\n详细信息见 cache\\logs。" :
                    "Luna could not load its window.\nCheck that Microsoft Edge WebView2 Runtime is installed.\nAlternatively launch Luna.exe --browser (no tray support).\nDetails: cache\\logs.";
            }
            internal static bool LocalNavigation(string url) {
                Uri uri; return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.GetLeftPart(UriPartial.Authority) == Address;
            }
            static void OpenExternal(string url) {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { Runtime.Diagnostics.RecordDiagnostic(ex); }
            }
            void ClosingWindow(object sender, FormClosingEventArgs e) {
                string warning; var pref = Runtime.Preferences.ReadPreferences(out warning);
                string action = ResolveCloseAction(pref.closeAction, quitting, e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing);
                if (action == "exit") { quitting = true; return; }
                e.Cancel = true;
                if (action == "tray") { HideToTray(); return; }
                if (CanAskPageToClose(ready, uiReady)) {
                    try { view.CoreWebView2.PostWebMessageAsString("luna-close-request"); return; }
                    catch (InvalidOperationException ex) { Runtime.Diagnostics.RecordDiagnostic(ex); }
                }
                // Startup/render failures must never trap users in an unclosable window.
                var reply = MessageBox.Show(this, Chinese ? "Luna 正在加载或页面不可用。\n是：最小化到托盘\n否：退出应用" : "Luna is loading or the page is unavailable.\nYes: minimize to tray\nNo: exit the app", "Luna", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (reply == DialogResult.Yes) HideToTray(); else if (reply == DialogResult.No) Stop();
            }
            public void Choose(string action) { Dispatch(delegate { if (action == "tray") HideToTray(); else Stop(); }); }
            public void RefreshAppearance() { Dispatch(UpdateAppearance); }
            public void ShowFromAnotherLaunch() { Dispatch(RestoreWindow); }
            public void End() { Dispatch(delegate { if (quitting) return; quitting = true; tray.Visible = false; Close(); }); }
            void Dispatch(Action action) {
                if (IsDisposed || !IsHandleCreated) return;
                try { if (InvokeRequired) BeginInvoke(action); else action(); } catch (InvalidOperationException) { }
            }
            void HideToTray() {
                if (quitting) return;
                // Do not hide the only window unless the notification icon was registered.
                try { tray.Visible = true; } catch (Exception ex) { Runtime.Diagnostics.RecordDiagnostic(ex); return; }
                Hide(); UpdateAppearance();
                if (!trayNoticeShown) {
                    trayNoticeShown = true;
                    tray.ShowBalloonTip(2500, "Luna", Chinese ? "已最小化到托盘。双击图标可恢复；右键可退出。" : "Luna is in the tray. Double-click to open; right-click to exit.", ToolTipIcon.Info);
                }
            }
            void RestoreWindow() {
                if (quitting) return;
                Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate(); tray.Visible = false; UpdateAppearance();
            }
            void UpdateAppearance() {
                if (IsDisposed || !IsHandleCreated) return;
                string warning; var pref = Runtime.Preferences.ReadPreferences(out warning);
                language = pref.language == "system" ? DataFormat.SystemLanguage() : pref.language;
                menu.Items[0].Text = Chinese ? "打开 Luna" : "Open Luna"; menu.Items[1].Text = Chinese ? "退出" : "Exit";
                if (!ready) Text = Chinese ? "Luna · 英雄联盟对局助手" : "Luna · League of Legends Matchup Explorer";
                var palette = ReadFrameColors();
                ApplyNativeFrame(Handle, dark, 20, palette.Dark);
                ApplyNativeFrame(Handle, caption, 35, palette.Caption);
                ApplyNativeFrame(Handle, text, 36, palette.Text);
                string theme = palette.Dark + ":" + palette.Caption;
                if (theme != lastTheme) {
                    lastTheme = theme;
                    BackColor = palette.Caption == -1 ? SystemColors.Window : ColorTranslator.FromWin32(palette.Caption);
                    ForeColor = palette.Text == -1 ? SystemColors.WindowText : ColorTranslator.FromWin32(palette.Text);
                    loading.BackColor = BackColor; loading.ForeColor = ForeColor; view.DefaultBackgroundColor = BackColor;
                    menu.BackColor = BackColor; menu.ForeColor = ForeColor;
                }
                Status = new { available = true, ready = ready, uiReady = uiReady, inTray = tray.Visible && !Visible,
                    frame = new[] { new { attribute = 20, applied = dark.Applied, result = dark.LastResult }, new { attribute = 35, applied = caption.Applied, result = caption.LastResult }, new { attribute = 36, applied = text.Applied, result = text.LastResult } } };
            }
            protected override void Dispose(bool disposing) {
                if (disposing) { timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); view.Dispose(); if (appIcon != null) appIcon.Dispose(); if (trayIcon != null) trayIcon.Dispose(); }
                base.Dispose(disposing);
            }
        }

        // Some documented DWM color attributes are write-only. A failed read
        // must not prevent setting the requested color (observed on Windows 11).
        internal sealed class FrameAttributeState {
            public int? Original, Applied, LastResult;
            public void Apply(int value, Func<int?> read, Func<int, int> write, int reset) {
                int? current = read();
                if (current == value || !current.HasValue && Applied == value) return;
                LastResult = write(value);
                if (LastResult != 0) return;
                if (!Original.HasValue) Original = current ?? reset;
                Applied = value;
            }
            public void Restore(Func<int?> read, Func<int, int> write) {
                if (!Applied.HasValue || !Original.HasValue) return;
                int? current = read();
                if (!current.HasValue || current == Applied) write(Original.Value);
            }
        }
        static int? ReadNativeFrame(IntPtr hwnd, int attribute) {
            try { int value; return NativeGetFrame(hwnd, attribute, out value, 4) == 0 ? (int?)value : null; }
            catch (DllNotFoundException) { return null; } catch (EntryPointNotFoundException) { return null; }
        }
        static int WriteNativeFrame(IntPtr hwnd, int attribute, int value) {
            try { return NativeSetFrame(hwnd, attribute, ref value, 4); }
            catch (DllNotFoundException) { return unchecked((int)0x80004001); } catch (EntryPointNotFoundException) { return unchecked((int)0x80004001); }
        }
        static void ApplyNativeFrame(IntPtr hwnd, FrameAttributeState state, int attribute, int value) {
            state.Apply(value, () => ReadNativeFrame(hwnd, attribute), v => WriteNativeFrame(hwnd, attribute, v), attribute == 20 ? 0 : -1);
        }
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int NativeGetFrame(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")] static extern int NativeSetFrame(IntPtr hwnd, int attribute, ref int value, int size);
    }
}

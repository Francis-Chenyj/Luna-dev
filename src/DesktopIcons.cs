using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Luna {
    internal static partial class Program {
        static DesktopIconService DesktopIcons;
        static readonly object DesktopIconSync = new object();
        static string DesktopTag(int port) { return "Luna" + DataFormat.Hash(Root.ToUpperInvariant() + ":" + port).Substring(0, 16); }
        static string DesktopPath { get { return "/" + DesktopTag(Port); } }
        sealed class FramePalette {
            public int Dark, Caption, Text;
        }
        static FramePalette FrameColors(string preference, bool systemDark, bool highContrast) {
            // COLORREF is 0x00BBGGRR. In high contrast leave colors to Windows.
            bool dark = preference == "dark" || preference == "system" && systemDark;
            return new FramePalette { Dark = highContrast ? 0 : dark ? 1 : 0,
                Caption = highContrast ? -1 : dark ? 0x151711 : 0xf1f7f4,
                Text = highContrast ? -1 : dark ? 0xeff4f0 : 0x222d1d };
        }
        static FramePalette ReadFrameColors() {
            string warning; var preferences = Runtime.Preferences.ReadPreferences(out warning);
            bool dark = false, highContrast = false;
            try { highContrast = System.Windows.Forms.SystemInformation.HighContrast; } catch { }
            try {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            } catch { }
            return FrameColors(preferences.theme, dark, highContrast);
        }
        static void StartDesktopIcons() {
            lock (DesktopIconSync) {
                if (DesktopIcons != null) return;
                DesktopIcons = new DesktopIconService(DesktopTag(Port), Root, Runtime.Options.Offline, Port);
                DesktopIcons.Start();
            }
        }

        // Edge --app otherwise derives its native window icon from a tiny favicon.
        // A private launch path gives this installation/port its own application ID.
        // Only Edge windows carrying that exact tag are eligible: never ordinary
        // browser tabs, other localhost apps, or windows identified by title alone.
        sealed class DesktopIconService {
            readonly string tag, iconFile, command;
            readonly Dictionary<IntPtr, WindowIcons> windows = new Dictionary<IntPtr, WindowIcons>();
            readonly object sync = new object();
            string error = "";
            sealed class WindowIcons {
                public uint ProcessId, Dpi;
                public IntPtr Small, Large;
                public int SmallSize, LargeSize;
                public readonly Dictionary<int, FrameAttributeState> Frame = new Dictionary<int, FrameAttributeState>();
            }
            public DesktopIconService(string tag, string root, bool offline, int port) {
                this.tag = tag;
                iconFile = Path.Combine(root, "web", "app-1.4.1.ico");
                command = "\"" + Path.Combine(root, "Luna.exe") + "\" --port " + port + (offline ? " --offline" : "");
            }
            public void Start() {
                Runtime.Workers.TryStart(Run, "Luna window icons", ApartmentState.STA);
            }
            public void Close() { Runtime.Workers.Stop(); }
            public object Status() {
                lock (sync) return new { supported = true, error = error, windows = windows.Select(pair => new {
                    dpi = pair.Value.Dpi, smallPixels = pair.Value.SmallSize, largePixels = pair.Value.LargeSize,
                    smallApplied = ReadIcon(pair.Key, 0) == pair.Value.Small,
                    largeApplied = ReadIcon(pair.Key, 1) == pair.Value.Large,
                    frame = new[] { 20, 35, 36 }.Select(attribute => FrameStatus(pair.Key, pair.Value, attribute)).ToArray()
                }).ToArray() };
            }
            static object FrameStatus(IntPtr hwnd, WindowIcons icons, int attribute) {
                int value = 0, code; FrameAttributeState state;
                try { code = DwmGetWindowAttribute(hwnd, attribute, out value, 4); }
                catch (DllNotFoundException) { code = unchecked((int)0x80004001); }
                catch (EntryPointNotFoundException) { code = unchecked((int)0x80004001); }
                return new { attribute = attribute, readResult = "0x" + code.ToString("X8"),
                    value = code == 0 ? (int?)value : null,
                    applied = icons.Frame.TryGetValue(attribute, out state) ? state.Applied : null };
            }
            void Run() {
                try {
                    while (!Runtime.Workers.IsCancellationRequested) {
                        lock (sync) Sweep();
                        if (Runtime.Workers.Token.WaitHandle.WaitOne(1500)) break;
                    }
                } catch (Exception ex) { lock (sync) error = ex.GetType().Name; }
                finally {
                    lock (sync) { foreach (var pair in windows) Release(pair.Key, pair.Value); windows.Clear(); }
                }
            }
            void Sweep() {
                var palette = ReadFrameColors();
                foreach (var pair in windows.ToArray()) {
                    uint pid; GetWindowThreadProcessId(pair.Key, out pid);
                    if (!IsWindow(pair.Key) || pid != pair.Value.ProcessId) {
                        Destroy(pair.Value); windows.Remove(pair.Key);
                    }
                }
                EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
                    try {
                        var name = new StringBuilder(80); GetClassName(hwnd, name, name.Capacity);
                        if (name.ToString() != "Chrome_WidgetWin_1") return true;
                        IPropertyStore store = Store(hwnd); if (store == null) return true;
                        try {
                            string id = ReadString(store, 5);
                            if (id.IndexOf(tag, StringComparison.Ordinal) < 0 || !id.StartsWith("MSEdge.", StringComparison.Ordinal)) return true;
                            uint pid; GetWindowThreadProcessId(hwnd, out pid);
                            using (var process = Process.GetProcessById((int)pid)) {
                                string executable = process.MainModule.FileName;
                                if (!EdgePaths().Any(p => string.Equals(p, executable, StringComparison.OrdinalIgnoreCase))) return true;
                            }
                            WindowIcons icons;
                            if (!windows.TryGetValue(hwnd, out icons)) { icons = new WindowIcons { ProcessId = pid }; windows[hwnd] = icons; }
                            // The full multi-resolution file is also the shell/pinning icon.
                            SetString(store, 2, command); SetString(store, 3, iconFile + ",0"); SetString(store, 4, "Luna");
                            uint dpi = GetDpiForWindow(hwnd); if (dpi == 0) dpi = 96;
                            int small = GetSystemMetricsForDpi(49, dpi), large = GetSystemMetricsForDpi(11, dpi);
                            Apply(hwnd, 0, small, ref icons.Small, ref icons.SmallSize);
                            Apply(hwnd, 1, large, ref icons.Large, ref icons.LargeSize);
                            icons.Dpi = dpi;
                            ApplyFrame(hwnd, icons, 20, palette.Dark);
                            ApplyFrame(hwnd, icons, 35, palette.Caption);
                            ApplyFrame(hwnd, icons, 36, palette.Text);
                        } finally { Marshal.ReleaseComObject(store); }
                    } catch (Exception ex) { error = ex.GetType().Name; }
                    return true;
                }, IntPtr.Zero);
            }
            // Browser theme-color is not honored by every Edge --app frame.
            // Probe the documented DWM attributes only on the exact tagged Luna
            // window. Unsupported systems keep their own frame; queries and
            // icons never depend on this optional appearance enhancement.
            static void ApplyFrame(IntPtr hwnd, WindowIcons icons, int attribute, int value) {
                FrameAttributeState state;
                if (!icons.Frame.TryGetValue(attribute, out state)) { state = new FrameAttributeState(); icons.Frame[attribute] = state; }
                ApplyNativeFrame(hwnd, state, attribute, value);
            }
            static void RestoreFrame(IntPtr hwnd, WindowIcons icons) {
                foreach (var pair in icons.Frame) {
                    int attribute = pair.Key;
                    pair.Value.Restore(() => ReadNativeFrame(hwnd, attribute), v => WriteNativeFrame(hwnd, attribute, v));
                }
            }
            void Apply(IntPtr hwnd, int kind, int pixels, ref IntPtr owned, ref int oldSize) {
                if (pixels <= 0 || pixels > 256) return;
                if (owned != IntPtr.Zero && oldSize == pixels && ReadIcon(hwnd, kind) == owned) return;
                IntPtr replacement = LoadImage(IntPtr.Zero, iconFile, 1, pixels, pixels, 0x10);
                if (replacement == IntPtr.Zero) return;
                IntPtr previousOwned = owned, ignored;
                if (SendMessageTimeout(hwnd, 0x80, (IntPtr)kind, replacement, 2, 200, out ignored) == IntPtr.Zero) { DestroyIcon(replacement); return; }
                owned = replacement; oldSize = pixels;
                // Never release an icon returned by Edge: it belongs to Edge.
                if (previousOwned != IntPtr.Zero) DestroyIcon(previousOwned);
            }
            static void Release(IntPtr hwnd, WindowIcons icons) {
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if (pid == icons.ProcessId) {
                    RestoreFrame(hwnd, icons);
                    IntPtr ignored;
                    if (ReadIcon(hwnd, 0) == icons.Small) SendMessageTimeout(hwnd, 0x80, IntPtr.Zero, IntPtr.Zero, 2, 200, out ignored);
                    if (ReadIcon(hwnd, 1) == icons.Large) SendMessageTimeout(hwnd, 0x80, (IntPtr)1, IntPtr.Zero, 2, 200, out ignored);
                }
                Destroy(icons);
            }
            static void Destroy(WindowIcons icons) {
                if (icons.Small != IntPtr.Zero) DestroyIcon(icons.Small);
                if (icons.Large != IntPtr.Zero) DestroyIcon(icons.Large);
            }
            static IntPtr ReadIcon(IntPtr hwnd, int kind) {
                IntPtr icon; return SendMessageTimeout(hwnd, 0x7f, (IntPtr)kind, IntPtr.Zero, 2, 200, out icon) == IntPtr.Zero ? IntPtr.Zero : icon;
            }
            static IPropertyStore Store(IntPtr hwnd) {
                var iid = typeof(IPropertyStore).GUID; IPropertyStore store;
                return SHGetPropertyStoreForWindow(hwnd, ref iid, out store) == 0 ? store : null;
            }
            static string ReadString(IPropertyStore store, uint id) {
                var key = new PropertyKey(id); PropVariant value;
                if (store.GetValue(ref key, out value) != 0) return "";
                try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
                finally { PropVariantClear(ref value); }
            }
            static void SetString(IPropertyStore store, uint id, string text) {
                if (ReadString(store, id) == text) return;
                var key = new PropertyKey(id); var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
                try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
                finally { PropVariantClear(ref value); }
            }
            [StructLayout(LayoutKind.Sequential)] struct PropertyKey {
                public Guid Format; public uint Id;
                public PropertyKey(uint id) { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); Id = id; }
            }
            [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant {
                [FieldOffset(0)] public ushort Type;
                [FieldOffset(8)] public IntPtr Pointer;
            }
            [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
                [PreserveSig] int GetCount(out uint count);
                [PreserveSig] int GetAt(uint index, out PropertyKey key);
                [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
                [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
                [PreserveSig] int Commit();
            }
            delegate bool EnumWindow(IntPtr hwnd, IntPtr data);
            [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr data);
            [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
            [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
            [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
            [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
            [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
            [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
            [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
            [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        }
    }
}

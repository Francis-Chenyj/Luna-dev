# Luna 1.5.2 — Local revision / 本地修订

2026-10-15 · FrancisChen

- The title-bar × and Alt+F4 offer **Minimize to system tray** or **Exit app**, with an optional remembered choice. Cancel leaves the current query untouched.
- Change the remembered action in Settings → General. Double-click the green L tray icon, use Open Luna, or relaunch the same shortcut to restore the existing window. Page/tray Exit always stops the app and updater.
- Correct the 1.5.1 title-bar bug: unsupported DWM color reads no longer prevent writes. The native window uses the app theme on supported Windows versions.
- Uses the installed Microsoft WebView2 Runtime with a pinned SDK. Fully extract the package; both DLLs and runtimes must accompany the EXE. Browser fallback: `Luna.exe --browser`, without tray controls.
- Local queries and downloaded data remain unchanged. Old Edge recent-query history is not imported; new recent queries use cache/webview2. Startup clears only disposable browser caches.

- 标题栏 × 与 Alt+F4 提供“最小化到托盘区 / 退出应用”，可选记住。取消不会改变当前查询。
- 在设置常规页更改关闭行为。双击绿色 L、托盘“打开 Luna”或再次启动同一快捷方式可恢复。页面和托盘菜单的“退出”始终结束程序与后台更新。
- 修正 1.5.1 标题栏颜色读取失败导致跳过写入的问题；系统支持时，原生标题栏跟随应用主题。
- 复用微软 WebView2 Runtime，增加小型 SDK 组件。完整解压，保留两个 DLL 及 runtimes；浏览器兼容模式不支持托盘。
- 保留本地查询与已下载数据；旧 Edge 最近查询不自动导入，新记录保存在 cache/webview2，启动只清理可再生成的浏览器缓存。

See [QA](QA-1.5.2.md), [user guide](USER_GUIDE.md) / [使用说明](USER_GUIDE.zh-CN.md). This is a local build, not a GitHub release. Existing third-party-data review is unchanged.

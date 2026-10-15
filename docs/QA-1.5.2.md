# Luna 1.5.2 验证 / Verification

2026-10-15 · FrancisChen · Windows 11 build 26300 · 200% display scaling

## 自动检查 / Automated checks

完整隔离检查 `scripts/check.ps1` 16/16 通过（dist/q152b，16:03:53–16:06:54，Asia/Shanghai），涵盖 Windows 构建、88 项 JavaScript 测试、8 套 C# 测试、离线 HTTP、包清单与哈希、解压运行包、解压源码重建。随后补充“普通浏览器共享原生服务时仍禁用托盘设置”的检查；最终全部 89 项 JavaScript 测试通过。后端与原生窗口源码与完整检查副本一致。

All 16 isolated Windows checks passed. The full run included 88 JavaScript tests and eight C# suites. A subsequent browser-capability guard added one regression test; all 89 final JavaScript tests passed. Backend/native-window source matches the full passing run. Builds verify the pinned SDK hashes and do not download dependencies.

| Query path / 查询方式 | Records / 条数 | Median / 中位 ms | P95 ms | Maximum / 最大 ms |
| --- | ---: | ---: | ---: | ---: |
| Isolated offline build / 隔离离线构建 | 856 | 3.94 | 7.04 | 15.84 |
| Extracted runtime / 解压运行包 | 856 | 3.91 | 7.12 | 15.59 |
| Running native window / 当前原生窗口运行中 | 856 | 2.28 | 5.13 | 24.38 |

隔离两轮核对了响应与随包快照完全一致。原生窗口一轮只验证响应成功和计时（本机可使用更新后的数据）。模拟后台续传与发布期间 681 次并发本地读取，最大 6.81 ms。以上是本地请求/解析计时，不是冷启动或完整界面绘制、网络下载速度保证。

The two isolated runs compare every response with the bundled snapshot. The native run checks success and latency against the current local library. During a simulated updater's resume/publication, 681 concurrent local reads peaked at 6.81 ms. These measurements are not cold-start, full-render or external-network guarantees.

新增回归覆盖：旧设置缺少 closeAction 时默认询问、独立原子保存、取消不改查询、重复关闭不重置对话框、记住托盘、重新改回询问、不记住退出、保存失败保留对话框与结果、显式退出/系统结束优先于偏好、原生接口 POST/Host/Origin/X-Luna 约束、无原生窗口时不保存关闭动作、只写 DWM 属性及失败不冒充成功。

New regressions cover preference migration, atomic persistence, cancellation, duplicate close requests, remember/reset, no-remember exit, save failures, explicit/session exit priority, API boundaries, no-window handling and write-only DWM attributes.

## 实机验证 / Desktop checks

- 观察到原生窗口深色和浅色标题栏均随主题变色；此前 1.5.1 的颜色读取返回不支持，程序错误地跳过写入，本版取消此依赖。
- 实际点击标题栏 × 出现双选项和记住选择。200% 缩放下初始窗口与关闭对话框可完整显示。
- 实际查询“阿狸 · 中路”后，勾选记住并最小化到托盘；再次启动同一路径恢复相同窗口，英雄、分路、结果保留。再次点击 × 直接进入托盘，后台服务健康。
- 使用与确认按钮相同的 `/api/window/close?choice=exit&remember=true` 接口验证真实退出：进程结束、端口关闭、偏好落盘。重新启动后设置页显示“退出应用”，验证跨启动保存。另有正常 `/api/quit` 重启验证。
- 测试结束仅把测试设置的 closeAction 改回 ask；保留用户当前语言、主题与自动更新选择。用户查询缓存和下载数据未清空。

Observed native light/dark title colors and the close dialog at 200% scaling. A real Ahri/Mid query survived tray minimize and relaunch restoration in the same window. Remembered tray behavior skipped the dialog on a subsequent X click. The real close API exited the process, released the port and persisted Exit, visible after restart. Test close preferences were reset to Ask while preserving other current preferences.

部分桌面自动点击因用户同时操作或窗口坐标不可用而中止；这些点击没有被计为通过。未自动完成托盘图标双击和右键菜单的视觉点击验收；其恢复/退出共用已验证的窗口生命周期处理。未实测 Windows 10、x86/ARM64、多屏切换、高对比度或真正关机/注销。对应逻辑有测试，不能替代各平台实机验收。

Some desktop inputs were interrupted by concurrent user input or unavailable coordinates and are not counted as passes. Tray-icon double-click/context-menu visual clicks were not completed; they share the validated restore/exit handlers. Windows 10, x86/ARM64, monitor moves, high contrast and actual OS shutdown/logoff still need platform checks.

## 包与范围 / Packaging and scope

复用已安装的微软 WebView2 Runtime，运行包只增加 SDK DLL/加载器及许可证。新增 cache/webview2 属于浏览器配置空间；启动异步清理可再生成的磁盘/站点缓存，不删除 localStorage 或英雄数据。旧 Edge 最近查询不会自动导入。

Uses the installed WebView2 Runtime; only SDK assemblies/loaders/licenses are bundled. Its profile is additional disk space. Startup clears disposable browser caches, never recent-query localStorage or matchup data. Old Edge history is not imported.

本版为本地修订，未提交、推送或发布 GitHub。数据获取/排序/分路策略不变。第一次检查由 PowerShell 7 派生环境触发 Get-FileHash 不可用；SDK 校验已改用 .NET，完整检查在项目指定的 Windows PowerShell 下通过。

Local revision only; no commit, push or GitHub release. Query/update algorithms are unchanged. An initial PowerShell-module availability issue was resolved by using .NET hashing and the documented Windows PowerShell test environment.

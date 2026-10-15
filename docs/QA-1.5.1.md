# Luna 1.5.1 本地验证 / Local verification

2026-10-15 · FrancisChen

## Scope / 范围

统一页头按钮和搜索区域的圆角焦点样式，保留键盘和 Windows 高对比度可见性；网页 theme-color 随浅色、深色、系统主题更新。Windows 标题栏通过已有的精确窗口识别服务同步颜色：只处理匹配本安装目录、端口和 Edge 应用标识的 Luna 窗口，不按标题猜测，不修改普通浏览器窗口。原生主题处理在后台线程运行，属性不支持时保留系统外观。退出时仅恢复仍由本进程持有的标题栏属性。

Rounded header/search focus styling, a theme-color hint and optional native Windows caption synchronization. Native changes are restricted to the existing exact Luna app-window identity. Unsupported attributes preserve system behavior; high contrast uses system caption colors. Queries retain their local execution path.

## Verification / 验证

完整检查 `scripts/check.ps1` 的 16 个步骤通过，记录位于本地 `dist/q151/report.json`，开始于 2026-10-15 14:22:54，完成于 14:25:54（Asia/Shanghai）：

- 84 项 JavaScript 测试通过，覆盖语言、主题、搜索、分路、查询恢复及来源标识；新增验证页面主题色随系统/手动选择同步，保存失败保持原值。
- 8 套 C# 测试通过；设置套件新增标题栏主题优先级、系统主题与高对比度策略。后台更新、恢复、损坏回退、缓存清理、单英雄更新、图标与长路径检查通过。
- 两轮真实回环 HTTP 分别核对全部 856 条随包查询，响应与快照一致。初次构建中位 3.90 ms / P95 6.94 ms / 最大 16.34 ms；解压运行包中位 3.95 ms / P95 6.97 ms / 最大 15.52 ms。
- 自动更新模拟测试中，并发续传/发布期间 669 次本地读取最大 6.63 ms；这不是外网上游下载速度。
- Windows 构建、测试工具、打包、包清单/哈希验证和源码 ZIP 解压重建均通过。

All 16 Windows verification steps passed, including 84 JavaScript tests, eight C# suites, two complete 856-record loopback HTTP comparisons, package checks and a source ZIP rebuild. HTTP median/P95/max were 3.90/6.94/16.34 ms for the first build and 3.95/6.97/15.52 ms for the extracted runtime. The simulated concurrent updater completed 669 local reads with a maximum of 6.63 ms. These are local test measurements, not UI rendering, cold-start or external-download guarantees.

前两类检查限制也保留记录：受限环境不能清理测试子进程，改用正常 Windows 权限运行隔离检查；较长的测试输出路径导致 StorageTests 临时样本写入失败，改用较短的 `dist/q151` 后全部通过。最初漏改的英文版本提示键已经修正并重新检查。未修改用户缓存，也未因这些工具限制改变查询或清理算法。

Earlier attempts encountered restricted test-process cleanup and an overly long temporary fixture path. Running the isolated checks with normal Windows permissions and a shorter output path resolved those verification constraints. An initially missed English version label was corrected before the passing run. Personal cache and product query/cleanup behavior were not changed.

## Visual review limit / 外观验收限制

本轮桌面检查工具因无法确认 Edge 独立窗口的当前 URL 而中止。未绕过限制，未把单元测试、DWM 属性调用或旧截图当作此次实际窗口外观验收。圆角、标题栏颜色、系统缩放和高对比度的最终可见效果仍需用户实机确认。已有 1.5.0 截图仅为历史界面示例。

Desktop inspection stopped because the tool could not confirm the Edge app window URL. This restriction was not bypassed. Unit tests, native attribute calls and historical screenshots do not establish visual correctness. Final appearance across scaling and high-contrast settings remains pending human verification.

本次 1.5.1 为本地修改，尚未提交、推送或发布至 GitHub。审查时远端 main 为 b98a0c997091ace08c4efa6aae40175301861eb7，公开最新标签为 v1.5.0-dev.1；原有第三方分发核查状态不变。

Version 1.5.1 is local and unpublished. The reviewed remote main was b98a0c997091ace08c4efa6aae40175301861eb7, with v1.5.0-dev.1 as the latest public release. Existing third-party distribution review remains unchanged.

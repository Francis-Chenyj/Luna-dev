# Real UI acceptance / 真实界面验收

Use a freshly extracted runtime in a new writable folder. Retain the exact source identity, Windows/WebView2 versions and evidence. Tests below complement DOM-state unit tests; mock focus/scroll methods do not prove these behaviors.

`tests/browser-smoke.mjs` is an executable real-browser regression for scope reload/history, translated accessible labels, keyboard tabs, local documents, Escape focus, counts and layout. Call `runBrowserSmoke(tab, exactIsolatedOrigin)` with a Browser Use tab; it uses only browser locators and read-only DOM inspection. `inspectLayout` can also run after each viewport/theme/language change. It is separate from the default Windows CI job, which cannot be assumed to provide an interactive browser. Record its returned checks alongside the native `-Desktop` smoke result.

| Scenario / 场景 | Acceptance / 预期 |
| --- | --- |
| First launch / 首次启动 | Clear search entry; bundled default profile queries work before downloads finish. |
| Search / 搜索 | Chinese, English, aliases and keyboard candidates select the intended hero; empty/unknown text has a clear response. |
| Lanes / 分路 | Detail searches retain the active lane; missing lane explains choices; recent/home selection picks the normal default. |
| Scope/history / 范围与历史 | Test all eight options, reload, back/forward and copied URL; preserve hero/lane/filters/scope. Scope-only changes cause no detail requests. |
| Missing profile / 缺少组合 | Exact current champion/profile download is offered online; both columns share one task. Offline mode explains how to recover defaults. |
| Languages / 语言 | Switch during a query. Close-dialog icon has a translated accessible name; 0/1/2 champion counts are grammatical. |
| Theme / 主题 | System/light/dark follow the chosen setting across restart. Text and selected states remain legible. |
| Native close / 关闭 | Cancel stays open; tray retains query; reopening restores it; explicit exit ends the service. |
| Dialogs / 对话框 | Keyboard navigation, focus, Escape and scroll work in real WebView2; verify at 720 px and normal desktop width. |
| Storage / 空间 | Pending measurement explains an active download/other-window lock; existing totals remain visible. |
| Interrupted update / 中断升级 | Preserve bad journal; verify installed files or rollback; no mixed unverified app is allowed to continue. |
| Deep folder / 深层目录 | Actual maintenance EXE verifies >260-character owned paths and uninstall retains user files/settings. |
| Upgrade health / 升级健康检查 | Native browser and application initialization plus offline query must pass. Intentionally broken app.js must fail. |

Record PASS/FAIL/not tested for each scenario; do not infer Windows 10, ARM64, a clean-machine install or GitHub/CDN migration from one local Windows 11 session. Formal publication also requires `scripts/check-release-readiness.ps1 -ForPublication` and a separately recorded public-channel upgrade drill.

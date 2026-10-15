# Luna 1.6.2 / 自动更新体验

Local review build by FrancisChen. No GitHub Release, tag or asset is published by this delivery.

Enabling automatic downloads saves automatic checking in the same operation and starts a background check immediately. Enabling checks alone also checks immediately. Disabling checks disables automatic downloads and cancels automatic work; disabling downloads keeps version checks. Download progress survives cancellation. Explicit manual downloads remain available with both switches off.

Existing work is reused. Rapid off/on during cancellation retains one check request. The delayed startup task reads current preferences and respects Pause. Offline and command-line manual-only sessions store preferences without starting automatic network traffic. Installation still requires Restart and update and confirmation, with settings/data preservation and rollback.

The README editions explain the exact startup interval, manual exceptions and publication requirements. See [verification scope](QA-1.6.2.md).

## 简体中文

作者 FrancisChen，本地审阅版本。本次没有发布 GitHub Release、标签或附件。

开启自动下载时一次保存检查和下载两个开关，并立即在后台检查；单独开启检查也立即检查。关闭检查会关闭自动下载并停止自动任务；单独关闭下载保留版本检查。取消保留下载进度，手动下载不受自动开关限制。

复用进行中的任务，取消期间快速重开会保留一次检查请求。启动延迟任务读取最新设置并尊重暂停。离线及命令行仅手动模式保存设置但不自动联网。安装仍需点击并确认“重启并更新”，保留设置与数据，失败回退。

中英文 README 同步说明调度间隔、手动例外和正式发布前提。测试范围见[验证记录](QA-1.6.2.md)。

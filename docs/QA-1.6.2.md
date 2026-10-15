# Luna 1.6.2 verification / 验证

Date: 2026-10-15. Author: FrancisChen. Local review; no GitHub publication.

## Automated checks

The lifecycle suite verifies atomic check/download preferences, immediate checks despite a recent successful check, nonblocking saves, request deduplication, rapid off/on during cancellation, download cancellation, manual-download independence, offline/manual-only modes, failure cooldown and Pause. It also covers signed archives, Range resume, verified-package reuse, rollback, interrupted transactions, local-query integrity and storage boundaries.

UI tests verify both switches reflect server results, progress refreshes immediately without a duplicate check request, failed saves restore controls, Chinese/English launch-mode notices are accurate, and an installed update is not advertised as still available. Settings regression also passes (theme, language, concurrent saves, old configuration and recovery).

## Short sessions and real update

Three separate processes ran for approximately 30 seconds each against a deliberately throttled loopback package server. Session 1 saved 6,324,224 bytes. Session 2 requested exactly that offset and completed the package. Session 3 requested neither metadata nor package bytes. The 341 simultaneous local matchup requests all succeeded: median 5.27 ms, P95 6.49 ms, maximum 20.10 ms. These are local HTTP result timings, not complete UI paint measurements or estimates of GitHub speed.

The native WinForms/WebView2 UI was used to turn automatic downloads on from both switches off. Both switches enabled; checking and downloading began without a second check click. The ready update required Restart and update and confirmation. The real maintenance process installed the signed package, reopened the unmodified 1.6.2 executable and passed its startup/catalog/matchup health check. The settings file and an extra personal file retained identical SHA-256 hashes; a 1.6.1 rollback copy remained. The follow-up fix makes the restarted app report installation success instead of a stale available-update state.

Fixture boundary: the predecessor uses this revision's update code with a lower test version (1.6.1), local transport delegates and the production public verification key. This is not an unmodified historical 1.6.1 build. The downloaded target is the signed, unmodified runtime package. Signature, archive, file verification and the maintenance handoff are not bypassed. Test transport code lives only in tests and is absent from the production Windows package. No release was uploaded; the public GitHub/CDN path was not exercised. The final package is checked separately for file integrity and offline queries.

Detailed session and native results are delivered under `verification/` alongside the local release. Earlier versions' wider Windows/DPI tests are not claimed as new runs here.

## 中文

生命周期测试覆盖开关原子联动、近期已检查后开启仍立即检查、设置保存不等网络、请求去重、取消期间快速重开、停止自动下载与手动下载独立、离线/仅手动模式、失败间隔与暂停。原有签名、续传、包复用、回退、中断恢复、本地查询与空间边界也一并覆盖。

界面测试验证两个开关与服务端一致、立即刷新状态且不重复检查、保存失败还原控件、中英文模式提示，以及安装完成后不再显示旧的“发现新版”。设置回归测试也通过，包括主题、语言、并发保存、旧配置与损坏恢复。

三次独立进程各运行约 30 秒，下载来自刻意限速的本机服务。第一次保留 6,324,224 字节；第二次从该位置继续并完成；第三次没有重复请求元数据或程序包。期间 341 次本地查询全部成功，中位数 5.27 ms、P95 6.49 ms、最大 20.10 ms。这是本地接口返回时间，不代表完整界面绘制耗时或 GitHub 下载速度。

原生桌面界面中，两个开关均关闭时开启自动下载，两者一起开启且自动开始检查、下载，无需再点击检查。下载完成后实际点击“重启并更新”和确认，独立维护程序安装签名包并重新打开未经改动的 1.6.2 程序，本地启动、目录与克制查询检查通过。设置及额外个人文件的 SHA-256 均未变化，保留了 1.6.1 回退副本。随后修复了升级后仍显示旧“发现新版”的提示问题。

测试边界：前驱副本使用本次更新代码，但版本标记设为测试用 1.6.1，更新网络传输替换为本机服务；它不等同于历史原版 1.6.1。目标为签名的完整运行包，没有绕过签名、哈希、路径校验或维护程序。测试入口只在 tests 中，正式 Windows 包不包含测试传输逻辑。本次未上传 GitHub，也未验证公网 GitHub/CDN 链路。最终交付包另做包完整性与离线查询检查。

详细结果保存在交付目录的 `verification/`。旧版本的其他 Windows/DPI 验收不算作本次重新测试。

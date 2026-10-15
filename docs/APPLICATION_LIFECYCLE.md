> 1.6.3 adds native WebView2/app readiness, preserved corrupt-journal recovery and deep-path maintenance. See [current repair and verification guide](AUDIT_REPAIRS.md). Historical session examples below keep their original versions.

# Application lifecycle / 程序维护

## Maintainer workflow

Version 1.6.2 couples `appAutoDownload => appAutoCheck` in one durable settings write. Enabling either schedules a check after releasing the settings mutex. One worker is reused; an enable arriving during cancellation retains one pending check. Automatic and explicit manual operations are distinguished. Startup rereads preferences after its delay and respects Pause. Legacy check-off/download-on preferences normalize to both off. Offline and `--no-auto-update` do not initiate automatic requests on preference changes. Installation receipts normalize the post-restart availability state.

`tests/prepare-app-update-session.ps1` creates an isolated lower-version fixture with loopback transport hooks; `tests/app-update-sessions.mjs` runs three 30-second sessions. The hooks are compiled only into the test executable, not the production runtime. Use a local signed package; no GitHub publishing is necessary for this test. The fixture predecessor contains current update logic, so it is not a historical executable compatibility test.

`scripts/package.ps1 -OutputDirectory <new-empty-directory>` builds the app and independent maintenance executable. The Windows ZIP includes `installed-files.json`, an ownership inventory with hashes. Source folders are ineligible for in-place upgrade/uninstall.

Sign a release with `scripts/sign-release.ps1 -ReleaseDirectory <directory> -PrivateKeyPath <outside-repository-private-key.xml> -Notes <release-notes>`. The private key must be outside source and release directories and match `src/UpdateTrust.cs`. Keep a separate secure backup; losing it prevents installed copies from trusting future updates. This RSA update signature is not Windows Authenticode signing.

Publish the Windows ZIP and `luna-update.json` together in a **stable** Release in `Francis-Chenyj/Luna-dev`, tagged `v<version>`. Never replace an already-published version's contents; use a higher version. Build/sign scripts do not upload. This 1.6.2 delivery remains local review.

Only HTTPS and specified GitHub download hosts are accepted. A valid publisher signature, signed ZIP hash/length, safe archive paths and per-file inventory are required. Tests generate independent ephemeral keys; the production private key is never packaged.

Installation is explicit. An independent helper waits for the parent and refuses another same-folder Luna instance, backs up owned files, journals replacement, tests the new local startup/query, and commits or restores. An interrupted transaction is recovered by an external helper at next launch. This version does not migrate data to an incompatible schema. Future incompatible changes require reversible data migration.

Uninstall removes only unchanged files in the ownership inventory; modified and extra files survive. Explicit full-data removal applies to the selected copy's entire `cache` and does not traverse links. Keeping user data still removes program-update staging and rollback binaries. Windows uninstall registration is per-user and per-installation path. Shared runtimes are retained.

`tests/run-lifecycle.ps1` covers archive/signature defenses, rollback, retention, accounting and resume. `tests/run-short-sessions.ps1 -RuntimeDirectory <clean-runtime> -OutputDirectory <new-directory>` runs three packaged offline 30-second native sessions and three native sessions of the same source with controlled upstream replies. Production request spacing is retained. These tests do not predict public-server download speed or claim reboot-cold OS caches.

## 维护者流程

打包脚本构建主程序和独立维护程序，运行 ZIP 的 `installed-files.json` 记录程序拥有的文件和哈希；源码目录禁止原位升级和卸载。

使用签名脚本传入发布目录、仓库与发布目录之外的私钥及更新说明。私钥应单独安全备份，必须匹配 `src/UpdateTrust.cs`。这是更新元数据签名，不等同于 Windows 代码签名。正式私钥不会进入程序包，测试使用临时测试密钥。

正式 Release 需同时上传 Windows ZIP 和 `luna-update.json`，标签为 `v<版本>`。同一公开版本不原地替换内容。构建、打包和签名不会自动上传；本次仍为本地审阅交付。

程序验证发布者签名、ZIP 大小和哈希、清单及路径。安装始终需要用户确认。独立维护程序等候退出，发现同目录其他实例则停止；备份、记录替换事务、检查新版本地启动与查询，失败恢复旧程序。异常中断后的下次启动由外部维护程序恢复。本版不改变用户数据格式；以后增加不兼容迁移时必须提供可恢复的数据方案。

卸载保留额外文件及修改过的程序文件，勾选完整清除时删除所选副本的整个 cache，不穿越目录联接。保留数据时仍清除程序更新暂存与回退副本。共享 WebView2/.NET 不删除。

后台校验进度只用于调度，实际查询始终验证记录。异常文件隔离区限 20 份、10 MiB、7 天；归属不明或可能用于恢复的数据保守保留，目录没有绝对硬容量上限。

短会话测试实际启动六次原生窗口，每次约 30 秒；三次为发布包离线运行，另三次使用相同源码及受控来源，验证落盘进度和重复下载。计时不代表公网速度，也不代表操作系统刚重启后的冷缓存。

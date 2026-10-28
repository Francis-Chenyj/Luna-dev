# Luna 1.7.1 — development pre-release / 开发预发布

Release date / 发布日期: 2026-10-28. [GitHub v1.7.1](https://github.com/Francis-Chenyj/Luna-dev/releases/tag/v1.7.1) distributes this build through the existing development pre-release channel. The underlying heading repair was verified locally on 2026-10-27.

This release also brings the previously local 1.6.x and 1.7.0 work to GitHub: optional BP champion synchronization, instance-owned query/update/storage services, typed fault-test fixtures, native-window improvements and update-recovery fixes. See the [changelog](../CHANGELOG.md) for the complete version history.

此版本把此前本地的 1.6.x、1.7.0 改进一并同步到 GitHub，包括可选 BP 英雄同步、查询／更新／存储服务独立持有状态、故障测试维护、原生窗口与升级恢复修复。BP 同步需要主动连接；只展示客户端可见的英雄选择，点击头像后查询本地严父／大儿，不查询玩家战绩或段位，也不自动选禁英雄。

Clicking a BP portrait previously moved focus to a heading whose outline crossed the champion title. The repaired heading has enough line spacing, leaves pointer selection unobstructed and uses an underline for keyboard focus. Focus transfer, forced-color visibility and return to the original BP slot remain available.

点击 BP 头像后，旧版名称焦点框会压到英雄称号。1.7.1 调整行高，去掉鼠标选择后的包围框，并以键盘下划线保留清晰的焦点提示；返回 BP 和高对比度支持继续可用。

Program file/product versions, the main header and About page, release metadata, development package metadata and current English/Chinese guides identify this revision as **1.7.1**. Known help/manifest exceptions in the original package are listed below. The patch retains 1.7.0 query behavior, settings and saved-data formats. Existing optional BP synchronization remains off on startup.

The browser suite adds 52 heading-focus scenarios to existing BP-board and results-page coverage. See the [verification scope](QA-1.7.1.md) for what has and has not been established. The standalone local acceptance tool, its source, browser profile, personal session reports and demonstration media are excluded from both packages and this repository.

## Downloads / 下载文件

### Known text inconsistencies / 已知文案遗漏

The original 1.7.1 packages still show 1.6.6 in the help dialog and retain browser-era close-window wording; their Windows application manifest also retains 1.6.4.0. The executable product/file version, header and About page are 1.7.1. These are text/metadata inconsistencies, not evidence that the package is 1.6.6. They are corrected on the default branch after this release and covered by an expanded version check. The published tag and assets remain unchanged; see [the 1.7.2 release](RELEASE-v1.7.2.md).

原 1.7.1 下载包的使用说明仍显示 1.6.6，并沿用浏览器模式的关闭说明；Windows 应用清单还保留 1.6.4.0。程序文件、主页与关于页版本为 1.7.1。这些文案和元数据遗漏已在发布后的主分支修正，修正随后装入 1.7.2，原标签及下载包不覆盖；原生窗口实际支持托盘／退出，只有浏览器和无窗口模式使用闲置超时。

### Assets / 附件

- `Luna-v1.7.1-Windows.zip`: complete Windows runtime; extract the whole archive. Normal use needs no Node.js or development tests.
- `Luna-v1.7.1-Source.zip`: source, build scripts, ordinary project regression tests and documentation; no compiled executable or standalone acceptance tool.
- `SHA256SUMS.txt` and `release-files.json`: verify the exact packages. `docs/build-provenance.json` inside the packages identifies the shipped source.

Fully exit Luna, including its tray process, before replacing an existing runtime. Retain `cache` to preserve settings, downloaded libraries and recent queries; see the [user guide](USER_GUIDE.md). When upgrading a source checkout, keep the source directory and rebuild its program files.

This is a manual-install development pre-release, not a publisher-signed stable automatic update; no `luna-update.json` is provided. `publicReleaseReady` remains false because the existing third-party/Riot review and publisher-signing prerequisites remain open. Availability on GitHub does not establish those approvals. Historical 1.7.0 acceptance reports retain their original version identities.

这是需要手动安装的开发预发布，不进入稳定版自动更新渠道。请下载并完整解压 Windows ZIP，保留原有 `cache`。程序、界面和附件均为 1.7.1；第三方材料与发布签名待办仍保留，不因上传 GitHub 而标记为已完成。

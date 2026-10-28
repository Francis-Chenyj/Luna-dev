# Luna 1.7.2 — development pre-release / 开发预发布

Release date / 发布日期: 2026-10-29. [GitHub v1.7.2](https://github.com/Francis-Chenyj/Luna-dev/releases/tag/v1.7.2) continues the manual-install development pre-release channel.

## Changes / 本次变化

The original 1.7.1 package retained an old help-dialog version, browser-era closing instructions and Windows application manifest version. Version 1.7.2 packages those corrections: executable, manifest, main header, About, help, development metadata and current guides now agree. Closing the native window asks whether to minimize to the tray or exit unless a choice was remembered; browser/headless idle timeout is described separately.

1.7.2 将此前主分支的文案与元数据修正正式装入新包：统一程序、应用清单、主页、关于页、使用说明和当前文档的版本；修正原生窗口的关闭说明，明确托盘／退出选择及浏览器模式的区别。中英文主页使用从本版实际运行界面截取的新图片。

The current release checklist, user/developer guides, test inventory, issue-form version example and Chinese Markdown formatting are synchronized. The README explicitly credits OpenAI Codex as an AI development collaborator; FrancisChen remains the creator and maintainer. Historical release records, tags and assets keep their original identities.

Queries, optional BP synchronization, the 1.7.1 heading-focus repair, settings and saved-data formats remain compatible. BP synchronization stays off on startup and reads visible champion selections only; this patch adds no player-history access or new LCU endpoints.

查询、可选 BP 同步、1.7.1 名称焦点修复、设置及已有数据格式保持兼容。本次版本与文档修正不代表新增 BP 能力或重新完成完整十人真实对局验收。

## Verification / 验证

The existing version regression now checks help text in both languages and the Windows manifest as well as the other version fields. The release workflow includes the complete Windows suite, browser layouts, exact-package native startup and upgrade/rollback from the original published 1.7.1 runtime. See [verification scope and limits](QA-1.7.2.md) and the [CI runs](https://github.com/Francis-Chenyj/Luna-dev/actions). Use the run for the tagged commit; historical passing runs do not validate a new package.

## Downloads / 下载文件

- `Luna-v1.7.2-Windows.zip`: complete Windows runtime; extract the whole ZIP and launch `Luna.exe`.
- `Luna-v1.7.2-Source.zip`: source, build scripts, ordinary project tests and documentation, without a compiled executable.
- `SHA256SUMS.txt` and `release-files.json`: archive checksums and file inventory. The packaged `docs/build-provenance.json` records the shipped source identity.

Fully exit Luna, including its tray process, before replacing an existing runtime. Retain `cache` for settings, downloaded libraries and recent queries, or copy it into a newly extracted runtime. Keep source checkouts in place and rebuild them. See the [user guide](USER_GUIDE.md) / [用户指南](USER_GUIDE.zh-CN.md).

独立验收程序及其源码、个人报告、浏览器配置、录像和 GIF 不进入项目或任何发布包。普通项目回归测试保留在源码包中。原 1.7.1 标签和附件不覆盖。

This is a manual-install development pre-release, not a publisher-signed stable automatic update; no `luna-update.json` is supplied. Existing third-party/Riot review and publisher-signing prerequisites remain open, so `publicReleaseReady` remains false. GitHub availability does not establish those approvals.

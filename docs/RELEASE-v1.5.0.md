# Luna v1.5.0-dev.1 — development pre-release / 开发预发布

**Author / 作者：FrancisChen · Date / 日期：2026-10-13（Asia/Shanghai）**

Luna is in active development. This pre-release uses application version 1.5.0 and GitHub tag `v1.5.0-dev.1`. Download the four attachments from [Releases](https://github.com/Francis-Chenyj/Luna-dev/releases). The third-party distribution review in [release status](../release-status.json) remains open; publication does not imply approval from data providers or Riot.

Luna 仍在开发中。本次为开发预发布，程序版本为 1.5.0，GitHub 标签为 `v1.5.0-dev.1`。四个附件统一放在 [Releases](https://github.com/Francis-Chenyj/Luna-dev/releases)；[分发状态](../release-status.json)中的第三方授权与 Riot 注册仍待处理，发布不代表取得上游或 Riot 认可。

## Changes / 变化

- Offline Chinese/English search, lanes and rank filters; the default profile covers 173 champions, 428 valid champion–lane combinations and 856 directional records. / 离线中英文查询、分路与分段筛选；默认库覆盖 173 位英雄、428 个有效英雄分路组合及 856 条方向记录。
- Settings combines General, Data, Support and About. / 统一设置入口，包含常规、数据、排错、关于。
- English, Simplified Chinese and system language; light, dark and system themes. Preferences persist, and switching preserves the active query. / 持久保存语言和主题，切换时保留当前查询。
- A next-launch startup data-update switch, with existing manual updates and offline restrictions. / 启动数据更新开关下次启动生效，保留手动更新和离线限制。
- Local log/data folder shortcuts and limited diagnostic copying. / 增加本地目录入口和精简诊断复制。
- Offline About documents distinguish original-code MIT from third-party terms. / 关于页提供离线文档，区分原创代码 MIT 与第三方条款。
- Fix champion titles overlapping relationship values at widths of 721–1000 pixels in both languages. / 修复中英文界面在 721–1000 像素宽度时英雄称号与关系数值重叠。
- Fix the three welcome steps remaining in Chinese in English mode. / 修复英文首页三个引导步骤未翻译的问题。
- Reliably return rejection responses for invalid request bodies with bounded connection teardown. / 修复拒绝非法请求体时偶发的连接重置，保留原有请求限制。
- Include bilingual issue forms, a prepared Windows build/test workflow and a local verification entry point; no automatic publication. / 纳入双语反馈表单、Windows 构建测试工作流和统一本地检查入口，不自动发布。
- Align bilingual operation/build guides, migration instructions, file boundaries and runtime acceptance records. / 同步双语操作与构建指南、迁移说明、文件边界和运行包验收记录。

See the [changelog](../CHANGELOG.md) and [QA record](QA-1.5.0.md) for scope and verification.

## Requirements and downloads / 要求与下载附件

Windows 10 / 11, .NET Framework 4.7.2+ and a modern browser; an Edge app window is preferred. Normal use needs no Node.js, Python, API key or administrator privileges. Actual tested environments are recorded in QA.

运行目标为 Windows 10 / 11、.NET Framework 4.7.2+ 和现代浏览器，优先使用 Edge 独立窗口。普通使用不需要 Node.js、Python、API Key 或管理员权限；实测环境以 QA 记录为准。

- `Luna-v1.5.0-Windows.zip`: executable, interface, bundled data and documentation. / 普通用户运行包。
- `Luna-v1.5.0-Source.zip`: source, scripts, tests, bundled data and documentation; no executable. / 源码包，不含 EXE。
- `SHA256SUMS.txt`: package and manifest hashes. / 校验文件。
- `release-files.json`: per-file package inventory. / 逐文件清单。

The selected artifacts are generated in `dist/v1.5.0-dev.1-r2/`. Earlier local review ZIPs remain unchanged. Identify this build using its companion checksums and per-file inventory. Both ZIPs include third-party data/assets with unresolved distribution review. The executable is not code-signed.

本次附件生成于 `dist/v1.5.0-dev.1-r2/`，此前本地审阅包保留原样。请用本次校验文件和逐文件清单识别构建；两个 ZIP 均包含分发条件仍待核实的第三方数据与素材。EXE 尚未进行代码签名。

## Install and upgrade / 安装与升级

1. Fully extract the Windows ZIP to a writable folder; keep the EXE, config, `web` and `data` together. / 将 Windows ZIP 完整解压到可写目录，保留 EXE、配置、`web` 和 `data`。
2. Run `Luna.exe` / `启动Luna.cmd`; use `离线启动Luna.cmd` to prohibit external updates. / 普通入口允许数据更新；离线入口禁止外网更新。
3. Before upgrading, use **Exit** in the old app, retain its directory, and **copy** its `cache` into the new folder before launching. / 升级前用程序内“退出”结束旧服务，保留旧目录备份，首次启动前复制 `cache`，保留设置与已保存数据。
4. Confirm version, preferences and queries, then recreate the shortcut if needed. / 确认版本、偏好和查询正常后，按需重建桌面快捷方式。

Recent queries are browser-local and are not transferred to another browser/port by copying `cache`. Closing the window alone leaves the idle timeout active; **Exit** stops the service immediately. Data updates do not upgrade the application. Logs are in `cache/logs`; older `cache/error.log` is retained.

最近查询属于浏览器存储，复制 `cache` 不会把它们迁移到另一浏览器或端口。只关闭窗口会保留空闲退出计时；程序内“退出”立即停止服务。数据更新不会升级程序本身。新日志位于 `cache/logs`，旧版 `cache/error.log` 原样保留。

See the [user guide](USER_GUIDE.md) / [用户指南](USER_GUIDE.zh-CN.md), [developer guide](DEVELOPER_GUIDE.md) / [开发指南](DEVELOPER_GUIDE.zh-CN.md) and [release checklist](RELEASE_CHECKLIST.md).

Author / 作者：**FrancisChen** · Project / 项目：[Luna](https://github.com/Francis-Chenyj/Luna-dev)。

# Luna 发布核对清单 / Release checklist

核对日期 / Checked: 2026-10-28 · 维护者 / Maintainer: FrancisChen

## 当前公开版本 / Current published version

当前下载是 [Luna 1.7.1 开发预发布](https://github.com/Francis-Chenyj/Luna-dev/releases/tag/v1.7.1)，程序版本为 `1.7.1`。这是完整产品的开发预览，需手动安装；没有 `luna-update.json`，不进入已签名稳定版自动更新渠道。

The current download is the complete 1.7.1 development pre-release, installed manually. It is not an authenticated stable-channel update. The tag points to source commit `5cab2d64c15212fd68d8c858477aca8d0941e829`; `docs/build-provenance.json` inside each package identifies its exact source content.

| 附件 / Asset | 内容 / Contents |
| --- | --- |
| `Luna-v1.7.1-Windows.zip` | 完整 Windows 运行包 / complete Windows runtime |
| `Luna-v1.7.1-Source.zip` | 源码、普通项目测试及文档，不含 EXE / source, project tests and docs, no EXE |
| `SHA256SUMS.txt` | ZIP 和逐文件清单的 SHA-256 / ZIP and inventory checksums |
| `release-files.json` | 包内路径、大小和哈希 / packaged paths, lengths and hashes |

`main` 可以包含发布后的修正。当前主页截图以及帮助文案和维护指南的后续修正，不表示原下载包已被替换；[更新日志](../CHANGELOG.md)把未打包修正与已发布记录分开。不要覆盖已公开标签或附件；下一份程序下载包使用更高版本。

The default branch can advance after publication. Later screenshot, help-text and documentation fixes do not alter the existing tag or assets. See [release notes and known text inconsistencies](RELEASE-v1.7.1.md) and the [changelog](../CHANGELOG.md). Publish a higher version for a new binary package.

## 当前验收流程 / Current verification workflow

1. 在隔离源码副本执行 `scripts/check.ps1 -Browser`：23 个基础检查组加 1 个浏览器组，包括 14 组 C# 回归、全部默认离线记录比对、打包清单核验、运行恢复检查和源码包重建。环境及命令见[开发指南](DEVELOPER_GUIDE.zh-CN.md) / [developer guide](DEVELOPER_GUIDE.md)。
2. 用最终包执行原生窗口及历史版本升级检查，按 [UI acceptance](UI_ACCEPTANCE.md) 记录实际人工体验。模拟 BP 和浏览器尺寸测试不能替代完整十人真实对局、多设备或原生 DPI 验证；本版本证据范围见 [QA](QA-1.7.1.md)。
3. 上传前检查两种包的逐文件清单、哈希和构建来源；上传后核对 Release 标签、附件哈希、双语页面、图片和反馈表单，并查看对应提交的 [Windows CI](https://github.com/Francis-Chenyj/Luna-dev/actions)。已发布 1.7.1 的 [CI 记录](https://github.com/Francis-Chenyj/Luna-dev/actions/runs/40541001359)与之后主分支的运行分别识别。
4. 保留个人缓存、独立 BP 验收工具及源码、个人验收报告、录像、GIF、签名私钥和本机构建输出在项目发布范围之外。普通项目 `tests/` 应保留；它不是被排除的独立验收工具。
5. 稳定版发布前，按[第三方核查](THIRD_PARTY_REVIEW.md)补齐证据，并核对签名与分发要求。当前 `publicReleaseReady = false` 是证据状态，不是 GitHub 可见性；公开开发预览不代表取得第三方许可。

Run the complete isolated checks, inspect the final package and its actual UI, verify hosted CI and release hashes, and retain explicit limits for real-client/device coverage. Keep standalone acceptance tools, personal reports/media, caches and private keys out of the repository and packages. Technical checks do not resolve the remaining third-party and signing requirements.

## 历史记录边界 / Historical record boundary

下文保留 2026-10-13 的 1.5.0 清单及其后本地修订备注，均只描述当时状态；其中的“当前”“尚未上传”与旧附件名不指今天的 1.7.1。历史文件和 QA 不能冒充对新包重新执行过的测试。

The following archive describes the original 1.5.0 preview and dated local follow-ups. Its version labels, upload status and test records are historical, not current release instructions.

<details>
<summary>展开历史 1.5.0 清单 / Expand the historical 1.5.0 checklist</summary>

## 历史记录：1.5.0 / Historical 1.5.0 record

> 2026-10-15：新增的 1.5.1 是本地审阅版，尚未推送或发布，见 [1.5.1 发布说明](RELEASE-v1.5.1.md)和 [QA](QA-1.5.1.md)。下文保留上一次 1.5.0 公开预发布的文件边界与验收记录，不代表 1.5.1 已上传。
>
> The 1.5.1 revision is local and unpublished. The checklist below retains the preceding 1.5.0 public pre-release record; it does not claim publication of 1.5.1.

维护者 / Maintainer：**FrancisChen** · 日期 / Date：2026-10-13（Asia/Shanghai）

本清单定义当前完整版本的文件范围和验收顺序。它不表示已经发布，也不替代[第三方核查记录](THIRD_PARTY_REVIEW.md)。产品名为 **Luna**，仓库为 **Francis-Chenyj/Luna-dev**，原创代码使用 MIT。

This checklist covers the complete local build, not a separate demo edition. It records file boundaries and acceptance steps; it does not authorize distribution or imply a GitHub upload.

## 1. 当前开发预发布 / Selected pre-release

- GitHub 标签为 `v1.5.0-dev.1`，程序内版本仍为 `1.5.0`，标记为 Pre-release。
- 本次附件目录为 `dist/v1.5.0-dev.1-r2/`。旧审阅包保留，不覆盖。
- 以同一目录的 `SHA256SUMS.txt` 和 `release-files.json` 识别文件，包名相同不代表内容相同。
- 本次本地自动检查记录为 `dist/ci-publish-20261013-r2/`；云端检查以仓库 Actions 对应提交的结果为准。
- 先前的人工界面、真实 Windows 缩放与迁移验收保留原日期及范围，见 QA；本次主页链接变更不冒充重复了全部人工验收。
- 第三方授权与 Riot 注册继续待办，`publicReleaseReady` 维持 `false`。这是证据核查状态，不是仓库是否公开。

The development pre-release tag is `v1.5.0-dev.1`, with application version `1.5.0`. Use the new artifact directory and its hashes; preserve older local archives. Automated results and historical manual acceptance are recorded separately. Third-party evidence remains open.

## 2. 仓库文件 / Repository contents

以源码包内 `Luna/` 的内容作为仓库根目录，不要直接上传整个工作目录，也不要再套一层 `Luna/`。

The contents inside the Source ZIP's `Luna/` folder form the repository root. Do not upload the whole working directory.

| 文件或目录 / Files | 用途 / Purpose | 分发范围备注 / Scope |
| --- | --- | --- |
| `src/` | C# 源码 / backend source | 原创代码 MIT |
| `web/` | 界面、翻译、搜索索引、图标 / interface and assets | 原创部分 MIT；来源数据与第三方内容保留各自条款 |
| `data/` | 目录、离线关系、别名、英雄头像 / bundled data | 当前完整包包含它们；分发证据尚待补齐 |
| `scripts/`、`tests/` | 构建、打包、维护、验证 / tooling and tests | 原创部分 MIT |
| `.github/` | 错误反馈、功能建议、Windows 工作流 / feedback and CI | 默认分支上传后核对实际生效 |
| `docs/` | 双语指南、发布资料、验证、来源记录与图片 / documentation | 第三方图片与示例不能因文档 MIT 而重新授权 |
| `README.md`、`README.zh-CN.md` | 英文首页与中文入口 / bilingual overview | README 介绍当前功能，不重复完整版本历史 |
| `CHANGELOG.md` | 版本变化 / version history | 对齐发布说明 |
| `LICENSE`、`THIRD_PARTY_NOTICES.md`、`release-status.json` | 原创许可、第三方范围、分发状态 / licensing and status | `publicReleaseReady` 当前仍为 `false` |
| `build.ps1`、`Luna.exe.config` | 构建与运行配置 / build and runtime config | 配置文件需要保留 |
| `启动Luna.cmd`、`离线启动Luna.cmd`、`创建Luna桌面快捷方式.cmd` | 用户入口 / launchers | 三个入口均随源码保留 |
| `.gitignore`、`.gitattributes` | 忽略与换行/二进制规则 / repository rules | 防止带入本机状态或破坏压缩样本 |

完整逐文件范围由最终 `release-files.json` 的 Source 清单给出。目录表便于审阅，不能代替逐文件清单。当前源码 ZIP 也含第三方数据，不能因为叫“源码包”就跳过分发核查。

The final Source inventory is the exact file list. The Source ZIP also contains third-party data; its name does not remove distribution-review requirements.

## 3. 下载附件 / Release attachments

| 附件 / Attachment | 面向对象 / Audience |
| --- | --- |
| `Luna-v1.5.0-Windows.zip` | 普通用户；完整解压后运行 / end users |
| `Luna-v1.5.0-Source.zip` | 开发者；包含源码、构建与测试，不含 EXE / developers |
| `SHA256SUMS.txt` | 校验两个 ZIP 和清单 / ZIP and inventory hashes |
| `release-files.json` | 两个包内的路径、大小、逐文件哈希 / per-entry inventory |

下载统一指向 [Releases](https://github.com/Francis-Chenyj/Luna-dev/releases)。以上四个文件作为开发预发布附件。`_build/` 是构建中间目录，验收报告也是本地材料，均不作为默认下载附件。

Download links are activated only after an actual Release exists. Build intermediates and machine-specific acceptance reports are not default download attachments.

## 4. 仅保留本地 / Local-only files

不上传或打包：`cache/`、`research/`、`dist/`、根目录生成的 `Luna.exe`、个人 `.lnk` 快捷方式、运行日志、`启动错误.txt`、临时文件、编辑器缓存、凭据和签名私钥。Windows ZIP 内重新构建的 EXE 是运行包的必要内容，与仓库根目录的生成文件不同。

Keep personal cache, research, outputs, root build products, shortcuts, logs, temporary files, credentials and signing keys local. The freshly built EXE belongs in the Windows ZIP, not in the source repository.

## 5. 文档职责 / Documentation boundaries

| 文档 / Document | 应回答的问题 / Responsibility |
| --- | --- |
| [README](../README.md) / [中文 README](../README.zh-CN.md) | 是什么、能做什么、如何开始 / overview and starting point |
| [用户指南](USER_GUIDE.zh-CN.md) / [User guide](USER_GUIDE.md) | 安装、查询、设置、更新、迁移与排错 / operation |
| [开发指南](DEVELOPER_GUIDE.zh-CN.md) / [Developer guide](DEVELOPER_GUIDE.md) | 构建、测试、打包、维护 / development |
| [CHANGELOG](../CHANGELOG.md) | 各版本发生什么变化 / version history |
| [发布说明](RELEASE-v1.5.0.md) | 此版本主要变化、附件和升级 / selected release |
| [QA 记录](QA-1.5.0.md) | 真正检查过什么、证据和边界 / verification evidence |
| [仓库资料](REPOSITORY_SETUP.md) | About、主题、计划地址和上传后设置 / repository setup |

检查双语页面内容对应、目录锚点、图片、离线相对链接、作者与版本。参考项目集中在 README 致谢，第三方来源仍在相关说明里署明。仓库未建立时保留计划入口提示。

Check language parity, anchors, images, offline links, author and version. Reference projects stay in acknowledgements; data attribution remains beside the relevant content. Planned links stay explicitly marked until the repository exists.

## 6. 运行包验收 / Runtime acceptance

验收需记录实际结果，不能仅勾选计划项。完成情况及机器边界见 [QA](QA-1.5.0.md)。

1. 完整解压选定 Windows ZIP 到新的可写目录，核对哈希；普通权限启动，含中文/空格路径可用。
2. 首次查询、中英文搜索、分路、分段、缺失条件和恢复默认符合用户指南。
3. 切换语言与深浅主题，查询条件保留；设置在退出重启后仍保存。
4. 离线模式的联网操作被禁用；保存启动更新偏好，在新普通进程中确认实际状态。默认自动更新、失败回退、停止与续传继续由既有模拟来源测试覆盖。
5. 关于页版本、作者、计划主页、离线更新记录、MIT 与第三方声明正确；诊断只含规定字段。
6. 使用隔离旧副本生成的设置和测试数据，退出后复制 `cache` 到新副本，核对偏好、保存数据和原日期；原副本保持可恢复。最近查询属于浏览器存储，单独说明。
7. 真实 Windows 125% / 150% 缩放与网页 100% 下检查中英文、两种主题、卡片和设置窗口，保存截图并恢复原系统缩放。
8. 程序内退出确实结束该副本服务；重新启动可正常查询。仅关闭窗口与退出按钮的行为分别记录。
9. 源码包重新构建；运行包离线接口与原始随包记录一致；包内不含个人缓存、日志或测试副本。

Verify fresh extraction, ordinary-user launch, query/filter behavior, language/theme persistence, update restrictions, About/diagnostics, isolated cache migration, real Windows scaling, shutdown/relaunch, source rebuild and package integrity. Record untested environments separately.

## 7. 公开发布前尚待完成 / Publication follow-up

- [分发状态](../release-status.json)仍列出 MalzaharAI 数据/更新接口范围、Riot 注册与素材来源、腾讯目录以及截图/整理内容范围的未决事项；此处不把它们标为已获授权。
- GitHub 仓库、About、Issues 表单和云端 Windows 检查需在首次上传后核对；云端结果不由本地报告代替。
- 本机运行包验收不等同于所有 Windows 10/11 设备验证；另机、干净系统以及未测试浏览器的范围以 QA 记录为准。
- 每项关闭时记录依据、日期和负责人，再更新分发状态、发布说明及最终包。维护者已选择开发预发布；后续正式版本继续复核这些事项。

Open evidence items and GitHub-hosted verification remain separate from local technical readiness. Record evidence before changing status. The maintainer has selected a development pre-release; this checklist records scope rather than granting third-party rights.

</details>

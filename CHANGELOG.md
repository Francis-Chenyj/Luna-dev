# Changelog / 更新日志

记录 Luna 各版本的变化；本地版本记录不代表已在 GitHub 发布。当前操作见[中文用户指南](docs/USER_GUIDE.zh-CN.md)，构建见[中文开发指南](docs/DEVELOPER_GUIDE.zh-CN.md)。

Version history for Luna. For current instructions, see the [user guide](docs/USER_GUIDE.md) or [developer guide](docs/DEVELOPER_GUIDE.md). Local version entries do not imply a public GitHub release.

## Unreleased / 未发布

暂无新增条目。 / No entries yet.

## 1.5.0-dev.1 — 2026-10-13（开发预发布 / Development pre-release）

- 仓库命名为 `Francis-Chenyj/Luna-dev`，统一程序主页、双语文档、反馈和下载入口；程序内版本仍为 1.5.0。
- Use `Francis-Chenyj/Luna-dev` for project, feedback and download links; the application version remains 1.5.0.
- 同步最新说明并重新打包，保留第三方授权与 Riot 注册待办，旧本地审阅包保留。
- Refresh documentation and release packages, preserving unresolved third-party permissions and Riot registration as well as earlier local review archives.

- 修正第三方声明中的预览图版本为 Luna 1.5.0，补列简体中文深色截图；区分首次来源核查与当前文档复核日期，保留未决分发状态。修正纳入开发预发布包，旧本地 ZIP 保留。
- Correct the preview version to Luna 1.5.0 and add the Simplified Chinese dark screenshot to third-party notices. Distinguish the initial source review from the current documentation review, retaining unresolved distribution status. Include the corrections in the development pre-release and preserve previous local ZIPs.

## 1.5.0 — 2026-10-12（本地审阅 / Local review）

- 新增统一设置页，提供常规、数据、排错、关于四个分区。
- 新增简体中文 / English / 跟随系统语言及深色 / 浅色 / 跟随系统外观，持久保存，切换保留当前查询与更新任务。
- 关于页显示 Luna 图标、版本、FrancisChen、计划主页、离线更新记录、原创代码 MIT 与第三方声明。
- 增加日志 / 数据目录入口及精简诊断复制；诊断不含原始日志、路径、账号或查询记录。
- 将已有启动数据更新做成可见开关，下次启动生效；保留手动更新、停止操作及离线 / 启动参数限制。
- 新增翻译、对比度、设置持久化、多进程保存和诊断隐私检查，保留原始关系数据。

English: Add persistent bilingual settings, light/dark/system themes, an offline About page, local folder shortcuts, privacy-limited diagnostic copying and a next-launch startup-data-update switch. Language and appearance changes preserve active queries and updates. Manual updates and offline/launch-option restrictions remain in effect.

### 修复与验证 / Fixes and verification

- 修复英文首页三个引导步骤仍显示中文的问题；翻译检查按浏览器规则解码属性中的字符实体，防止漏检。
- Fix the three welcome steps remaining in Chinese in English mode; decode attribute character references in translation checks as browsers do.
- 修复窗口宽度在 721–1000 像素时，英雄称号与关系数值因遗留的负边距而重叠的问题，适用于中英文和两种主题。
- Fix champion titles overlapping relationship values at viewport widths of 721–1000 pixels by removing the obsolete negative margin, for both languages and themes.
- 在本地准备中英双语错误反馈和功能建议表单，以及 Windows 自动构建与验证工作流；尚未上传或在 GitHub 执行。
- 新增统一检查入口，在隔离副本中运行既有测试、离线接口核对、打包校验、运行包解压检查与源码包重新构建，并记录结果与日志。
- 补充检查进程的超时、失败返回及清理验证；源码包纳入反馈表单和自动检查配置，增加当前版本与文档链接一致性检查。
- 修复本地接口拒绝错误请求体时偶发连接重置：先发送错误响应，再限时、限量丢弃残余输入；新增重复请求体、分块请求体和未发送完正文的回归检查，保留既有访问与请求校验规则。

English: Prepare bilingual feedback forms and a Windows verification workflow locally. Add an isolated entry point for existing tests, offline HTTP checks, package integrity, extracted-runtime checks and source-package rebuilds, with timeouts, process cleanup and local reports. Include collaboration files in source packages and check version/documentation consistency. Fix intermittent connection resets when rejecting invalid request bodies, with bounded connection teardown and regression checks that preserve the existing request restrictions. No GitHub execution or publication has occurred.

- 完成双语操作与构建说明的复核，补充设置迁移、发布文件边界及运行包验收记录。
- Review bilingual operation/build guides and document preference migration, publication file boundaries and runtime acceptance.

### 文档与署名 / Documentation and attribution

- 作者署名统一为 **FrancisChen**，覆盖界面、许可证、程序集信息、快捷方式、文档和打包元数据。
- 项目首页拆为默认英文 `README.md` 与简体中文 `README.zh-CN.md`，顶部互相跳转；首页保留项目介绍、界面预览、运行要求和文档导航。
- 用户指南按操作场景重写并拆为英文、简体中文两份；新增独立的双语开发指南，收录构建、测试与本地打包流程。版本变化集中在本文件。
- 内置说明、README 和指南的项目主页统一指向 Luna 拟定仓库，并注明仓库待建立；参考项目集中在 README 致谢，保留 Codex 辅助开发说明。
- 在本地准备仓库 About 简介与主题标签，添加当前版本的离线界面截图及来源说明。
- 两份 README 均纳入运行包、源码包及包内容校验。

English: Unify FrancisChen attribution and Luna project links; provide separate English and Simplified Chinese READMEs, task-based user guides and developer guides; add a current offline preview and local About/topics draft. Keep version history here and include the documentation in local packages.

## 1.4.3 — 2026-10-11

- 首页搜索、候选和最近英雄入口使用该英雄的常用分路；详情页搜索和关系卡片沿用当前分路，历史导航恢复已记录的分路。
- 增加默认、沿用、已选和恢复提示。缺失分路等待手动选择，合法空结果不切换分路；取消过期查询与重试，防止迟到结果覆盖当前页面。
- 为原创代码加入 MIT 许可证、第三方声明及分发核查记录；新增按版本生成运行包和源码包、重新编译、输出 SHA-256 及逐文件清单的脚本与校验。

English: Correct lane selection by entry point, label lane context, cancel obsolete queries, and prepare licensed, checksummed local packages with documented third-party scope.

## 1.4.2 — 2026-10-11

- 将三个单独分段分类明确为“仅高分段／仅中分段／仅低分段”，增加组合分类说明。
- 保持精确分类筛选逻辑与数据不变，说明“全分段”与“全部七类分段”的区别。

English: Clarify standalone rank categories and distinguish the All-ranks category from all seven categories without changing filtering behavior.

## 1.4.1 — 2026-10-11

- 重绘 14 档 Windows 图标，按窗口 DPI 提供原生尺寸，并为 Luna 专用 Edge 窗口绑定图标资源。
- 引入跨查询保留分路和历史恢复；该行为在 1.4.3 按不同入口进一步调整。
- 无有效分路时清空前一英雄结果、取消旧请求并禁用无效更新。

English: Improve native icons across DPI settings, introduce lane retention, and clear stale results when no lane is available. Version 1.4.3 later refines the entry-point rules.

## 1.4.0 — 2026-10-11

- 补齐 173 位英雄的英文姓名、称号、繁体译名及 863 个英雄对应民间搜索词，保留拼音、简写与歧义候选。
- 首页提供“更新离线库”，详情页增加“联网更新此英雄”；同一英雄、分路与条件的两侧数据校验后一起保存。
- 单英雄更新与整库任务共用调度并优先处理；失败、取消或上游换版时保留旧数据和时间。
- 窗口缓存限制为 96 条、估算 8 MiB、闲置 15 分钟；任务状态最多 24 项。修复更新按钮与迟到状态响应的竞争，并缩短临时文件名。

English: Expand search aliases, add champion refresh alongside library management, preserve validated data on failures, bound in-window caches, and fix asynchronous status races.

## 1.3.5 — 2026-10-11

- 启动检查等待缩短至约 1 秒，先更新当前与最近英雄，再补齐完整库。
- 双向关系及前后原站版本一致后即可原子保存使用，无需等待全库完成；界面明确标注英雄更新范围。
- 新增跨启动保留的英雄缓存，正常最多 64 组、8 MiB，最近优先级最多 24 组；支持离线读取、损坏回退和空间统计。
- 复用同版已验证结果、续传记录与目录，减少重复请求。

English: Prioritize current/recent champions after a shorter startup delay, publish verified pairs before the full library finishes, and reuse bounded persistent champion caches.

## 1.3.4 — 2026-10-11

- 普通启动自动检查默认条件及已保存的完整库，每次服务启动安排一次，可停止或手动重试。
- 自动下载限速，下载与整库校验移出查询共用的锁；通过完整校验后才启用新库，失败保留旧数据并支持续传。
- 搜索框下方显示后台状态；离线模式禁止外网更新，增加 `--no-auto-update` 参数。

English: Add automatic background updates, keep local queries responsive during downloads, retain usable data on failure, and support manual-only or offline operation.

## 1.3.3 — 2026-10-11

- 两侧结果分别标注程序自带、联网更新或备用旧数据，显示原站生成时间与本机更新时间。
- 区分原站检查时间与下载保存时间；旧数据回退保留原始来源和日期，缺失时间显示“未记录”。

English: Show provenance and timestamps per direction, distinguish checks from downloads, and preserve original dates during fallback.

## 1.3.2 — 2026-10-11

- 每种筛选组合保留当前完整库与上一份可用备份；仅保留最近一版可续传下载，闲置 7 天后清理。
- 启动、更新结束及持续运行每 24 小时维护一次，保护随包库、旧缓存、个人文件及用途不明的数据，限制路径与目录联接边界。
- 支持跨进程刷新启用记录，并在数据管理中展示占用与释放空间。

English: Bound retained libraries and staged downloads, add conservative scheduled cleanup, and report storage use while respecting local path boundaries.

## 1.3.1 — 2026-10-11

- 查询期间保留完全相同条件的近期结果与原始时间；短暂故障最多重试两次，共用 5 秒等待上限。
- 区分服务断开、超时、未下载、损坏与正常空结果；切换条件或退出时取消旧重试。

English: Retain exact-query results during bounded retries and distinguish missing data, valid empty results and connection failures.

## 1.3.0 — 2026-10-11

- 查询改为只读本地，取消首次查询联网及六小时过期刷新。
- 预装默认阈值 1、严谨度 5 的完整库：173 位英雄、428 个有效英雄分路、856 份双向记录。
- 新增后台批量更新、准确条件覆盖提示和全部 25 种筛选组合下载；支持限速、有限重试、续传、停止、校验与原子切换。
- 失败保留旧库，损坏记录可回退前版或随包数据。

English: Make queries local-only, bundle the complete default profile, and introduce resumable validated library updates with fallback support.

## 1.2.0 — 2026-09-21

- 程序、窗口、EXE、快捷方式及程序包统一使用 Luna 名称，保留绿色底、黑色 L 图标。
- 为厄加特增加“螃蟹”和“蟹”别名，并在目录更新后保留。
- 增加完整英文说明，以及厄加特与蒙多案例，说明整局对局表现、对线指标和位置总体胜率的区别。

English: Adopt the Luna name, add persistent Urgot nicknames, and provide an English guide with a matchup-interpretation example.


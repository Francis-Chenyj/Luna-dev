# Luna 1.5.0 本地验证 / Local verification

日期：2026-10-12（Asia/Shanghai）。作者：FrancisChen。

## Scope / 范围

新增统一设置、中英文切换、深浅主题、关于、本地目录入口、精简诊断复制和下次启动自动数据更新开关。保留现有查询、更新与数据内容。所有检查在本机或隔离副本完成；未进行 GitHub 创建、上传或发布，未访问真实数据更新来源。

Settings, bilingual UI, themes, About, local folder shortcuts, limited diagnostics and startup-update preferences were verified locally. No GitHub publication or real upstream-data update was performed.

## Automated checks / 自动检查

- JavaScript：81 项通过，覆盖原有搜索与查询行为，新增语言切换保留查询、系统主题响应、保存失败回退、离线限制、跨窗口同步、诊断白名单与剪贴板失败处理。
- 翻译：静态界面、明确文案键、插值占位检查通过。深浅主题的主要文字、辅助文字、强调色、警告色与按钮文字组合均达到普通文字 4.5:1 对比度；这不是全产品无障碍认证。
- C# 设置：默认值、独立字段保存、两个进程并发保存、损坏文件保留、不兼容格式拒绝覆盖、启动限制、诊断脱敏、固定目录与文档白名单通过。
- 既有 C# stability、storage、auto-update、focus、hero-update、longpath、icons 套件通过；更新套件使用模拟来源。新版本构建通过。
- 实际 HTTP：离线模式及普通模式分别核对 856 条随包记录，返回数据与原始快照一致；新增接口的方法、来源、标记头、参数、固定文档与诊断字段检查通过。
- 离线 HTTP 查询耗时：中位 3.85 ms，P95 7.62 ms，最大 12.16 ms；普通模式（保存的自动更新开关关闭）中位 3.89 ms，P95 7.05 ms，最大 16.86 ms。为本机样本，不代表其他设备或实时联网速度。
- 实际进程复核：在离线界面关闭启动更新后，另一个普通进程使用新端口启动；语言、主题与开关保留，启动自动更新未运行。

## Interface review / 界面检查

- 实际浏览器检查英文浅色、中文深色、关于与 MIT 离线全文、诊断复制及 640 像素窄窗口布局。
- 语言与主题切换保留亚托克斯、上路、阈值 1、严谨度 5 及当前分段；英文英雄名、称号、分段、来源与日期同步显示。
- 复制内容仅包含版本、运行模式、自动更新状态与错误类别次数。原始异常仍在本地日志，不进入复制文本。
- 目录按钮的前端请求与后端固定路径规则通过测试；未将任意用户输入作为路径或命令执行。未自动打开真实个人目录进行人工浏览。
- 截图见 [英文浅色](images/luna-matchups.png)、[中文深色](images/luna-matchups-zh-CN.png)，来源范围见[配图说明](images/README.md)。

## Limits / 限制

启动更新开关只在下次服务启动时生效。离线与 `--no-auto-update` 限制优先。当前未增加程序自动升级、托盘、LCU 或游戏内功能。第三方素材和数据的分发条件保持原有待核查状态，详见[分发记录](../release-status.json)。

原版程序及源码备份保留在本地忽略目录 `research/settings-1.5.0/before`；该目录和测试缓存不会进入源码或运行包。

## Follow-up verification / 反馈与自动检查、重叠修复复核

2026-10-12 补充复核，仍为本地未发布修改。以上记录为设置功能初次验证，以下记录覆盖后续修复与第三步自动检查。

- 中英文关系卡片重叠的原因是 1000 像素断点中的旧规则保留了 `margin-top: -16px`；后续样式取消浮动后，数值行仍向上移，与英雄称号重叠。删除这条过时规则后，名称、称号和关系数值恢复独立排版。
- 真实浏览器复现：修复前，900 像素窗口的 85 张卡片均检测到文字重叠。修复后，简体中文 / English、深色 / 浅色，以及 360、640、720、721、800、900、1000、1001、1440 像素宽度共 36 种组合全部通过，每种检查 85 张卡片。中文和英文截图另经目视确认。该浏览器复核是独立检查，不属于 Node 自动测试。
- 本地统一检查 `scripts/check.ps1` 的 16 个步骤全部通过：84 项 JavaScript 测试、检查进程的参数 / 失败 / 超时清理回归、Windows 构建、8 套 C# 测试、离线 HTTP、打包、包内容与 SHA-256 校验、解压运行包复查和源码包重新构建。
- 原始构建和解压后的运行包各核对 856 条离线接口记录，均与随包快照一致。测试仅使用隔离副本、本地模拟来源和回环地址。
- 检查过程中修复了拒绝非法请求体时偶发的连接重置：发送错误响应后关闭发送方向，再最多用 200 ms 丢弃 16 KiB 残余输入。新增 40 次重复非法请求、分块正文、方法 / 标记错误及未发送完整正文的回归；错误状态码和设置不变的断言全部通过。
- 中英双语错误反馈表单要求填写版本、Windows 版本、复现步骤、预期与实际结果；截图、日志和精简诊断为选填。另提供功能建议表单。4 个 YAML 配置已通过本地语法和关键字段检查。
- Windows 工作流使用相同的本地检查入口，源码包包含反馈表单、工作流、检查脚本和布局检查函数。工作流只有读取仓库权限，不上传检查产物，不创建 Release，不写入仓库。

完整检查结果和日志保留在本地 `dist/ci-review-03`。布局检测记录、修复前备份及中英文截图保留在 `research/ci-step3`。这些机器专用检查产物不会进入公开包。

The follow-up local run passed all 16 verification steps, including 84 JavaScript tests, eight C# suites, two sets of 856 offline HTTP comparisons, package verification and a source-package rebuild. A separate real-browser review passed all 36 language/theme/viewport combinations. The overlap came from an obsolete negative margin, which was removed. Invalid-body rejection also received a bounded connection-teardown fix and strict regression coverage.

GitHub 托管环境尚未执行该工作流；未来首次上传后仍需确认 Windows runner 的实际结果。第三方分发核查状态不变，`publicReleaseReady` 仍为 `false`。本次没有创建仓库、上传或发布。

The GitHub-hosted workflow has not run. Distribution review remains unchanged (`publicReleaseReady: false`); no repository creation, upload or publication occurred.

## Fourth-step acceptance / 第四步文档与运行包验收

2026-10-12 第四步验收记录。版本为尚未公开的 **1.5.0**，第四步当时选定的本地附件为 `dist/v1.5.0-final-review/`；其 `SHA256SUMS.txt`、`release-files.json` 和本地 `verification.json` 用于识别当时的具体文件，不能只按版本号区别旧包。此后文档同步重新打包见文末；最新附件与文件边界以[发布核对清单](RELEASE_CHECKLIST.md)为准。

### Documentation / 文档

- 双语 README 保留介绍与开始入口；双语用户指南负责操作、设置、数据更新、升级迁移和排错；双语开发指南负责构建、测试与打包。历史变化归入 CHANGELOG，本地首发修复并入 1.5.0。
- 用户指南补充首次启动新副本前复制 `cache`、保留旧目录、浏览器最近查询不随该目录迁移、相同版本号以哈希识别，以及关闭窗口与程序内退出的区别。英文按钮和分段名称对齐实际界面。
- 本地 18 份 Markdown 文档的路径、图片引用与 25 处标题跳转检查通过；双语 README 配图经目视复核。开发指南中的源码工作流以文件路径说明，避免在不含 `.github` 的 Windows 包里出现失效的相对链接。
- 项目名、作者和计划主页继续统一为 Luna、FrancisChen、`Francis-Chenyj/Luna-dev`。About 和主题标签仅准备在本地；旧名称扫描未发现当前文档或界面出现 Luan、LeagueLuna 或旧作者署名。

### Runtime / 运行包

| 检查 | 实际结果 |
| --- | --- |
| 新装、权限和路径 | 将已验证的运行 ZIP 完整解压到带中文和空格的新目录，以普通 Windows 权限启动；子进程 PATH 不含 Node.js/Python。离线模式显示正确，首次查询无需外网 |
| 查询和条件 | 英文 Jax、中文别名“剑魔”查询成功；默认条件、沿用上路、全部七类分段和缺失条件提示正常，恢复默认后可继续查询 |
| 语言与主题 | 简体中文 / English、深色 / 浅色切换保留查询条件；退出后再次启动，English、深色和关闭启动自动更新的选择仍保留 |
| 关于与诊断 | 1.5.0、FrancisChen、Luna 计划主页正确；更新记录、MIT 与第三方声明可离线读取。诊断复制仅含版本、运行模式、启动更新状态和错误摘要；没有路径、账号或查询记录 |
| 数据更新偏好 | 离线限制有效；在新普通进程中读取保存的关闭状态，界面显示本次手动更新，未发起启动自动更新。实际下载行为由本地模拟来源套件覆盖 |
| 升级迁移 | 用测试中生成的设置和测试库复制到第二个完整解压副本。旧副本 862 个缓存文件哈希保持不变；新副本核对 860 个持久文件，排除两个运行地址文件。查询仍使用同一数据，保留原生成/保存时间 |
| 关闭、退出和重启 | 仅关闭窗口后服务继续存在，重新运行入口复用同一进程；程序内 Exit 停止该进程。再次启动生成新进程，设置保留、84 张查询卡片布局通过；最后再次 Exit，确认验收进程结束 |

迁移样本由随包数据复制到隔离下载缓存并配置测试指针构成；它模拟已保存库，不代表本轮从上游重新下载。未读取或迁移个人缓存。人工验收起点为前次已验证 ZIP，首页翻译修复后复查了新文案；其 9 个后端源码文件与本轮构建输入一致。最终包的独立复查结果保留在随包目录的本地 `verification.json`，不会将历史 ZIP 的哈希当作新包哈希。

### Windows scaling / Windows 系统缩放

用户协助切换 Windows 显示设置，浏览器网页缩放恢复为 100%。以下为真实系统缩放，未用浏览器缩放或仅调整宽度替代：

| Windows 缩放 | 实测像素比例 | 语言 × 主题 | 结果 |
| --- | --- | --- | --- |
| 125% | 1.25 | 简体中文 / English × 浅色 / 深色，共 4 种 | 每种 85 张关系卡片无重叠或越界，设置窗口位于视口内 |
| 150% | 1.5 | 简体中文 / English × 浅色 / 深色，共 4 种 | 每种 85 张关系卡片无重叠或越界，设置窗口位于视口内 |

截图经目视复核，检测记录包含窗口尺寸和查询条件。最初 125% 下观察到 1.125，确认叠加了网页 90% 后由用户按 Ctrl+0 校正；仅将校正后的 1.25 计为通过。结束后用户恢复原来的 **200%**，并观察到像素比例 2。此前 36 种语言/主题/窗口宽度组合的验证记录仍保留，不能与这 8 种系统缩放检查混为一项。

### Fix and final automated run / 发现的修复与最终自动检查

验收发现英文首页三个引导步骤仍显示中文。原因是 HTML 属性中的 `&nbsp;` 在浏览器中已解码为不换行空格，而翻译表仍使用字面实体字符串；原静态检查未模拟这一步。先修正检查并确认失败，再将三个键改为实际空格字符；84 项 JavaScript 测试通过，真实浏览器首页也已显示英文。没有改写关系数据或增加产品功能。

本轮 `scripts/check.ps1` 的 **16 个步骤全部通过**：84 项 JavaScript 检查、检查工具自身回归、构建、8 套 C# 测试、离线 HTTP、打包与完整性校验、解压运行包检查、源码包重新构建。构建副本和解压运行包各核对 856 份数据，均与随包快照一致。整轮记录保留在 `dist/ci-step4-final`，用时约 178 秒。随后补齐本 QA 记录，再生成选定的最终附件，并将相关包校验结果单独记录，避免覆盖已审阅 ZIP。

The fourth-step review aligned the bilingual documentation and verified fresh extraction, ordinary-user launch, isolated cache migration, About/diagnostics and process shutdown/relaunch. All eight real Windows scaling combinations (125%/150%, two languages, two themes) passed; the original 200% setting was restored. A three-message English welcome-screen omission was fixed and regression-checked. The final complete local run passed all 16 steps, including 84 JavaScript tests, eight C# suites and two sets of 856 offline comparisons. Exact final-package checks are recorded separately beside the selected ZIPs.

### Evidence and remaining scope / 证据与未覆盖范围

本轮本地证据位于 `research/release-step4`：系统缩放检测与截图、新装环境、迁移前后哈希、诊断和关于页记录、进程退出重启记录、文档检查及最后打包日志。它们与测试副本均不进入公开源码包或运行包。1096 个既有源码/界面/数据/检查文件的基线核对显示，仅 `web/locales.js` 与 `tests/i18n.test.cjs` 在本轮改变；文档另行维护，数据原样保留。

实际系统为 `Microsoft Windows NT 10.0.26300.0`，界面验收使用本机 Edge。未将本机 PATH 排除开发工具等同于全新系统：干净虚拟机、另一台 Windows 10/11 机器、其他浏览器、下载标记/SmartScreen、签名信任提示未实测；目录入口有既有接口/固定路径检查，本轮没有人工观察资源管理器。GitHub 云端运行和真实上游下载也不在本轮范围内。第三方分发事项仍未关闭，`publicReleaseReady` 保持 `false`。

This is a local technical acceptance record, not a clean-machine or all-Windows certification. GitHub-hosted execution, other machines/browsers, download/signature prompts and third-party distribution evidence remain separate follow-up items. No repository, upload, registration or public release was performed.

## Documentation refresh / 文档同步重新打包

2026-10-12 后续本地同步：应用版本仍为 **1.5.0**，将最新第三方声明、核查范围及发布文档纳入 `dist/v1.5.0-final-review-r2/`。程序源码、界面和随包数据保持不变，旧包保留原样。此处的 r2 仅用于区分本地审阅目录，不是新的应用版本或已公开的 Release。

本次自动检查在独立副本中执行。首次尝试在检查工具的进程超时清理环节中断：受限执行环境中的 Windows 进程树清理命令返回失败；本地单进程探针复现了这一环境限制，测试进程已清理。该次失败记录保留在 `dist/ci-docs-final-20261012/report.json`，不计为通过，也没有修改检查脚本来绕过此项。

在正常执行环境中的完整重跑记录位于 `dist/ci-docs-final-20261012-r2/report.json`；新附件目录内的本地 `verification.json` 记录最终包与工作区文件的一致性、文档链接、校验值及旧包保留情况。以这些实际结果文件为准，不将本次重新打包等同于重新完成人工界面或 Windows 缩放验收。

第三方授权和 Riot 产品注册继续保留待办，`publicReleaseReady` 仍为 `false`。本次不申请代码签名、不创建仓库、不上传或发布到 GitHub。

This local documentation refresh keeps application version 1.5.0 and preserves program source, UI files, bundled data and previous archives. The selected artifacts are in `dist/v1.5.0-final-review-r2/`; r2 identifies a local review directory only. The first run stopped during the timeout-cleanup regression because Windows process-tree termination failed in the restricted execution environment; that failed result is retained in `dist/ci-docs-final-20261012/report.json`. A standalone local probe reproduced the environment limitation and was cleaned up; no check was removed or bypassed. The complete rerun outside that restricted environment is recorded in `dist/ci-docs-final-20261012-r2/report.json`, with final artifact checks in local `verification.json` beside the ZIPs. Earlier manual UI/scaling evidence is retained, not counted as newly performed. Third-party permissions and Riot registration remain pending (`publicReleaseReady: false`); no code-signing application, repository creation, upload or publication is performed.

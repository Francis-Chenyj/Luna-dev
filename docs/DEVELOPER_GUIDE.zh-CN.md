# Luna 开发指南

[English](DEVELOPER_GUIDE.md) · **简体中文** · [项目介绍](../README.zh-CN.md) · [用户指南](USER_GUIDE.zh-CN.md) · [更新日志](../CHANGELOG.md)

**适用版本：1.6.6 · 作者：FrancisChen**

故障测试的迁移映射、时间控制边界和新增规则见[测试维护](TESTING.md)。新增测试使用类型明确的服务、注入依赖和真实运行时生命周期。

补充入口：[审计修复与测试命令](AUDIT_REPAIRS.md)、[参与开发](CONTRIBUTING.md)、[安全问题](SECURITY.md)。

项目主页：[Luna · GitHub](https://github.com/Francis-Chenyj/Luna-dev)。

本指南说明现有构建、测试与本地打包流程。请使用源码目录或 `Luna-v1.6.6-Source.zip`；Windows 运行包不包含开发源码和测试。以下命令均在源码根目录执行。

## 开发环境

| 工具 | 用途 |
| --- | --- |
| 安装 .NET Framework 4.7.2+ 的 Windows | 构建与运行 Luna |
| Windows PowerShell | 现有构建脚本和 C# 测试脚本 |
| Node.js 18+ | JavaScript 与 HTTP 测试 |
| Microsoft Edge WebView2 Runtime | 原生窗口运行与 `-Desktop` 真实界面健康测试 |
| Python 3 标准库 | 可选的离线数据采集与搜索索引维护 |

构建使用已有的 .NET Framework `csc.exe`，优先寻找 `Framework64/v4.0.30319`，其次寻找 `Framework/v4.0.30319`。单英雄更新和长路径测试脚本目前直接使用 Framework64 编译器，因此完整测试建议在 64 位 Windows 环境中运行。

构建和基础检查无需 npm；可选真实浏览器检查需要锁定的 Playwright 开发依赖。无需前端打包或外部 C# 包还原。普通用户运行 Luna 不需要 Node.js 或 Python。

## 目录与模块

| 路径 | 职责 |
| --- | --- |
| `src/ApplicationRuntime.cs` | 服务组装、启动策略与后台工作生命周期 |
| `src/Program.cs` | HTTP 和桌面进程宿主；调用服务接口 |
| `src/Library.cs`、`src/QueryService.cs` | 索引、校验、原子发布及本地查询 |
| `src/DataUpdates.cs` | 整库和单英雄共同调度、进度、取消及优先级 |
| `src/Storage.cs`、`src/QuarantineStore.cs` | 保守清理、空间统计和隔离区 |
| `src/Settings.cs`、`src/DiagnosticLog.cs` | 设置持久化及独立诊断状态 |
| `web/i18n.js`、`web/locales.js`、`web/settings.js` | 双语文案与设置交互；主题使用 CSS 语义颜色变量 |
| `src/LocalHttp.cs` | 严格请求解析、请求头总超时及连接断开取消 |
| `src/LocalIO.cs` | 文件读写长路径适配；路径归属和联接检查仍由调用处执行 |
| `src/DesktopWindow.cs` | WinForms/WebView2 窗口、托盘、关闭策略与界面就绪确认 |
| `src/AppUpdate.cs`、`src/UpdateTrust.cs` | 应用更新、断点下载、发布者公钥 |
| `src/Deployment.cs` | 独立维护程序、清单、事务恢复、升级与卸载 |
| `src/DesktopIcons.cs` | Luna 自身 Edge 应用窗口的图标 |
| `src/AssemblyInfo.cs` | 产品名称、作者与版本信息 |
| `web/` | 原生 HTML、CSS、JavaScript、搜索索引和 Luna 图标 |
| `data/` | 随包目录、默认关系库、搜索标签和头像 |
| `scripts/` | 打包、图标、快捷方式及数据维护工具 |
| `tests/` | 现有 JavaScript、C# 和 HTTP 验证 |
| `.github/` | 双语反馈表单与 Windows 自动检查 |
| `docs/` | 用户与开发指南、核查记录和配图 |
| `cache/` | 本机运行状态，不纳入源码或程序包 |
| `dist/` | 本地生成的包，不纳入源码 |
| `research/` | 本地采集、隔离检查和归档，不纳入源码或程序包 |

查询只读本地文件。外网更新在启用前核对英雄、分路、筛选组合和原站版本；同一英雄的双向结果一起保存，完整组合在全部记录通过校验后整体启用。前端保留上游的分类和排序。

设置实现：每次只保存一个白名单字段，使用跨进程锁和原子替换。启动更新偏好只在进程启动时读取；离线与命令行限制优先。新增界面文案应进入翻译表，沿用命名键或现有中文源文案键；不要直接翻译或改写上游数据。诊断新增字段必须经过显式白名单审查。

## 构建

先退出需要替换 EXE 的 Luna 实例，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本重新生成图标，编译 `Luna.exe` 和 `Luna.Maintenance.exe`，复制两份 WebView2 托管 DLL 及三个架构的 `runtimes` 加载器。SDK 固定在 `vendor/webview2` 并校验哈希，无需联网还原。运行时还必须保留 `Luna.exe.config`、`web`、`data`；安装、升级和卸载还需打包生成的 `installed-files.json`。维护程序嵌入 .NET 4.7.2 目标与长路径声明，临时维护进程复制并校验主程序配置。文件适配层支持未开启系统长路径策略的情况。

若希望将上述编译产物、配置及运行依赖放到其他目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\dist\dev-build
```

该输出方式**不会复制** `web` 与 `data`，启动仍需完整运行目录。图标生成仍写入源码目录中的 `web`。

## 本地运行参数

| 参数 | 行为 |
| --- | --- |
| `--offline` | 禁止外网更新，保留本地读取 |
| `--no-auto-update` | 关闭启动检查，仍允许手动联网更新 |
| `--no-browser` | 运行本地服务但不打开浏览器 |
| `--port <数字>` | 指定 1024–65000 内的起始端口；占用时最多再尝试后续 29 个端口 |

EXE 默认起始端口为 17653；离线启动脚本另外指定 17683。测试 `--no-auto-update` 前先退出已有普通服务，重新打开同一实例不会替换其启动设置。

在源码根目录构建后，可手动启动离线服务：

```powershell
.\Luna.exe --offline --no-browser --port 18783
```

服务会将实际地址写入 `cache/running-url.txt`。在浏览器打开该本地地址查看界面，结束时用程序内 **退出**；只关闭标签页仍会等待闲置超时。

## 现有测试

真实浏览器回归需要 Node 20+ 和锁定的开发依赖。先运行 `npm ci --ignore-scripts --no-audit --no-fund` 与 `node node_modules/playwright/cli.js install chromium`，再运行 `scripts/check.ps1 -Browser`。本机已有 Edge 时，可直接使用 `-Browser -BrowserChannel msedge`，无需额外下载 Chromium；Windows 工作流使用 Chromium。测试复制并校验独立程序包，创建临时浏览器资料，禁止外部请求，保留 `browser/result.json`、截图及 `trace.zip`，不使用个人浏览器资料或缓存。默认离线检查与构建不需要 npm。测试不会隐式下载依赖。

已有程序包也可单独运行：`node tests/browser-runner.mjs --runtime <解压后的Luna目录> --output <新的输出目录> --channel msedge`。输出目录须为新目录，且在输入程序包以外。

升级事务以 committed 日志为完成边界，之后的清理或结果记录失败不会触发回滚。异常备份、解压目录和压缩包保存在 `cache/app-update/*-preserved-*` 或 `package-rejected-*`；自动清理只删除完整归属的旧备份。原生健康探测直接绑定临时端口，再校验含随机标识、进程号、端口的地址记录和健康响应，避免探测端口被抢占。普通启动仍使用文档所列端口范围。

### 一次完成本地检查

运行与预备 Windows 工作流相同的入口：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
```

每次会创建新的 `dist/checks/<唯一编号>/`，保存独立源码副本、`report.json`、可阅读的 `summary.md`、各步骤日志、程序包与解压测试副本。检查不会改动正在使用的可执行程序、图标和个人缓存。Node.js 需在 PATH 中，也可以传入 `-NodePath <node.exe绝对路径>`。如需指定位置，使用 `-OutputDirectory .\dist\my-check`，必须是当前源码目录下 `dist` 内尚不存在的新目录；不会覆盖以前的检查结果。

检查顺序为：JavaScript 与项目一致性检查 → 检查工具的失败和超时处理 → 构建 → 11 组 C# 测试 → 离线接口 → 打包及文件清单、哈希校验 → 解压 Windows 包后再次完整检查离线接口 → 解压源码包重新构建。每轮接口检查核对默认条件的全部随包记录，同时检查请求边界、设置、文档和界面资源。任一步失败即停止，等待设有超时，只清理检查自身启动的进程，并保留日志。测试临时文件集中在检查目录的 `temp` 中。

源码包中的 `.github/workflows/windows-checks.yml` 选择 `windows-2022`、Windows PowerShell 和 Node.js 24，准备在分支推送、拉取请求或手动触发时运行。只配置仓库读取权限，官方 Actions 固定到明确版本的提交，不上传程序包、不创建 Release。环境准备可能下载 Node.js 和 Actions；Luna 测试使用本地样本和离线服务，不连接数据提供方，也不使用个人凭据。Windows 运行包不包含这些开发配置。参见 [GitHub 工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)与 [Windows 环境清单](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md)。

GitHub 实际执行结果以仓库 Actions 页面中对应提交的运行记录为准，本地检查另行记录。校验和用于确认内容完整性，语言、主题和排版仍需人工检查；技术检查通过不代表第三方分发条件已经解决。

修改布局时，还需在真实浏览器中打开隔离的离线查询，检查中英文、深浅主题，以及 360、640、720、721、800、900、1000、1001、1440 CSS 像素宽度。保留全部分段展开，覆盖共轭标签与较长英雄称号。从 `tests/browser-smoke.mjs` 导入 `inspectLayout` 到浏览器自动化会话，在页面内执行该只读测量函数，检查文字重叠和超出卡片边界，并结合截图复核。这项浏览器检查补充自动工作流，不属于 Node 界面模拟测试。特别不要遗漏 721–1000 像素区间：此前残留的关系数值负边距曾在此处导致文字重叠。

### 单独运行测试

按改动范围选择相关检查。默认 C# 测试使用隔离目录、本地样本或模拟数据源。

```powershell
node --test .\tests\core.test.cjs .\tests\ui.test.cjs .\tests\i18n.test.cjs .\tests\repository.test.cjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-settings.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-stability.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-storage.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-auto-update.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-focus.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-hero-update.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-icons.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-longpath.ps1
```

| 测试 | 主要覆盖 |
| --- | --- |
| `i18n.test.cjs`、`run-settings.ps1` | 翻译覆盖、插值、主题文字对比度、持久化、多进程保存、异常设置与诊断隐私 |
| `core.test.cjs`、`ui.test.cjs` | 搜索词、分路选择、筛选、界面状态与查询行为 |
| `run-stability.ps1` | 查询 / 更新稳定性及校验后的数据回退 |
| `run-storage.ps1` | 保留规则与保守清理边界 |
| `run-auto-update.ps1` | 启动调度、失败、续传及启用期间的读取 |
| `run-focus.ps1` | 英雄优先级、双向更新与缓存复用 |
| `run-hero-update.ps1` | 手动英雄更新队列和状态 |
| `run-icons.ps1` | 原生图标资源 |
| `run-longpath.ps1` | 较深的安装目录与更新临时路径 |

自动更新套件将随包 856 份记录作为默认条件输入，额外条件使用模拟样本。`run-focus.ps1 -Live` 会明确访问真实原站，不属于默认离线检查；真实外网核验与构建、打包分开进行。

## 隔离 HTTP 验证

使用不带个人缓存的新运行副本，让测试比较随包快照，而不是已下载的新数据。先在源码根目录构建，然后运行以下示例；它在已忽略的 `research/` 下创建隔离副本，读取实际本地端口，并在结束时只停止自己启动的进程：

```powershell
$ErrorActionPreference = 'Stop'
$projectPath = (Get-Location).Path
$testPath = Join-Path $projectPath ('research\http-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
foreach ($item in @('Luna.exe', 'Luna.exe.config', 'web', 'data', 'LICENSE', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectPath $item) -Destination $testPath -Recurse
}
$testProcess = Start-Process -FilePath (Join-Path $testPath 'Luna.exe') `
    -ArgumentList '--no-browser', '--offline', '--port', '18783' `
    -WorkingDirectory $testPath -WindowStyle Hidden -PassThru
try {
    $addressFile = Join-Path $testPath 'cache\running-url.txt'
    $testUrl = ''
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if (Test-Path -LiteralPath $addressFile) {
            $testUrl = (Get-Content -LiteralPath $addressFile -Raw).Trim()
            if ($testUrl -match '^http://127\.0\.0\.1:\d+$') { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($testUrl -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'Test service did not write its local address.' }
    node .\tests\server.mjs $testUrl --offline
    if ($LASTEXITCODE -ne 0) { throw 'HTTP checks failed.' }
} finally {
    $testProcess.Refresh()
    if (-not $testProcess.HasExited) { Stop-Process -Id $testProcess.Id }
}
```

HTTP 套件核对全部 856 份随包记录、来源日期、合法空列表、筛选隔离、本地 HTTP 访问边界，并测量响应时间。检查后保留隔离副本，便于查看。

若需普通服务的固定快照检查，将启动参数改为 `--no-browser --no-auto-update --port 18753`，并去掉 Node 命令末尾的 `--offline`。仍使用隔离目录；比较快照期间不要点击联网更新按钮。

## 打包与校验

使用**全新空目录**。`dist/v1.6.6/` 默认目录可能已有本地包，下面示例另选一个目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -OutputDirectory .\dist\v1.6.6-review
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-package.ps1 -ReleaseDirectory .\dist\v1.6.6-review
```

再次打包时换一个未使用的目录，脚本不会与已有文件混装。

| 输出 | 内容 |
| --- | --- |
| `Luna-v1.6.6-Windows.zip` | 重新构建的 EXE、运行文件、随包数据、双语 README 与文档 |
| `Luna-v1.6.6-Source.zip` | 源码、脚本、测试、数据与文档；不含 EXE |
| `SHA256SUMS.txt` | 两个 ZIP 和清单的 SHA-256 |
| `release-files.json` | 包内逐文件路径、大小与哈希 |
| `_build/` | 本地构建中间目录，不作为下载附件 |

脚本从 `src/AssemblyInfo.cs` 读取版本，并与 `release-status.json` 核对。打包会重新构建 EXE，不直接复制根目录的旧 EXE，并校验产物版本和作者。

两个 ZIP 都以 `Luna/` 为顶层目录，包含许可证、第三方声明和核查状态。明确的目录与扩展名规则排除缓存、采集目录、个人快捷方式和常见密钥文件。**当前两个包都会带入第三方数据和素材。**打包成功不会改变 `publicReleaseReady`，也不会创建 GitHub 仓库或 Release。

包校验核对哈希、文件清单、路径边界、分发元数据及运行包 / 源码包差异。源码包还包含 `.gitattributes`、反馈表单、工作流和统一检查脚本；运行包不带开发配置。`repository.test.cjs` 检查当前版本和作者一致性、项目入口，以及本地文档和配图文件是否存在。语言跳转、标题锚点与排版仍需人工复核，ZIP 完整性检查不能证明导航正确。

## 运行包验收

交付运行包前，按[发布核对清单](RELEASE_CHECKLIST.md)验收：选定同一个 Windows ZIP，分别解压新装副本和设置迁移副本，检查普通权限启动、查询筛选、语言主题、离线关于与诊断、启动更新偏好、退出及重新启动。使用测试中生成的设置和样本数据，不使用个人缓存。

Windows 系统缩放与浏览器网页缩放是两个独立设置。实际切换 Windows 为 125% 和 150%，网页缩放保持 100%，记录观察到的像素比例、语言主题、窗口大小和截图，结束后恢复原设置。只修改浏览器宽度或网页缩放不算 Windows 系统缩放验收。未具备的系统、浏览器或干净机器环境需单独注明。

同一版本号的本地包用逐文件清单和 SHA-256 区分。[验证记录](QA-1.6.6.md)记录实际完成的检查，[发布说明](RELEASE-v1.6.6.md)描述选定构建。代码或包内容变化后重做相关检查，不直接覆盖已验证的 ZIP。

## 数据与搜索词维护

普通用户通过 Luna 更新数据。以下是维护者工具：

| 工具 | 行为 |
| --- | --- |
| `scripts/build-search-index.py` | 不带参数时，根据已保存的合并词表重建 `web/aliases.js` |
| `scripts/build-search-index.py --english <路径> --traditional <路径>` | 合并匹配的 Data Dragon 目录与 `data/community-aliases.json`，重建总词表和浏览器索引 |
| `scripts/prepare-library.py --profile default` / `--profile all` | 访问上游接口，将可续传采集保存在 `research/acquisition/` |
| `scripts/download-icons.mjs` | 从脚本配置的来源下载头像 |
| `scripts/build-icon.ps1` | 生成 Luna 自身的几何图标 |

重建搜索词时保证官方目录的英雄覆盖一致，记录来源与版本变化，并重新运行搜索测试。替换 `data/library/<阈值-严谨度>/` 前先核实采集清单完整性。普通构建不需要重新采集；采集与尚待确认的分发条件应分别核对。

## 文档与版本资料维护

| 文档 | 职责 |
| --- | --- |
| [英文 README](../README.md) / [简体中文 README](../README.zh-CN.md) | 项目介绍、预览、运行要求与文档导航 |
| [英文用户指南](USER_GUIDE.md) / [简体中文用户指南](USER_GUIDE.zh-CN.md) | 安装、操作、更新与故障处理 |
| [英文开发指南](DEVELOPER_GUIDE.md) / [简体中文开发指南](DEVELOPER_GUIDE.zh-CN.md) | 目录、构建、测试与本地打包 |
| [CHANGELOG](../CHANGELOG.md) | 按版本记录变化，未发布修改放在 Unreleased |
| [仓库资料](REPOSITORY_SETUP.md) | About 简介、项目主页与主题标签 |
| [发布说明草稿](RELEASE-v1.6.6.md) | 后续发布说明与附件清单 |
| [发布核对清单](RELEASE_CHECKLIST.md) | 仓库与附件范围、运行包验收、尚待完成的发布事项 |
| [QA 记录](QA-1.6.6.md) | 标明日期的验证结果与范围 |
| [第三方核查](THIRD_PARTY_REVIEW.md) | 分发依据和未解决事项 |

更改用户可见行为时，同步维护两种语言。产品名为 **Luna**，作者为 **FrancisChen**，仓库为 **Francis-Chenyj/Luna-dev**；参考项目链接集中在 README 致谢。

以后准备程序新版本时，应同步更新源码、界面中的版本预期及分发资料；接口与包检查从元数据读取预期版本。仅调整工具和文档的改动先记入 **Unreleased**，不擅自提高程序版本。反馈表单与 Windows 工作流已在本地准备，GitHub 上的启用和实际 Release 发布仍需另行授权。

## Desktop host / 桌面窗口 (1.6.6)

`src/DesktopWindow.cs` owns the WinForms/WebView2 window and NotifyIcon. HTTP runs on a dedicated thread; query and update workers are unchanged. Close choices are stored as `closeAction: ask|tray|exit` in schema-1 preferences (missing field defaults to ask). Explicit Exit and Windows session end bypass the preference. `/api/window/close` and `/api/window/show` require the same POST, X-Luna, loopback Host and Origin checks as other mutations. Do not use them from external origins.

自有窗口在主 STA 线程运行，本地 HTTP 使用独立线程。托盘保持后台任务；退出取消更新、关闭监听并释放托盘和 WebView2。标题栏写入不再依赖可能不受支持的 DWM 属性读取。Windows 10 兼容性及高对比度仍应在发布前实机验证。

`scripts/desktop-dependencies.ps1` verifies and copies the pinned Microsoft SDK from `vendor/webview2`; builds do not fetch packages. Both runtime and source ZIPs include the required dependencies and Microsoft license. The installed Evergreen Runtime remains separate. Native navigation stays on this Luna origin; user-selected HTTP(S) external links open in the default browser. No remote debugging port or host-object bridge is exposed.

WebView2 uses `cache/webview2` and clears only DiskCache/CacheStorage asynchronously after an idle delay, at most once daily. Never delete the complete profile to clean caches: it contains the recent-query localStorage. Tests run in private folders and headless HTTP does not initialize WebView2.


See [Application lifecycle / 程序维护](APPLICATION_LIFECYCLE.md) for signing, installation, updates, rollback and uninstall.

服务归属、并发规则和测试迁移见[运行时架构](ARCHITECTURE.md)。独立运行新增套件：`tests/run-service-isolation.ps1`。

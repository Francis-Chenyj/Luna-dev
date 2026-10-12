# Luna 1.4.3：发布准备验证记录

日期：2026-10-12（Asia/Shanghai） · 作者：FrancisChen

程序版本仍为 1.4.3。本记录保留前一轮署名、README 语言拆分和帮助链接检查；随后完成的文档分层与入口复核单独记在末尾，避免将旧测试当作重新执行。

## 首轮署名与链接检查

| 检查 | 结果 |
| --- | --- |
| 构建 | 从当前源码构建成功；EXE 的 CompanyName 为 FrancisChen，版权为 Copyright (c) 2026 FrancisChen，产品版本为 1.4.3 |
| JavaScript 回归 | 现有核心与界面测试 70 项通过，0 失败 |
| 离线 HTTP 回归 | 隔离副本启动成功；856 条关系逐项匹配随包快照，参数隔离、正常空结果和本机访问限制通过 |
| 页面内容 | 隔离离线服务返回的 HTML 包含 FrancisChen；使用说明的项目主页指向 `https://github.com/Francis-Chenyj/Luna-dev`，注明仓库待建立，旧 Akari 入口已移除 |
| 本地文档链接 | 英文/中文首页、手册、日志及声明的本地文件和标题链接检查通过 |
| 原有数据 | 1,036 个 data 文件与修改前 SHA-256 逐项一致 |
| 快捷方式 | 项目目录的 Luna.lnk 已用当前署名重新生成 |
| 当前署名 | 当前源码、脚本、测试、文档、许可证和准备材料未残留旧作者名 |

离线 HTTP 本次样本为中位数 4.54 ms、P95 7.69 ms、最大 20.05 ms；这只是本机一次测量，不代表其他机器性能。

## 程序包检查

`scripts/package.ps1` 会重新构建，并将两份 README、CHANGELOG、许可证和声明同时收进运行包与源码包。

`tests/run-package.ps1` 核对 ZIP 与逐文件清单的全部哈希、必需文件、元数据和运行包/源码包边界。最终文件与哈希以 `dist/v1.4.3/release-files.json` 和 `SHA256SUMS.txt` 为准。

```powershell
node --test .\tests\core.test.cjs .\tests\ui.test.cjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-package.ps1 -ReleaseDirectory .\dist\v1.4.3
```

## 首轮检查范围

- 本次测试使用完整版本的现有数据与隔离缓存，未执行真实上游更新。
- 本次未重跑更新调度、存储清理等未改动模块的全部 C# 套件，也未进行新一轮真实浏览器视觉检查。
- 旧截图、旧程序包及原验证记录保存在本地 `research/francischen-docs-review/` 和既有研究归档中，未将历史记录冒充为本次测试。
- 技术验证不改变[第三方分发核查](THIRD_PARTY_REVIEW.md)的结论。本次仅修改和打包本地文件。

## First-pass summary in English

The current executable builds with FrancisChen attribution. All 70 existing JavaScript tests passed, and an isolated offline instance passed all 856 snapshot queries and access-boundary checks. A subsequent offline HTML check confirmed that help links to Luna's planned repository at https://github.com/Francis-Chenyj/Luna-dev, labels it as not yet created, and no longer contains the Akari link. Local documentation links resolve, and all 1,036 bundled data files remain unchanged. Packaging checks cover both READMEs, manifests, hashes and source/runtime separation. This pass did not repeat live upstream or browser visual testing.

## 文档分层与入口复核 — 2026-10-12

- 复核程序帮助、双语 README、双语用户指南和开发指南的 Luna 项目地址、FrancisChen 署名及“仓库待建立”说明；参考项目仅在 README 致谢中链接。本地补齐 [About 与主题标签](REPOSITORY_SETUP.md)。
- 将 README 精简为项目介绍、预览、运行要求和导航；用户指南按操作组织，开发指南独立说明构建、测试与打包，CHANGELOG 保留版本变化。
- 检查当前全部 Markdown 文件的相对路径、图片和标题锚点；双语指南的 12 个 PowerShell 代码块通过语法解析。
- 直接执行开发指南的隔离离线 HTTP 示例：856 条快照与访问边界检查通过；本次样本中位数 4.54 ms、P95 7.85 ms、最大 23.16 ms。示例启动的服务已在结束时停止。
- 在隔离离线副本的真实浏览器页面检查查询界面和使用说明，确认主页地址为 Luna，截取当前界面用于 README；未进行外网数据更新。
- 源码、界面资源、数据、运行 EXE 及现有构建与测试脚本逐项保持不变。仅文档、配图和本地打包产物更新，未重跑未改动模块的完整单元测试套件。

文档配图条件见[配图说明](images/README.md)。本轮本地检查原始记录和修改前文档保存在已忽略的 `research/docs-organization-20261012/`。程序包按上文流程重新生成与校验；最终哈希以当前包旁的清单为准。

Documentation follow-up: project identity and language navigation were rechecked, separate user/developer guides were prepared, all PowerShell examples parsed, and the documented isolated HTTP example passed 856 snapshot checks. A real offline browser view supplied the README preview and confirmed Luna's project link. Application code and bundled data remain unchanged; no GitHub account settings, repository or Release were created.

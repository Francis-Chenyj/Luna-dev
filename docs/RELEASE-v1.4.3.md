# Luna 1.4.3 — 本地发布说明草稿

作者：FrancisChen · 日期：2026-10-11 · 原创代码许可：MIT

**状态：仅供本地审阅，尚未发布。** 计划仓库为 `Francis-Chenyj/Luna-dev`，计划标签为 `v1.4.3`。第三方数据 / 素材与产品注册事项见[核查记录](THIRD_PARTY_REVIEW.md)；本说明不表示已取得整体分发许可。

## 本版功能

- 首页搜索和最近英雄从该英雄的常用分路开始；详情页连续查询沿用当前分路，历史导航恢复对应分路。
- 缺失分路由用户选择；有效分路下的空结果不会自动换路。
- 本地查询、默认离线库、当前英雄优先更新、断点续传与空间管理继续保留。

本次本地发布准备统一作者署名，加入 MIT 和第三方声明，整理双语 README、用户指南和开发指南，并重新构建可核验的分发包。详细变更见[更新记录](../CHANGELOG.md)。

使用方法见[中文用户指南](USER_GUIDE.zh-CN.md) / [English user guide](USER_GUIDE.md)；构建与测试见[中文开发指南](DEVELOPER_GUIDE.zh-CN.md) / [English developer guide](DEVELOPER_GUIDE.md)。

## 下载文件

| 文件 | 使用方式 |
| --- | --- |
| `Luna-v1.4.3-Windows.zip` | 完整解压，运行 `Luna.exe` 或 `启动Luna.cmd`；无需开发工具 |
| `Luna-v1.4.3-Source.zip` | 含源码、构建脚本、数据、文档与测试；无 EXE |
| `SHA256SUMS.txt` | 核对 ZIP 与逐文件清单的 SHA-256 |
| `release-files.json` | 核对 ZIP 内各文件的路径、字节数及哈希 |

本地输出目录为 `dist/v1.4.3/`。ZIP 内都有顶层 `Luna/` 目录、许可证、第三方说明和本地审阅状态文件。`_build/` 是打包产生的本地构建目录，不是下载附件。

## 安装、升级与校验

需要 Windows 10 / 11、.NET Framework 4.7.2+ 和现代浏览器；优先使用 Edge 应用窗口。ZIP 便携包不需要管理员权限，当前 EXE 没有代码签名。支持平台是产品目标；本次测试不等于覆盖所有 Windows 版本和硬件。

1. 退出旧 Luna，再将新运行包解压到可写目录。不要只复制 EXE。
2. 从旧目录迁移需要保留的 `cache`，或在旧目录中谨慎覆盖程序文件并保留缓存。
3. 从新目录运行程序；需要桌面入口时重新运行创建快捷方式脚本。
4. 普通启动会检查数据更新；离线入口禁止外网更新。首次默认条件查询不必等待全库下载。

在附件所在目录运行：

```powershell
Get-FileHash -Algorithm SHA256 .\Luna-v1.4.3-Windows.zip
Get-FileHash -Algorithm SHA256 .\Luna-v1.4.3-Source.zip
```

将结果与 `SHA256SUMS.txt` 中对应文件名的值比对。校验值用于检查内容一致性，不是数字签名。

## 数据与已知限制

- 随包包含当前 173 位英雄目录与默认条件的 856 份关系记录；其他条件按需下载。
- 关系值不是胜率。来源更新时间与游戏补丁时间不是同一个概念。
- 数据更新不自动安装新版程序；原站故障或数据变化可能影响联网更新。
- 本次没有重新采集第三方关系数据，随附数据保持原样；未对个人缓存执行回写或清理，正在运行的旧实例可能继续更新自己的运行记录。
- 原创代码的 MIT 许可不覆盖第三方游戏素材、上游数据或截图中的第三方内容。

实际验证结果见 [QA-1.4.3.md](QA-1.4.3.md)。

## English

Luna 1.4.3 is a local review build by FrancisChen. Home/recent searches use the champion's common lane; detail-page navigation retains the current lane. Existing offline queries, focused updates, resumption and storage maintenance remain.

Extract the Windows ZIP and run `Luna.exe`. The source ZIP contains no executable. Windows 10/11, .NET Framework 4.7.2+ and a modern browser are required. Keep your existing cache when upgrading. Verify ZIP hashes against `SHA256SUMS.txt`.

Original code is MIT-licensed. Third-party distribution and Riot product-registration evidence remain unresolved; this is not a public GitHub release. See the [review](THIRD_PARTY_REVIEW.md), [notices](../THIRD_PARTY_NOTICES.md) and [verification](QA-1.4.3.md).

# Luna 仓库资料 / Repository details

作者：**FrancisChen** · 整理日期：2026-10-13

Luna-dev 是 Luna 的公开开发仓库。产品名保持 Luna；首个下载版本标记为 Pre-release。此文件记录仓库展示信息和后续维护规则。

## 项目身份与入口

| 项目 | 统一内容 |
| --- | --- |
| 产品名称 | Luna |
| 作者与维护者 | FrancisChen |
| GitHub 账号 | [Francis-Chenyj](https://github.com/Francis-Chenyj) |
| 仓库名称 | `Luna-dev` |
| 项目主页 / About 的 Website | [Luna on GitHub](https://github.com/Francis-Chenyj/Luna-dev) |
| 首页语言 | 默认英文 `README.md`，顶部跳转到简体中文 `README.zh-CN.md` |

程序内项目主页、两份 README 和用户指南使用上述同一项目地址。作者主页保留账号的实际拼写 `Francis-Chenyj`，显示署名使用 `FrancisChen`。参考项目链接集中在双语 README 的致谢部分；数据来源及其声明仍在相应数据说明中标明。

## About 简介

建议使用英文，与仓库默认首页一致，可直接复制：

> Luna is a Windows League of Legends matchup explorer with offline queries, bilingual search, lane and rank filters, and background data updates. In development; maintained by FrancisChen.

对应中文：

> Luna 是 Windows 上的英雄联盟对局查询工具，提供离线查询、中英文搜索、分路与分段筛选，以及后台数据更新。开发中，由 FrancisChen 维护。

## Topics 主题标签

`league-of-legends` · `matchup` · `offline` · `windows` · `csharp` · `dotnet-framework` · `javascript`

这些标签描述当前产品用途和实际技术，不将参考项目名称、官方认可或尚未实现的功能当作标签。

## 仓库维护规则

- 默认分支为 `main`，产品标题使用 Luna，署名使用 FrancisChen。
- About 使用上述简介、项目主页和主题标签；下载统一指向 [Releases](https://github.com/Francis-Chenyj/Luna-dev/releases)。
- 开发版本标记为 Pre-release，首个标签为 `v1.5.0-dev.1`，程序内版本为 `1.5.0`。
- `.github/ISSUE_TEMPLATE/` 提供双语错误反馈与功能建议；默认分支更新后检查实际表单。
- `.github/workflows/windows-checks.yml` 调用 `scripts/check.ps1`，只构建与检查，不自动发布。云端结果以 Actions 对应提交为准。
- 只上传源码清单中的文件；排除 `cache/`、`research/`、`dist/`、日志、个人快捷方式与凭据。

仓库与 Release 附件范围见[发布核对清单](RELEASE_CHECKLIST.md)。第三方证据状态由 [release-status.json](../release-status.json) 单独记录，公开开发状态不改变待办。

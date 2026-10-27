# 第三方内容声明 / Third-party notices

维护者：FrancisChen · 当前适用版本：Luna 1.7.1 · 配图范围更新：2026-10-28（Asia/Shanghai）

首次来源核查为 2026-10-11，既有政策复核为 2026-10-12；2026-10-28 仅同步当前版本与 README 配图范围。日期更新不表示已重新核查政策、取得新增授权或通过产品注册 / 审阅，具体依据与未决事项见[核查记录](docs/THIRD_PARTY_REVIEW.md)。

Luna 的原创程序代码、构建脚本和原创说明采用根目录的 [MIT 许可证](LICENSE)。本文件区分随附内容的来源与授权范围；不授予维护者无权授予的第三方权利。**当前完整包作为开发预发布版本提供，第三方内容的公开分发条件尚未全部确认；预发布并不代表取得这些授权。**证据与待办见[核查记录](docs/THIRD_PARTY_REVIEW.md)。

| 内容 / 文件范围 | 来源 / 权利说明 | 当前处理 |
| --- | --- | --- |
| `data/library/**`、`data/seeds/**`、`data/catalog.json` 中的分路统计与关系信息 | [MalzaharAI](https://www.malzaharai.top/counter) 的公开页面和接口；上游作者及相关权利人保留其权利 | 未找到明确允许第三方将离线快照打包再分发的条款；不标记为 MIT |
| `data/icons/**`、英雄名称、称号及游戏相关图像 | League of Legends / Riot Games 及相关权利人；现有下载脚本指向 MalzaharAI 的头像路径，现存图像缺少逐文件来源链 | 不标记为 MIT；官方 Data Dragon 的使用依据和注册要求见下文，不能据此直接认定现有整包已获许可 |
| `data/catalog.json` 中的简体名称、称号、拼音和基础关键词 | [腾讯英雄目录](https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js) | 未确认该端点附带独立的开放数据或再分发许可；保留来源，待核实 |
| `data/search-aliases.json`、`web/aliases.js` 中的英文和繁体标签 | [Riot Data Dragon](https://developer.riotgames.com/docs/lol#data-dragon)，原整理版本 16.20.1 | 属于游戏内容，适用 Riot 条款；不是 MIT 授权的原创词汇 |
| `data/community-aliases.json` 与派生搜索词中的社区叫法 | 人工筛选整理；参考来源逐项列在 `data/search-aliases.json` 的 `sources` 中 | 整理工作不改变上游内容的权利；没有将整篇文章引入项目，也不据此声称所有来源已授权 |
| `docs/urgot-mundo-example.png` | 用户提供的示例截图，包含上游界面 / 游戏内容 | 提供用于本项目说明，不等同于已确认可按 MIT 任意再分发其中的第三方内容 |
| `docs/images/luna-v1.7.1-matchups-en.png`、`docs/images/luna-v1.7.1-matchups-zh-CN.png` | 2026-10-28 从已发布的 Luna 1.7.1 隔离离线副本截取的英文浅色、简体中文深色界面；包含现有英雄头像、名称及随包关系数据 | 当前双语 README 产品预览；保留截图内第三方内容原有范围，截取条件见[配图说明](docs/images/README.md) |
| `docs/images/luna-matchups.png` | 2026-10-12 从 Luna 1.5.0 隔离离线副本截取的英文浅色界面；包含现有英雄头像、名称及随包关系数据 | 保留供历史记录引用；适用原有第三方内容范围，截取条件见[配图说明](docs/images/README.md) |
| `docs/images/luna-matchups-zh-CN.png` | 2026-10-12 从 Luna 1.5.0 隔离离线副本截取的简体中文深色界面；包含现有英雄头像、名称及随包关系数据 | 保留供历史记录引用；适用相同的第三方内容范围，截取条件见[配图说明](docs/images/README.md) |
| `web/logo.svg`、`web/app*.ico`、`web/app-*.png` | Luna 绿色 L 图标；构建脚本使用基本几何图形生成 | 项目自有资源，随原创代码采用 MIT；不代表 Riot 或 MalzaharAI 的标志 |

## Riot 官方资源

[Data Dragon 文档](https://developer.riotgames.com/docs/lol#data-dragon)明确将游戏数据与素材提供给第三方开发者使用。[通用政策](https://developer.riotgames.com/policies/general)要求使用支持的资源、展示非官方声明，并注册产品接受审核；[LoL 注册说明](https://developer.riotgames.com/docs/lol#registration)将这一要求扩展到不使用官方 API 的面向玩家产品。使用还受 [API 条款](https://developer.riotgames.com/terms)与 [Legal Jibber Jabber](https://www.riotgames.com/en/legal)约束。

这些文件提供有条件的使用依据，不将素材转为 MIT，也不能代替 Luna 自身的产品注册。当前项目中未找到 Luna 已注册或获审阅的证据。

按 Riot 通用政策展示的声明：

> Luna isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

Luna 与 MalzaharAI、腾讯及 Riot Games 无隶属或背书关系。商标、名称与素材的权利归各自权利人。

## 运行环境与依赖

本项目使用 Windows / .NET Framework 系统组件，并调用用户电脑已有的浏览器。发布包不附带 Windows、.NET、Edge、Node.js 或 Python。前端没有打包第三方框架；这不改变上表列出的数据、标签与素材的来源。

## English

The [MIT license](LICENSE) covers Luna's original code, build scripts, original documentation and its own geometric L icon. It does not relicense third-party game assets, labels, upstream matchup data or third-party content within screenshots. The table above identifies affected paths and sources.

This document applies to Luna 1.7.1. The README image scope was updated on 2026-10-28 with English/light and Simplified Chinese/dark screenshots (`luna-v1.7.1-matchups-en.png` and `luna-v1.7.1-matchups-zh-CN.png`) from a verified isolated offline copy of the published 1.7.1 package. The 1.5.0 screenshots remain as historical references. The initial source review and earlier policy review retain their 2026-10-11 and 2026-10-12 dates; this image update does not constitute a new policy review, permission or completed product registration/review.

The current complete packages are **development pre-releases**. Pre-release publication does not resolve the pending third-party permissions. MalzaharAI snapshot redistribution and third-party update-access terms remain unconfirmed. The Tencent catalog endpoint has no confirmed open-data license in this review. Riot permits use of official Data Dragon resources subject to its policies; product registration/review and the provenance of the presently bundled images remain unresolved for Luna. See the [dated review](docs/THIRD_PARTY_REVIEW.md) before distributing these files.

## Microsoft WebView2 SDK (1.5.2+)

Luna includes unmodified Microsoft.Web.WebView2 1.0.4258.31 Core/WinForms assemblies and native loaders. Copyright Microsoft Corporation. Redistribution terms are included in [vendor/webview2/LICENSE.txt](vendor/webview2/LICENSE.txt); provenance/checksums are in [vendor/webview2/README.md](vendor/webview2/README.md). These files retain their own license. The separate WebView2 Runtime is supplied and updated by Microsoft.

Luna 随包提供未经修改的微软 WebView2 SDK DLL 与加载器，保留微软版权与完整许可证；不属于 Luna 原创代码的 MIT 授权范围。完整浏览器运行环境由微软单独提供及维护。

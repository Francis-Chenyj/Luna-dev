<p align="center">
  <img src="web/logo.svg" width="96" height="96" alt="Luna：绿色底、黑色 L 图标">
</p>

# Luna

[English](README.md) · **简体中文**

**v1.5.0** · Windows 10 / 11 · .NET Framework 4.7.2+ · [MIT — 原创代码](LICENSE)

用于英雄联盟选人阶段的本地对局查询工具。搜索英雄，即可查看 **严父（谁克制我）** 和 **大儿（我克制谁）**，并按分路与分段筛选结果。

**项目主页：**[Luna · GitHub](https://github.com/Francis-Chenyj/Luna-dev)。

在 **设置 → 常规** 中可即时切换简体中文 / English，选择深色、浅色或跟随系统。README、用户指南和开发指南均分别提供英文与简体中文版本。

## 功能

| 功能 | 说明 |
| --- | --- |
| 本地查询 | 默认条件离线库覆盖 173 位英雄、428 个有效英雄分路组合。 |
| 灵活搜索 | 支持中英文姓名、称号、拼音及整理的英雄外号。 |
| 双向关系 | 按分路、分段分类、克制阈值和场次严谨度筛选两侧结果。 |
| 后台更新 | 优先更新当前及最近英雄；整库下载支持断点续传，失败时回退到已保存的数据。 |
| 设置与排错 | 保存语言与主题偏好，设置启动数据更新，打开本地目录及复制精简诊断。 |
| 来源与时间 | 区分程序自带、联网下载和备用旧数据，显示原站生成时间与本机保存时间。 |

关系结果来自 [MalzaharAI](https://www.malzaharai.top/counter)。**关系值不是胜率**，数据生成时间也不是游戏补丁日期。

## 界面预览

![Luna 1.5.0 离线模式下的亚托克斯上路查询界面](docs/images/luna-matchups-zh-CN.png)

Luna 1.5.0 简体中文、深色主题，使用随包数据：亚托克斯、上路、阈值 1、严谨度 5，查看范围选择“全分段”分类。此图用于展示界面，不代表实时推荐。[截图说明](docs/images/README.md)。

## 下载与快速开始

**开发中 · 预发布版本。**请到 [Releases 下载页](https://github.com/Francis-Chenyj/Luna-dev/releases)获取 `Luna-v1.5.0-Windows.zip`。`Luna-v1.5.0-Source.zip` 用于开发，不含可执行程序。请一并下载 `SHA256SUMS.txt` 核对 ZIP 的 SHA-256。第三方分发授权和 Riot 注册仍待完成，详见[分发状态](release-status.json)与[第三方声明](THIRD_PARTY_NOTICES.md)。

**运行要求：**Windows 10 / 11、.NET Framework 4.7.2 或更高版本，以及现代浏览器。优先使用 Edge 独立应用窗口。普通使用无需 Node.js、Python、账号、API Key 或管理员权限。

下载 Windows 运行包后：

1. 将完整 ZIP 解压到可写目录，例如 `D:\Apps\Luna`。
2. 双击 **`Luna.exe`** 或 **`启动Luna.cmd`**。
3. 搜索英雄、选择分路，查看两侧克制列表。

需要禁止外网更新时，运行 **`离线启动Luna.cmd`**。安装、设置与数据迁移、快捷方式和故障处理见[用户指南](docs/USER_GUIDE.zh-CN.md)。更新对局数据不会升级 Luna 程序本身。

## 文档导航

| 想了解什么 | 阅读位置 |
| --- | --- |
| 安装使用、理解结果、管理数据 | [用户指南](docs/USER_GUIDE.zh-CN.md) |
| 构建、测试、打包源码 | [开发指南](docs/DEVELOPER_GUIDE.zh-CN.md) |
| 各版本发生了哪些变化 | [更新日志](CHANGELOG.md) |
| 数据与素材的来源和声明 | [第三方声明](THIRD_PARTY_NOTICES.md) |

## 问题反馈

支持中英文的[错误反馈](https://github.com/Francis-Chenyj/Luna-dev/issues/new?template=bug-report.yml)与[功能建议](https://github.com/Francis-Chenyj/Luna-dev/issues/new?template=feature-request.yml)表单用于收集反馈。错误反馈请填写 Luna 版本、Windows 版本、复现步骤、预期与实际结果。可使用 **设置 → 排错 → 复制诊断信息** 提供简短摘要，截图与相关日志片段为可选项，分享前请移除个人信息。

## 作者与致谢

由 [**FrancisChen**](https://github.com/Francis-Chenyj) 创建和维护，开发过程中使用 [OpenAI Codex](https://openai.com/codex/) 辅助。

README 排版参考 [Yifan Ad Skipper](https://github.com/CharryLee0426/yifan-ad-skipper)。[League Akari](https://github.com/LeagueAkari/LeagueAkari) 为 GitHub 项目展示提供参考；[Codex Team Token Tracker](https://github.com/CharryLee0426/codex_team_token_tracker) 为用户与开发文档的分层提供参考。

## 许可证

Copyright © 2026 FrancisChen。Luna 的原创代码、脚本、说明和绿色 L 图标采用 [MIT 许可证](LICENSE)。第三方游戏素材、上游数据及截图中的第三方内容仍适用各自条款，详见[第三方声明](THIRD_PARTY_NOTICES.md)。

Luna 未获 Riot Games 官方认可，也不代表 Riot Games 或参与制作、管理其产品的相关人员的观点。Riot Games 及其相关名称和标志的权利归各自权利人。

# 第三方分发条件核查

首次来源核查：2026-10-11（Luna 1.4.3） · 当前版本复核：2026-10-12（Luna 1.5.0，Asia/Shanghai） · 维护者：FrancisChen

## 当前发布安排（2026-10-13）

维护者已选择将完整源码与 Windows 包发布至 `Francis-Chenyj/Luna-dev`，并以开发预发布形式提供。此选择不关闭任何第三方授权或 Riot 注册待办；`publicReleaseReady: false` 继续表示证据未齐全，而非仓库可见性。没有代为联系上游或注册 Riot。下列核查结论和过程保留其原日期与范围。

The maintainer selected a public development pre-release containing the complete source and Windows package. Unresolved third-party evidence and Riot registration remain unchanged. The dated review below is historical; publicReleaseReady is a review-completion flag, not repository visibility.

## 2026-10-12 核查结论（历史记录）

本地源码整理、MIT 授权及审阅包制作可以完成。**尚不能把包含现有全部数据和素材的包标记为已具备公开分发条件。**首次来源核查完成的是公开资料核对，不是取得上游授权或通过 Riot 产品审核；当前版本补充复核的范围见下文。没有代用户注册、联系上游或发布任何文件。

| 核查项 | 结果 | 发布前需要的证据 / 处理 |
| --- | --- | --- |
| 项目原创代码的 MIT 授权 | 已由作者明确选择，已落地 `LICENSE` | 保留版权行与许可全文 |
| MalzaharAI 离线快照及第三方客户端更新 | 已访问公开页面，未发现明确再分发许可 | 找到适用的公开条款，或由维护者向上游确认离线快照、GitHub 源码包、程序包及更新请求的允许范围 |
| Riot 官方 Data Dragon 资源 | 有条件的第三方使用依据 | 按官方政策完成适用的产品注册 / 审阅，保持声明与产品信息准确 |
| 当前头像与腾讯英雄目录 | 尚无完整的分发依据及逐文件来源链 | 确认现有来源的条款；必要时改用有明确依据的官方来源并记录版本、URL 和校验值 |
| 社区词表及用户示例截图 | 有来源记录，但范围未逐项确认 | 确认截图可用于公开说明，排查隐私；确认仅保留所需短标签，不复制来源文章或不明素材 |

`release-status.json` 因此保留 `publicReleaseReady: false`。它记录的是当前证据状态，不宣称上游禁止所有用途。

## 当前版本复核 / Current-version review

2026-10-12 对 Luna 1.5.0 的补充复核：

- 当前源码与已有本地完整包仍包含关系快照、英雄目录、头像和整理词表；本次只修正文档，没有替换数据源、采集新数据或改动程序。
- 双语 README 的配图均为 2026-10-12 截取的 Luna 1.5.0 界面：`docs/images/luna-matchups.png` 是英文浅色，`docs/images/luna-matchups-zh-CN.png` 是简体中文深色。[第三方声明](../THIRD_PARTY_NOTICES.md)已分别列明，两图的完整条件见[配图说明](images/README.md)。用户提供的厄加特 / 蒙多示例另行保留来源记录。
- 重新核对 [Riot 通用政策](https://developer.riotgames.com/policies/general#product-registration)与 [LoL 注册说明](https://developer.riotgames.com/docs/lol#registration)：前者要求产品注册与审阅，后者明确面向玩家的产品即使不使用官方 API 也要注册。当前本地资料仍没有 Luna 已完成该流程的证据。
- MalzaharAI 与腾讯部分仍沿用首次核查的证据，未把本次文档修正记为重新取得上游授权；第三方分发状态保持待确认。
- 已生成的审阅 ZIP 保留原样。本次文档修正记录于 CHANGELOG 的 Unreleased，并纳入 `dist/v1.5.0-final-review-r2/` 的本地重新打包，生成新的清单与校验值。此次同步不关闭任何第三方授权或 Riot 注册待办；具体附件及检查范围见[发布核对清单](RELEASE_CHECKLIST.md)。

The 2026-10-12 follow-up covers Luna 1.5.0 documentation and the two current README previews, and rechecks Riot's public registration policies. The earlier MalzaharAI/Tencent evidence retains its original date. No program, data source, bundled data, permission status or registration status changed. Existing review ZIPs remain unchanged; these documentation corrections are included in the local repackaging at `dist/v1.5.0-final-review-r2/`, with a new inventory and checksums. Third-party permissions and Riot registration remain open; see the [release checklist](RELEASE_CHECKLIST.md) for the selected artifacts and verification scope.

以下第 1–4 节保留 2026-10-11 首次来源核查的过程和依据；其中历史响应、文件大小及哈希不代表本次重新请求的结果。

## 1. MalzaharAI

核对了[严父页面](https://www.malzaharai.top/counter)的公开 HTML、导航与链接、页面中的许可相关关键词，以及针对该站点的公开搜索结果。页面说明其查询使用公开缓存；这描述了网站功能，不等于允许其他项目打包并再次分发数据。

- 直接只读请求返回 HTTP 200；HTML 长度为 80,089 个字符。
- 本次取得的 HTML 未检出 `license`、`copyright`、`授权`、`转载`、`分发`、`许可`、`条款`、`协议`、`开源`等关键词；可见链接没有指向数据再分发许可。
- 未进行批量数据采集，也未通过登录、购买或规避访问限制获取内容。
- 页面提供上游作者的[哔哩哔哩主页](https://space.bilibili.com/3493129149745401)及使用指南。该链接仅作后续联系入口；本次没有发送消息。

本地证据存于忽略目录 `research/release-review/malzahar-counter.html`，不进入仓库或发布包。SHA-256：`8adad744569d998bbbcf5184c4badf9334c51857799b1d64e4f085b973bd0587`。

建议后续确认的具体范围：默认离线库和历史快照能否随 GitHub 仓库、源码 ZIP、Windows ZIP 分发；能否持续调用公开接口供第三方客户端更新；是否要求额外署名、请求频率、使用范围或禁止商业再分发。本次没有将未找到许可写成“已获授权”，也没有将没有发现条款写成“明确禁止”。

## 2. Riot Games

访问的主要官方依据：

- [Data Dragon](https://developer.riotgames.com/docs/lol#data-dragon)：面向第三方开发者提供静态数据与游戏素材。
- [General Policies](https://developer.riotgames.com/policies/general)：产品注册、审核、支持的资源来源及可见的非官方声明。
- [LoL Registration](https://developer.riotgames.com/docs/lol#registration)：面向玩家的产品即使不使用官方 API 也需要注册。
- [API Terms](https://developer.riotgames.com/terms)与 [Legal Jibber Jabber](https://www.riotgames.com/en/legal)：资源权利、使用条件及游戏相关知识产权边界。

判断：官方资源有使用途径，但不能据此把游戏素材授予 MIT。当前没有查到本项目的注册或审阅材料；新增的声明已放在项目首页、第三方声明与应用页脚，但声明本身不完成注册。Luna 的 MIT 代码可以允许商用，游戏素材能否用于某种商业产品仍取决于相应第三方条款。

现有 `scripts/download-icons.mjs` 请求 MalzaharAI 的头像路径；当前目录没有逐个头像的上游 URL、原始版本与内容一致性记录。因此，本次不宣称本地全部头像已经验证为官方 Data Dragon 原件。

## 3. 腾讯目录、社区词表与截图

`src/Program.cs` 与数据维护脚本将[腾讯英雄目录](https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js)作为简体标签来源。本次直接请求该端点返回 HTTP 200，取得 94,730 个字符的响应，未检出许可 / 版权 / 授权相关字段。另访问了[腾讯游戏许可及服务协议](https://game.qq.com/contract.shtml)，返回 HTTP 200；该协议包含游戏知识产权及复制、转载等使用行为的限制，没有据此确认该 JSON 数据集获得独立的开放数据或再分发授权。

来源 URL 和可访问性不构成独立的开放许可证；通用游戏服务协议的具体适用范围也需要结合该数据来源判断。本次结论仍为“专门分发依据未确认”，没有直接判定 Luna 违法或禁止一切使用。两份响应保存在忽略目录：

- `research/release-review/tencent-catalog.txt`：SHA-256 `dae235f00924dce1a53e1919102266aa7a9ba046f7ee0790edd14bea15726339`。
- `research/release-review/tencent-game-contract.txt`：SHA-256 `87d5c9951b1c27ef054723eb4f31de1f8fae5ef22ba63b60b5e29b93bbc9a3a3`。

`data/search-aliases.json` 记录 Data Dragon、腾讯、九游、不乐秀、PTT、搜狐、萌娘百科与哔哩哔哩的参考链接。代码保存整理后的名称 / 别名，不附带这些来源的整篇文章；这不等于已审定每个来源的授权范围。

`docs/urgot-mundo-example.png` 在旧说明中标记为用户提供；历史界面截图也包含上游数据或游戏头像。截图中的游戏内容不属于维护者的 MIT 授权范围。现有图片没有被删除、改图或对外发送。

## 4. 已落实与后续事项

- 已新增 `LICENSE`、`THIRD_PARTY_NOTICES.md`、本记录和随包的 `release-status.json`。
- README 和发布说明明确当前为本地审阅版，不放置尚不存在的 GitHub 下载链接。
- 打包脚本携带许可证与声明，不会把研究证据、个人缓存和日志加入包中。
- 不通过降低核查标准、补一段免责声明或更改许可证名称来替代尚缺的授权 / 注册证据。

维护者取得新证据后，可更新本记录及 `release-status.json`，再重新打包。当前包应保留在本地审阅；尚未执行任何 GitHub 操作。

## English summary

Initial source review: 2026-10-11 for Luna 1.4.3; current-version follow-up: 2026-10-12 for Luna 1.5.0. Original code is licensed under MIT by the author's instruction. The initial review found no explicit MalzaharAI snapshot-redistribution permission on the inspected public page or its visible links. Official Riot documentation supports conditional use of Data Dragon resources and requires player-facing products to register; Luna's registration/review evidence is not present. Bundled champion portraits lack a per-file provenance record, and Tencent catalog redistribution terms remain unconfirmed. The two current README screenshots now have matching version/language records; this does not resolve the rights to their embedded third-party content. Screenshot and curated-content scope should be resolved before redistribution. These are unresolved evidence items, not a claim that all third-party use is prohibited. No author was contacted and no account or repository was changed.

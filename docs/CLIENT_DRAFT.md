# BP champion synchronization / BP 英雄同步

Luna 1.7.2 development pre-release. This feature is off on every application start. Select **Connect client / 连接客户端** on the home page to enable it for this run; **Disconnect / 断开连接** cancels polling and clears the live slots. Local matchup queries remain available. Offline launch disables upstream updates but still permits an explicitly enabled connection to the local League client.

## 使用与显示

1. 打开并登录英雄联盟客户端，在 Luna 首页选择“连接客户端”。Luna 不会代为登录、启动匹配或选禁英雄。
2. 进入英雄选择后，我方、对方各保留五个位置。可见英雄随客户端快照出现；我方预选与已锁定状态分开显示，敌方预选不显示。
3. 点击英雄头像才查询该英雄的本地“严父／大儿”。同步十个位置不会自动下载数据或查询十份详情；现有阈值和严谨度筛选保持不变。
4. 不确定的分路不会被当作实际位置。Luna 可采用该英雄本地常用分路开始查询，并允许用户切换；这不是对局位置推断。
5. 离开 BP、断线或关闭连接时清空实时位置。已经打开的本地查询继续可用。

“选人轮次”来自客户端动作分组，不代表毫秒级先后。同一轮可以同时选人，中途连接不能还原未观察到的真实点击顺序。英雄交换以后，头像以当前队伍快照为准。训练模式常常不足十名玩家，因此单人训练通过不能证明完整十人 BP、交换或敌方可见时机均已实机验收。未知英雄显示暂未收录，不借助其他客户端接口补充隐藏信息。

## Privacy boundary / 隐私边界

The service only sends GET requests to two fixed paths on `https://127.0.0.1:<discovered-port>`:

- `/lol-gameflow/v1/gameflow-phase`
- `/lol-champ-select/v1/session` (only during champion selection)

It does not call player profiles, identity lookup, match history, rank, chat, matchmaking or pick/ban mutation endpoints. It does not subscribe to the general client event stream. Proxies and redirects are disabled. Credentials are discovered only for the running League client, remain in memory, and are never included in URLs exposed to the page, logs, diagnostics or saved files. Certificate handling is scoped to the particular loopback request; global TLS validation is unchanged.

**客户端原始选人响应可能夹带昵称、账号标识或聊天凭据。不能声称网络层从未收到这些字节。** Luna 只将阵营、槽位、英雄编号、预选／锁定、轮次和已知分路投影为独立对象；其余字段不保存、不展示、不转发，也不写入异常日志。后端返回给网页的对象不存在原始会话字段。连接开关不跨程序重启保存。

## Implementation and testing / 实现与验证

`ClientDraftService` owns its connection, cancellation and worker lifetime independently. Generation checks prevent late responses from resurrecting a disconnected or replaced session. Bounded timeouts, bounded response sizes, a fixed endpoint allowlist and fixed error codes keep network failures from leaking response bodies or blocking shutdown indefinitely.

The page uses stable native buttons, updates only changed contents and preserves keyboard focus. The public Luna routes retain Host, Origin, X-Luna, method and empty-body validation: GET `/api/draft`, POST `/api/draft/connect`, POST `/api/draft/disconnect`. Reading state does not enable client access. Tests use injected transports and synthetic sessions; browser regressions replace only these Luna routes with sanitized fixtures, never connect to an installed client by accident.

Run `tests/run-client-draft.ps1`, `node --test tests/ui.test.cjs`, and `scripts/check.ps1 -Browser`. See [QA](QA-1.7.2.md) for the evidence scope. A fixture passing proves that input scenario, not compatibility with every League region/client version.

## External compatibility and publication / 外部兼容与发布

LCU is an unsupported interface and may change without notice. Riot describes the need to register products and their LCU endpoint usage in its [League Client API documentation](https://developer.riotgames.com/docs/lol#league-client-api); current [general policies](https://developer.riotgames.com/policies/general) also apply. The maintainer must complete product/feature review before public release. Local implementation does not constitute approval.

Akari was consulted for [session types](https://github.com/LeagueAkari/LeagueAkari/blob/56cdfde00e56148f4641d1a8cf86acb66e903124/src/shared/types/league-client/champ-select.ts) and connection behavior. Its [README](https://github.com/LeagueAkari/LeagueAkari/tree/56cdfde00e56148f4641d1a8cf86acb66e903124) excludes Tencent-operated servers, so referencing Akari does not establish Tencent compatibility for Luna. Real-client validation must record the actual environment and visible behavior. No Akari source code is bundled by this implementation.

## Akari implementation comparison / Akari 源码对照

The review uses Akari dev commit `56cdfde00e56148f4641d1a8cf86acb66e903124`. Its [native command-line discovery](https://github.com/LeagueAkari/LeagueAkari/blob/56cdfde00e56148f4641d1a8cf86acb66e903124/native/win32-x64/src/tools/tools.cc) uses limited process-query access, and its [active subscriptions](https://github.com/LeagueAkari/LeagueAkari/blob/56cdfde00e56148f4641d1a8cf86acb66e903124/src/shared/constants/subscribed-lcu-endpoints.ts) select the general WebSocket event feed. Luna adopts limited process-query discovery first, with WMI as a fallback, and keeps its connection state and cancellation separate from other services. Akari's process-discovery polling must not be confused with its event-driven champion-selection updates.

This implementation polls the two fixed BP-related HTTP paths, then rechecks gameflow before publishing a session. A phase recheck reduces the phase/session race; it is not an atomic transaction with the client. The page and backend each refresh at approximately one-second intervals during BP, so this is periodic synchronization, not a guarantee of subsecond end-to-end updates. Real BP latency still requires measurement in an available match environment.

A general event stream would receive unrelated account/chat/module events. Endpoint-specific subscriptions can be considered after verifying their behavior on supported client builds; Akari's source documents a champion-selection event subscription issue. For this limited feature, bounded polling gives a complete current snapshot on connection and recovery without persisting credentials or consuming the general event feed. This is a deliberate scope choice, not a claim that polling is universally more reliable or faster than WebSockets.

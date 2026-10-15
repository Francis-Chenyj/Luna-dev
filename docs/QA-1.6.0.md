# Luna 1.6.0 — 验证记录 / Verification

日期 / Date: 2026-10-15. Windows 11 (NT 10.0.26300.0), Node.js 24.19.0, .NET Framework.

## 中文

本次为本地交付，未提交或发布到 GitHub。所有安装、升级、删除与网络故障测试使用独立副本，未拿用户的实际缓存做破坏性试验。

### 已完成

- 完整检查流程 17 步通过：94 项 JavaScript/界面/翻译/文档检查，9 组 C# 回归，源码构建、两轮默认离线库 HTTP 验证、程序包校验及解压源码再构建。
- 每轮 HTTP 校验覆盖 173 位英雄的 428 个有效分路、双方向共 856 条默认记录，逐条与随包数据比较。新增维护接口覆盖请求方法、来源、标记和非预期请求体的拒绝检查。
- 100 个损坏英雄缓存、20 MiB 超大记录、隔离区数量/字节/期限限制、未知文件保护、查询时校验、后台校验断点与空间分类加总通过。
- 更新说明签名被篡改、包路径穿越及非法路径被拒绝；断点下载从已保存的字节偏移继续，已验证的包不再次下载；离线模式禁止检查及下载。
- 使用真实 1.5.2 EXE 与签名 1.6.0 完整包验证升级，启动新进程并实际查询目录和对局，设置及额外文件保留。强制健康检查失败后恢复真实旧 EXE，旧版仍可查询。
- 用户级安装注册及移除、默认保留设置、明确选择清除数据、保留额外/改过的文件与源码目录保护通过。
- 实际通过应用维护接口交给独立卸载程序：主进程 326 ms 退出，程序文件被移除，设置与个人文件哨兵保留。
- 原生窗口人工验收：中文浅色、英文深色、标题栏随主题变化、应用维护与空间页、卸载目标路径、默认不勾选删除数据，以及取消操作。未覆盖所有 Windows 版本、DPI 和窄窗口组合。

### 每次只使用 30 秒

最终代码实际启动六轮，每轮运行约 30 秒后通过应用退出接口关闭；前三轮使用完整离线运行包，后三轮使用相同应用源码和受控更新响应。各轮原生窗口均成功就绪，没有遗留应用进程。

| 模式/轮次 | 窗口就绪 ms | 首次接口结果 ms | 查询次数 | 查询 P95 / 最大 ms | 保存的更新记录 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 离线 1 | 942.4 | 434.6 | 189 | 6.85 / 17.91 | 0 |
| 离线 2 | 462.6 | 141.0 | 191 | 4.48 / 14.09 | 0 |
| 离线 3 | 630.8 | 145.0 | 191 | 4.58 / 13.94 | 0 |
| 受控更新 1 | 955.3 | 443.1 | 188 | 6.47 / 13.88 | 40 |
| 受控更新 2 | 491.1 | 165.5 | 191 | 4.66 / 13.18 | 88 |
| 受控更新 3 | 506.6 | 174.9 | 191 | 4.55 / 13.56 | 136 |

已保存记录跨次保留且哈希一致；跨次重复下载为 **0**。阿狸中路的双向优先更新在后续运行持续可用。本轮 1,141 次本地接口查询全部成功，最慢 17.91 ms，未观察到后台更新导致查询变慢。以上不是公网下载速度：外部响应为受控样本；窗口时间是原生 NavigationCompleted，接口耗时不含用户输入和完整绘制。

### 大量缓存的启动对比

同一隔离目录模拟 25 种条件，每种保留当前与备份，共 42,800 条真实样本变换后的记录，压缩记录合计 161,548,793 字节。交替启动旧/新版各三次：

| 版本 | 启动至首次本地结果 | 单次查询 |
| --- | --- | --- |
| 1.5.2 | 3.342–3.460 秒 | 17.88–20.23 ms |
| 1.6.0 | 0.492–0.498 秒 | 18.92–19.23 ms |

这是无窗口新进程测试，Windows 文件缓存未清空，不等同于重启电脑后的冷缓存或所有用户的启动速度。只比较启动路径，不据此宣称全平台速度保证。

### 测试中修复的问题与限制

真实升级检查暴露了中文系统默认解码造成的误判，已显式使用 UTF-8。维护子进程继承本地监听句柄会阻碍父进程退出，已改为不继承该句柄的启动方式并实测交接成功。旧 PowerShell 环境的哈希命令加载问题改为直接使用 .NET；随后完整流程通过。最后交接修改再次经过六轮短会话及独立卸载测试，交付包另行进行构建与完整离线核对。

仍需区分：程序更新机制已实现和本地验证，但线上实际分发需要正式发布签名包；没有执行 GitHub 发布。未做断电硬件试验、系统重启后冷缓存基准、全 Windows 版本矩阵或长期内存泄漏判定。未知或唯一恢复文件采取保守保留，浏览器存储和整个目录没有绝对硬容量上限；空间统计是后台文件字节统计，繁忙/受限目录可能延后或不完整。

## English

This is a local delivery, with no GitHub commit or publication. Destructive installation/update/removal tests use isolated copies, never the user's working cache.

All 17 complete verification steps passed: 94 JavaScript/UI/i18n/repository checks, nine C# suites, build, two full offline HTTP passes, package verification and a build from extracted source. Each HTTP pass compares all 856 default-profile records (173 champions, 428 valid lanes, both directions) with the shipped snapshot. Maintenance API guards reject invalid methods, origins, request markers and unexpected bodies.

Tests cover 100 malformed hero records, oversized records, quarantine count/bytes/age, unknown-file preservation, query integrity, resumable audits and storage totals. Invalid signatures and unsafe archive paths are rejected. A partial application archive resumes from its saved offset and a verified archive is not downloaded again. Offline mode blocks application networking.

A real 1.5.2 executable was upgraded using an authenticated 1.6.0 full package. The health probe launches the new process and queries its catalog and actual matchups. Settings and personal files survive. A forced failed health probe restores the real old executable, which can still query. Per-user registration/removal, retained-data uninstall, explicit data removal, modified/extra-file preservation and source-folder protection passed. An actual HTTP-to-worker uninstall handoff exited the app in 326 ms, removed its program files and retained settings and a personal sentinel.

Native UI review covered Chinese/light, English/dark, matching title-bar colors, application/storage pages, uninstall path disclosure, an initially unchecked data-removal option and cancellation. It does not cover every OS, scaling level or narrow width.

Six final-code sessions ran for approximately 30 seconds each and exited through the application's real quit endpoint: three packaged offline launches, followed by three launches of identical application source with controlled upstream responses. The table above lists the exact results. All 1,141 local queries succeeded; the slowest was 17.91 ms. Persisted update records increased **40 → 88 → 136**, with unchanged hashes and **zero cross-session duplicate downloads**. Ahri's priority pair remained available. Native NavigationCompleted ranged from 462.6 to 955.3 ms. These are local HTTP/navigation measurements, not typing/full painting time or public-network throughput.

The large-cache fixture contained 25 profiles plus one backup each, totaling 42,800 reshaped real sample records (161,548,793 compressed bytes). Alternating three launches of each executable produced first local results in 3.342–3.460 s for 1.5.2 and 0.492–0.498 s for 1.6.0. This headless new-process comparison did not flush Windows caches and is not a reboot-cold or cross-machine guarantee.

Real integration uncovered and fixed system-default decoding in the health probe (now explicit UTF-8), and an inherited listening handle that delayed parent exit during maintenance (launch mode changed and handoff retested). Release scripts now hash directly through .NET to avoid old PowerShell module-loading differences. The final handoff changes were followed by another six short sessions and actual worker uninstall; delivered packages also receive a fresh build and full offline comparison.

Application update delivery still requires publishing the signed ZIP and metadata; no release was uploaded. Hardware power-loss testing, a full Windows matrix, reboot-cold benchmarks and long-term leak analysis were not performed. Unowned or potentially unique recovery files are preserved, so whole-directory/browser usage has no absolute quota. Storage measurements are background file-byte totals and may be delayed or partial while busy or permission-limited.

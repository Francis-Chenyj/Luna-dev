# Luna 验证记录 / Verification

> 历史验证记录。旧版界面截图保存在本地 research 历史归档中，不纳入当前程序包；当前署名和文档验证见 [QA-1.4.3.md](QA-1.4.3.md)。

## 1.4.0 搜索、独立英雄更新与空间管理 / Search, champion refresh and storage

日期 / Date: 2026-10-11。以下计时来自本机测试，不是所有电脑、网络或绘制速度的保证。模拟传输测试跳过真实请求间隔，不能据此预测更新速度。 / Timings are local measurements, not universal device/network/rendering guarantees. Simulated transports skip rate limits and do not predict update times.

- **55 项 JavaScript 检查通过**：173 位英雄的四类官方标签及 863 个人工词条逐一验证；歧义词多候选、精确匹配优先；窗口缓存条数、字节估算、闲置过期；首页更新入口；单英雄请求范围、完成后重读、失败保留、迟到响应与离线按钮状态。
- **单英雄更新套件通过**：同版手动更新只请求两份关系及前后两次版本状态，不下载英雄目录或整库；重复点击合并；两侧一起提交；已提交整库文件哈希不变；单侧失败最多三次后停止，原结果及时间不变；原站中途换版不发布混合数据；筛选条件隔离；重启／离线复用；整库进行中单英雄优先；任务记录上限 24。最终 856 条本地读取最慢 **42.81 ms**。
- **自动更新、稳定性、英雄优先与空间清理回归通过**：续传复用、取消释放连接、版本校验、故障回退、当前／上一完整库、连续 100 版与全部 25 个条件的独立保留、7 天无进展清理、个人文件／被占用文件／目录联接保护。模拟自动续传与发布期间 809 次并发读取最慢 **6.27 ms**；后台失败不阻塞全部 856 条默认结果。
- **实际 HTTP**：普通模式（为固定快照而关闭自动更新）核对 856 条内容、来源、时间、参数与本机访问边界，中位 / P95 / 最慢 **4.66 / 7.96 / 20.04 ms**。最终构建的离线模式相同验证为 **4.72 / 7.87 / 21.13 ms**。含本机 HTTP、校验、序列化，不含浏览器绘制。较早一次并行测试遇到连接重置；单独重试及最终构建复测均通过，未复现。
- **真实网络与浏览器**：通过英文称号 `the Blind Monk` 找到李青；“球女”保留辛德拉与奥莉安娜候选；首页无需选择英雄即可打开整库管理。手动更新李青打野 1/5，旧结果一直可见，完成后两侧均显示联网来源、相同保存时间，数据生成时间保持原站值。本次双向缓存 **17,042 字节**。最终 EXE 重启为离线模式后，两侧继续使用该文件，生成／保存时间不变，网络按钮禁用。浏览器没有警告或错误。测试仅操作独立临时目录。
- **路径边界**：更新临时文件缩短为同目录的固定长度唯一名称，避免将完整关系文件名再拼接 UUID。测试最终路径 240 字符、旧临时路径 277 字符、新临时路径 246 字符，压缩结果和元信息均可写入、读取和原子替换；过期新旧命名临时文件均受清理，近期文件保留。不宣称任意长度的安装路径都受支持。

All **55 JavaScript checks passed**, covering every official label and curated term for 173 champions, ambiguous candidates, exact-match ranking, cache entry/estimated-byte/idle limits, homepage management, precise champion-update scope, completion reloads, retained results, stale replies and offline controls.

The manual-champion suite passed: an unchanged-edition explicit refresh fetches two detail responses plus two version checks, with no catalog or whole-library download; duplicate requests coalesce; both directions publish together; committed full-profile hashes stay unchanged. Failure retries are bounded, old data/timestamps survive, mixed source editions never publish, profiles remain isolated, restart/offline reads persist, manual requests receive priority during full collection, and job states stay capped at 24. Maximum read time across 856 local records was **42.81 ms**.

Automatic-update, stability, focused-update and storage regressions passed, including resumption/reuse, cancellation, provenance/fallback, current/previous edition retention, 100 successive editions, all 25 profiles, expired partial downloads, and protection of personal files, locked files and junction targets. During simulated resume/publication, 809 concurrent reads peaked at **6.27 ms**. Failed updating left all default records readable.

The normal manual-only HTTP fixture verified all 856 bundled records and access boundaries at median/P95/max **4.66/7.96/20.04 ms**. Final-build offline checks measured **4.72/7.87/21.13 ms**, including HTTP, validation and serialization but excluding rendering. An earlier parallel test encountered a connection reset; isolated retry and final verification passed without reproduction.

Browser checks confirmed English-title search, ambiguous nicknames and homepage management. A real manual Lee Sin jungle 1/5 update kept old results visible, then displayed both downloaded badges and matching save times while preserving the source generation date. Its pair file was **17,042 bytes**. After restarting the final executable offline, that same file and its original dates remained usable and network controls were disabled. No browser warnings/errors were observed. User caches were untouched by these fixtures.

The path-boundary regression verified compressed data and metadata creation/read/atomic replacement at a 240-character final path: the old temporary name would have been 277 characters, while the new name is 246. Cleanup handles expired old/new temp names and retains recent files. This does not claim unrestricted long-path support.



---

# Luna 1.3.5 验证记录 / Verification

作者 / Author: FrancisChen  
验证日期 / Tested: 2026-10-11

## 1.3.5 短时使用的优先更新 / Priority updates for short sessions

- 47 项 JavaScript 检查通过。新增验证：本地优先级提示即使无响应也不阻塞查询；英雄更新发布后，全库仍在运行时页面即可刷新；显示双向更新范围和独立时间；离线查询不登记联网更新优先级。
- 新增 C# 英雄优先更新套件通过：当前英雄先于整库；只下载一侧时仍读取原数据；两侧及前后版本校验通过后原子启用；中途停止／重启保留；单侧失败、原站换版不覆盖此前可用结果；同版缓存与目录不重复请求；损坏回退及不同档位隔离；64 组保留与个人文件保护；未完成全库暂存被移除后，完整库仍能复用已验证英雄缓存。
- 856 条真实随包记录在带英雄更新缓存的本地读取测试中全部与原始关系一致，最慢 5.64 ms，没有外网请求。模拟自动更新续传及完整库切换时执行 771 次并发读取，最慢 5.86 ms。模拟源跳过请求限速，不能用其更新时间预测真实网络。
- 实际原站探测：在独立临时目录下载厄加特上路、默认 1/5 条件的严父与大儿，加上前后原站状态检查，用时 **5.03 秒**，压缩后的完整双向记录 **8,534 字节**。本次原站指纹未变；为测试单英雄更新流程，探测器仅清空测试副本内存里的完整库索引，未改动用户数据。计时不含启动延迟和浏览器轮询；不是全库更新时间或所有网络的保证。
- 最终 EXE 的本机 HTTP 测试在普通服务（固定快照使用 `--no-auto-update`）与离线模式分别核对全部 856 条：普通中位/P95/最大 5.10/8.41/22.37 ms；离线 4.94/8.47/21.28 ms。含本机 HTTP、文件校验、序列化，不含页面绘制。新增优先级 POST 的方法、来源标识、参数边界及“只记录优先级，不启动联网”均通过。
- 浏览器在独立离线验证副本中加载上述实际下载的英雄更新，确认两个方向均显示“联网更新的数据”“本英雄双向数据已更新”、一致保存时间和 13/26 条结果；页面无警告或错误。此副本有意不放完整库，以验证英雄更新独立可用；正式发行包仍包含完整默认库。
- 原有稳定性、启动自动更新与空间管理测试通过，包括连续 100 版、25 条件独立保留、原站中途换版、取消释放连接和目录联接保护。全部 1,034 份随包数据文件与 1.3.4 哈希一致。

All 47 JavaScript checks passed, including nonblocking priority hints, refreshing a published pair while full collection continues, explicit pair scope/timestamps, and offline behavior. The new C# suite verifies priority order, atomic two-direction publication, persistence across cancellation/restart, old-pair retention after failures or source changes, same-edition pair/catalog reuse, corruption fallback, profile isolation, 64-pair retention with personal-file protection, and whole-library reuse after staging removal.

All 856 real bundled relationships remained exact with focused overlays; maximum local read time was 5.64 ms without networking. During simulated automatic resumption and whole-profile publication, 771 concurrent reads peaked at 5.86 ms. Fixture transport skips rate limiting, so simulated update times do not predict real-network behavior.

An isolated live probe downloaded both actual default-profile Urgot top-lane responses and checked the source fingerprint before and after, taking **5.03 seconds** and saving **8,534 bytes**. The source edition was unchanged; the probe cleared only its temporary in-memory complete-profile index to exercise the pair path. User files were untouched. The timing excludes launch delay and browser polling and is not a whole-library or universal network guarantee.

The final executable passed 856 HTTP equality/provenance and boundary checks in each mode. Normal service (with automatic updates disabled for snapshot comparison) median/P95/max were 5.10/8.41/22.37 ms; offline was 4.94/8.47/21.28 ms. These include local HTTP, validation and serialization, excluding rendering. The new priority POST rejects wrong methods, missing request markers and invalid keys, and never starts an update itself. An isolated offline browser fixture using the real downloaded pair showed both downloaded badges, explicit champion scope, matching saved dates and 13/26 results without page warnings/errors. That fixture intentionally omits the complete library to prove pair independence; the release retains all default data. Existing stability, automatic-update and storage suites passed, and all 1,034 bundled files are byte-identical to 1.3.4.


以下为历史版本记录；1.3.4 的“等待全库完成”策略已由 1.3.5 的英雄优先更新补充。 / The following entries describe earlier releases; 1.3.5 adds independently validated champion-pair publication ahead of whole-library completion.

## 1.3.4 启动自动更新 / Automatic startup updates

- 44 项 JavaScript 检查通过：新增启动等待／取消、自动失败保留界面结果、只有完整发布后才刷新、自动任务不弹提示，以及离线／仅手动模式显示。
- 新增 C# 自动更新套件：以完整随包 856 条真实关系为输入，验证约 5 秒启动延迟、每次服务启动仅一次、离线禁用、手动覆盖、有限重试、等待更新锁时仍可查询、停止／退出取消、下载失败保留全部原数据、下次启动续传，以及只更新默认和已完整保存条件。
- 默认条件及一个模拟额外条件共 1,712 条记录的更新范围测试通过；额外条件是用于验证调度范围的合成输入，不是该条件的真实原站统计。模拟传输跳过取数限速，未访问外网。
- 模拟后台续传、校验和启用期间同时执行 1,036 次本地读取，均有正确完整结果，无意外回退；最慢 106.59 ms。阻塞下载期间核对全部 856 条真实关系，最慢 5.04 ms；失败后全部数据及来源日期保持不变。这些是本机 C# 本地读取计时，不含 HTTP 或浏览器渲染，也不是所有电脑的保证。
- 原有 C# 稳定性及空间管理套件通过：原站中途换版、损坏库回退、取消与连接释放、100 连续版本、25 条件独立保留、文件占用及个人文件／联接边界。
- 最终 EXE 的本机 HTTP 测试：普通服务（以 `--no-auto-update` 固定快照）和离线模式各验证全部 856 条，内容、来源、生成时间、过滤隔离及访问边界均正确。普通中位/P95/最大 4.42/7.85/16.29 ms；离线 4.45/7.54/16.18 ms。包含 HTTP、校验与序列化，不包含页面绘制。
- 实际普通启动最初返回 `scheduled`、`automatic=true`，无需点击即完成真实原站检查，显示“数据版本未变化”，未重复下载。浏览器确认版本 1.3.4、作者 FrancisChen、螃蟹别名、13 个严父／26 个大儿、原始生成时间及“此份随程序提供”；检查时间独立显示，页面无警告或错误。离线状态与禁用更新按钮也已确认。

All 44 JavaScript checks passed, including the startup delay/cancel state, retained results after automatic failure, refresh only after complete publication, quiet automatic jobs, and offline/manual-only states. The new C# suite uses all 856 real bundled records and covers one delayed job per launch, disabled modes, manual updates, bounded retries, nonblocking lock waits, cancellation/exit, complete-data retention after download failure, resumption and profile scope. A synthetic additional profile makes 1,712 records for scope testing; it does not claim to reproduce that filter's real statistics. Fixture transport skips rate limiting and makes no external requests.

During simulated resumption, validation and activation, all 1,036 concurrent local reads returned complete correct data without unexpected fallback; the maximum was 106.59 ms. With download transport blocked, all 856 real records remained readable, with a 5.04 ms maximum. Failure preserved data, provenance and timestamps. These are development-PC C# read timings, excluding HTTP and rendering, not universal guarantees. Existing stability and storage suites also passed, including source changes during downloads, fallback, cancellation, connection release, 100 successive editions, all 25 profiles, locked files, personal-file and junction boundaries.

Both normal service (`--no-auto-update` for deterministic snapshot comparison) and offline mode passed all 856 HTTP content/provenance, filter and access-boundary checks. Median/P95/maximum were 4.42/7.85/16.29 ms and 4.45/7.54/16.18 ms respectively, including local HTTP, validation and serialization but excluding rendering. Actual normal startup reported an automatic scheduled job, then checked the real source without a click and found an unchanged dataset, downloading nothing. Browser checks confirmed version, author, Urgot's nickname, 13/26 results, retained bundled timestamps, separate check time, quiet status and offline update controls, with no page warnings or errors.



以下为历史版本验证记录；旧版本的“手动更新”策略已由上述 1.3.4 行为替代。 / The following records describe earlier releases; their manual-only update policy is superseded by 1.3.4 above.

## 1.3.3 来源与时间 / Provenance and timestamps

- 40 项 JavaScript 检查通过。新增覆盖自带库不冒充本机下载、联网／备用两侧不同日期、回退自带库、旧版随包记录、缺失／无效时间、窗口保留结果恢复后标签复原，以及原站检查时间独立显示。
- C# 更新测试验证实际发布时间取自完整库清单，版本未变只刷新成功检查时间；失败、取消、损坏当前库、损坏清单及异常启用记录均保留对应备用数据的原日期。旧版缓存没有生成时间时不推造；旧版随包记录不使用打包者取数时间作为本机更新。
- 原有空间管理回归通过：连续 100 版最多保留两版，25 种条件独立保留，过期与占用处理、个人文件／目录联接保护及更新互斥锁均通过。
- 普通与离线模式各核对全部 856 份默认条件关系，返回内容、原站生成时间及自带来源标记正确，不触发联网更新。普通模式接口中位 5.00 ms、P95 8.90 ms、最大 14.14 ms；离线为 5.06／9.09／19.49 ms。这是本机接口观察值，不是所有电脑完整界面的性能保证。
- 实际浏览器检查版本 1.3.3、作者 FrancisChen、螃蟹别名、严父 13／大儿 26、两侧可见来源与时间及数据管理的独立检查时间；页面警告和错误为空。1034 份随包数据文件与 1.3.2 的哈希一致，没有重新采集或改写英雄关系。

All 40 JavaScript checks passed, including bundled attribution, independently dated downloaded/backup directions, fallback and legacy seeds, absent/invalid metadata, retained-result recovery, and separate upstream-check dates. C# update tests verify complete-library save timestamps, unchanged-version checks, and provenance across cancellation, corruption, malformed manifests and activation pointers. Legacy source dates are never fabricated, and publisher retrieval times are not attributed to the user.

The storage regression suite also passed: 100 successive editions, all 25 independently versioned profiles, expiry, locked-file recovery, personal files, junction boundaries and the update lock. Both normal and offline modes passed 856 real-record equality and provenance checks without implicit updates. API median/P95/max were 5.00/8.90/14.14 ms (normal) and 5.06/9.09/19.49 ms (offline), local observations rather than universal whole-interface guarantees. Browser checks confirmed the version, author, Urgot nickname, 13/26 results and visible timestamps without page warnings/errors. All 1,034 bundled data files remain byte-identical to 1.3.2.



## 1.3.2 空间管理 / Storage management

- 34 项 JavaScript 测试通过，包含空间数字展示、暂缓清理提示和既有查询重试行为。
- 独立临时目录模拟连续 100 个数据版本，始终最多保留当前与上一版；两版模拟库文件总量为 6,588–6,634 字节，未随更新次数增长。模拟记录用于验证保留算法，不能据此推断真实英雄库大小。
- 25 个筛选组合各自使用不同版本，均正确保留自己的两版；同时查询的最慢读取为 1 ms（小型模拟库、本机观察值）。
- 7 天下载过期、最近一份下载保留、已确认原站换版清理、过期版本观察不误删、占用文件下次恢复清理、损坏／异常指针保护、个人文件保护、目录联接和路径越界保护全部通过。
- 更新失败、取消和源站切版不替换可用数据；修复损坏当前库时保留上一份可用备份，并自动删除淘汰库。后台清理遵守更新互斥锁，跨窗口启用记录变化可被查询识别。
- 普通和离线启动分别验证全部 856 份随包关系，数据与源记录一致，不发起隐式更新。普通模式中位 4.67 ms、P95 7.98 ms、最大 14.55 ms；离线中位 4.73 ms、P95 8.05 ms、最大 17.78 ms。
- 实际界面检查 1.3.2、FrancisChen、“螃蟹”搜索、严父 13／大儿 26，以及自动空间管理面板；页面警告与错误日志为空。
- 新增自动整理没有获取新的原站数据。随包库与 1.3.1 相同；未下载完整 25 种组合，README 中全组合体积是明确标注假设的估算。

All 34 JavaScript checks and the C# storage and update-stability suites passed. One hundred successive synthetic editions stayed bounded to two retained editions (6,588–6,634 fixture bytes). All 25 independently versioned profiles retained their own two editions; concurrent synthetic queries peaked at 1 ms on the test PC. Tests covered expiry, resumption, confirmed source changes, stale observations, locked-file recovery, corrupt pointers/libraries, personal files, junctions, path boundaries and the shared update mutex. Repair updates preserve a usable previous edition and remove obsolete data. Local windows detect changed activation pointers.

Both launch modes passed all 856 real bundled-record equality checks with no implicit updates. Normal API median/P95/max: 4.67/7.98/14.55 ms; offline: 4.73/8.05/17.78 ms. These are local observations, not universal performance promises. Real UI checks confirmed version/author, Urgot's nickname and 13/26 results, storage labels, and no page warnings/errors. Bundled source data is unchanged; the all-25-profile storage estimate is a sizing model rather than an actual full-source download.


## 既有数据与查询验证记录 / Earlier data and query verification

## 数据 / Data

- 数据指纹 / Source SHA-256: `949ecef22fc8bffdffd7253c0c9e0ca94a3671b062040cc75677d9a63374ba36`
- 数据生成 / Source generation: `2026-10-10T22:10:41.972765+00:00`（北京时间 2026-10-11 06:10:41）。
- 默认条件 / Default profile: threshold 1, strictness 5.
- 173 英雄 / champions；428 有效英雄分路 / valid champion–lane pairs；856 独立双向记录 / independent directional records.
- 所有记录校验值与完整性通过；两种模式的完整接口内容逐条比对通过。JSON 数字负零按零比较，不影响数值。
- All hashes and query identities passed. Every full payload matched the saved source response; numeric negative zero is normalized to zero for JSON comparison.

## 功能与稳定性 / Behavior and stability

- 32 项搜索和界面状态测试全部通过。 / All 32 search and UI-state tests passed.
- 普通启动、离线启动均遍历 856 份查询；不启动隐式外网更新。 / Both normal and offline startup passed all 856 local queries without implicit network updates.
- 不同阈值、严谨度、分路、方向隔离；有效空结果与缺失数据分开提示。 / Exact profile and query isolation; valid empty results differ from missing data.
- 更新失败保留完整库；续传只补缺项；相同原站版本跳过重复请求。 / Failed updates preserve complete data; resumption fills only missing records; unchanged editions skip detail fetching.
- 下载前后原站版本不一致时拒绝发布；更新中查询保持快速。 / Mixed upstream editions are rejected; queries stay responsive during updates.
- 取消更新、损坏文件回退、旧版缓存兼容、离线禁用更新、HTTP 取消后释放连接通过。 / Cancellation, corruption fallback, legacy records, offline update rejection and transport cleanup passed.
- 本机来源、Host、请求方式、请求体与文件路径边界检查通过。 / Local-origin, host, method, body and file-boundary checks passed.
- 实际界面验证“螃蟹”搜索、缺失组合提示、离线更新按钮禁用、普通启动的联网版本检查；真实检查返回“已是原站当前版本”。 / UI checks covered Urgot's nickname, missing profiles, disabled offline updates and a successful real source-version check.

## 性能样本 / Performance sample

| 模式 / Mode | 查询数量 / Queries | 中位数 / Median | P95 | 最大值 / Maximum |
| --- | ---: | ---: | ---: | ---: |
| 离线 / Offline | 856 | 4.37 ms | 7.87 ms | 14.30 ms |
| 普通 / Normal, final candidate | 856 | 4.43 ms | 7.45 ms | 14.89 ms |

在开发电脑上用 4 个本地并发请求测量，包含本机 HTTP、校验和数据序列化，不包含浏览器绘制。不是所有电脑的性能保证。更新测试使用独立临时目录，未改动原用户缓存。

Measured on the development computer with four concurrent localhost requests, including local HTTP, validation and serialization, but excluding browser rendering. These are observations, not guarantees for every PC. Stability fixtures use isolated temporary directories and do not change the user's cache.

## 覆盖限制 / Coverage limit

随包预装 1 / 25 种条件。额外 24 种通过界面更新器下载；没有声称已预装全条件。后台批量算法经模拟源验证；发布时未进行 21,400 次真实全条件取数。

One of 25 filter profiles is preinstalled. The other 24 are available through the updater. Bulk update behavior is tested with simulated sources; release validation did not fetch all 21,400 real filter-specific records.


## 1.3.1 查询恢复 / Query recovery

- 短暂 503 故障：仅严父侧失败 2 次，自动第三次成功；大儿侧仅读取 1 次。界面约 1.05 秒自动恢复，原结果全程保留。
- 持续无响应：3 次读取后停止，浏览器实际验证约 4.61 秒；保留厄加特上路 13 个严父、26 个大儿，停止转圈。
- 连接失败：仅做一次共享服务检查，显示启动提示，保留相同条件旧结果；服务恢复后自动重查。另一个请求先恢复连接标记的竞态也有回归测试。
- 参数错误、损坏数据、格式不完整和未下载组合均不盲目重试。切换英雄、分路、筛选或退出会取消旧重试。
- 缺失 2 / 5 条件时没有显示 1 / 5 的旧结果；点击“恢复默认条件查看”才将滑块改回 1 / 5 并显示完整结果。
- 自动化遍历 173 位英雄的全部 428 个有效分路，各含严父／大儿，普通与离线均完成。两种模式共 856 个界面组合的数量与已校验源记录一致，失败 0，超过 5 秒 0。

| 界面结果就绪 / UI results ready | 英雄 / Champions | 分路组合 / Pairs | Median | P95 | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| 普通 / Normal | 173 | 428 | 33 ms | 46 ms | 282 ms |
| 离线 / Offline | 173 | 428 | 33 ms | 44 ms | 285 ms |

界面时间为本机自动输入或切换分路，至两侧数据、数量和完成状态可从页面确认的时间；包含自动化开销，不包含程序启动和人工打字，不是所有电脑的性能承诺。全部英雄的默认推荐分路均有关系可显示；部分其他分路的合法空结果按原站保留。

Temporary 503 errors recovered on the third read in approximately 1.05 seconds; only the failing column retried. Hung requests stopped after three attempts in approximately 4.61 seconds, with the exact cached 13/26 Urgot results still readable. Service failure was checked once per query, and reconnect recovered the selected query automatically, including the case where another request had already restored the connected badge. Invalid, damaged and missing data did not retry blindly. Changing selections cancelled obsolete retries. Restoring defaults explicitly changed the controls before showing default-profile data.

Both startup modes passed all 173 champions and 428 valid champion–lane pairs in the real browser UI, with both directional counts matching the previously verified records. No failure or over-five-second result occurred. Timings measure automatic entry/lane selection through observable page completion and include automation overhead; they exclude application launch and human typing and are not guarantees for all hardware. Every champion's default lane had relationships; valid empty results in other lanes were preserved.

32 JavaScript tests plus the C# stability suite passed. Fault injection ran only in an isolated local QA service; no fault-injection server or settings are included in the release. Existing offline files and personal cache are not modified by these fault simulations.



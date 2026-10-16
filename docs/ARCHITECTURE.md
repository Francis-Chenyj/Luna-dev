# Runtime ownership / 运行时状态归属

Applies to Luna 1.6.5. The existing .NET Framework build, local API and on-disk formats remain supported.

## 服务边界

`ApplicationRuntime` 是组装入口。每个实例通过构造函数接收独立目录、启动模式和可选网络传输，创建自己的服务。它聚合接口状态，不保存任务队列或数据索引。`RuntimeOptions` 只包含只读启动策略和路径。

| 服务 / 文件 | 状态与职责 |
| --- | --- |
| `LibraryStore` / `Library.cs` | 私有条件库索引、当前/回退版本、损坏标记、英雄双向缓存、最近查询、校验检查点。完整校验后原子发布；对外目录为独立副本。 |
| `QueryService` | 条件校验、本地结果选择和损坏回退提示。没有网络依赖。 |
| `DataUpdateCoordinator` / `DataUpdates.cs` | 整库与单英雄共用的一套队列、优先级、进度、取消和限速。调度锁属于该实例。 |
| `StorageMaintenance` / `Storage.cs` | 保留规则、整理调度、空间统计。通过仓库方法检查版本，通过程序升级服务清理其下载，不修改它们的字段。 |
| `AppUpdateService` / `AppUpdate.cs` | 程序版本检查、下载、准备状态和升级交接。每个任务捕获自己的取消源。 |
| `PreferenceStore`、`DiagnosticLog`、`QuarantineStore` | 各自拥有设置保存、诊断计数和隔离区清理；设置变化在持久化且释放设置互斥后通知应用。 |
| `BackgroundWork` | 登记后台线程，拒绝退出后的新任务，取消并等待已登记任务，最后释放网络资源。 |
| `DataSourceClient`、`ReleaseClient`、`QueryActivity` | 每实例网络传输及连接限制、发布源读取、最近查询活动。 |
| `DataFormat`、`LocalIO`、`Deployment` | 无会话任务状态的格式、路径及文件操作工具。序列化器按调用创建；固定分路列表不可修改。 |

`Program` 及桌面宿主仍承担进程入口、窗口和监听器的唯一实例。业务服务不访问 `Program`，构造和测试这些服务无需启动窗口或 HTTP。

## 并发与生命周期规则

1. 查询只读本地记录；对来源和条件的校验、损坏回退规则保持一致。索引只由仓库发布，调用方获得的目录修改不会影响后续查询。
2. 整库与单英雄调度由同一协调器拥有，保留任务去重、英雄优先级和整库结束时接续队列的行为。
3. 同一目录的更新和清理仍使用原有目录命名的跨进程互斥。不同目录拥有独立状态；同目录实例通过磁盘指针和原子英雄记录观察已发布数据。
4. 数据发布继续校验下载前后来源版本、完整清单及文件哈希。取消前已发布的完整数据保留，未完成下载不取代完整版本。
5. 退出先停止接收和关闭宿主，再取消后台工作。工作注册与停止决定由同一登记锁保护；取消回调在登记锁外执行。等待最多三秒，超时写入诊断，并让仍在退出的工作继续拥有资源，直至全部结束。随后进程退出仍由 Windows 回收资源。
6. 服务启动工作前检查退出状态。竞态中无法登记的工作清除忙碌或排队状态，不遗留永久等待的英雄任务。
7. 路径归属、链接检查、个人文件保护、升级事务的 committed 边界及回滚规则继续执行。重构不变更数据和设置格式。

## 验证入口

`tests/run-service-isolation.ps1` 在新目录构造真实服务实例，注入本地样本数据源，不使用反射或 `Program` 单例。覆盖目录副本、独立任务/取消/关闭、同目录写入交接、发布与查询/清理并发、离线重启、退出拒绝新任务、两类更新收尾和工作登记释放。

现有故障回归的普通操作已迁移为直接调用服务；`tests/TestApplication.cs` 集中保留旧夹具所需的私有字段故障注入和少量反射。该适配器只参与测试编译，不进入 Windows 运行包。后续新增测试应优先通过构造函数、正常操作和可控数据源准备场景，不扩展它。

`scripts/check.ps1 -Browser` 包括新隔离套件、原有故障回归、完整离线接口、两类压缩包检查和真实浏览器验收。原生窗口、实际历史包升级/回滚及其他系统环境仍按独立验收入口记录。

## English summary

Each `ApplicationRuntime` owns its services. Query code has no network dependency; the repository alone owns indexes and damage markers, and returns detached catalogs. One data coordinator retains both full-library and champion scheduling. Storage maintenance consumes repository operations and update cleanup methods rather than shared fields. Application-update state, transports, preferences, diagnostics and activity are per instance.

The original directory mutexes, atomic publication, ownership checks, data formats and rollback rules are retained. `BackgroundWork` registers threads before starting them, stops accepting work before cancellation, and releases network resources only after workers drain. Native/process host state remains in `Program`; domain services never reference it. Legacy private-state fixture injection is centralized in a test-only adapter; new isolation tests use typed services and injected transports.

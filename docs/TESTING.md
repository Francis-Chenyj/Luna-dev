# Fault regression maintenance / 故障回归维护

Applies to Luna 1.7.1. Test changes must preserve the failure being simulated and the observable recovery behavior.

## 场景迁移

| 原测试做法 | 现有准备与断言 | 主要套件 |
| --- | --- | --- |
| 按字段名改离线、仅手动模式 | 构造不可变 `RuntimeOptions`，创建新运行时，断言没有请求且本地查询可用 | Settings、AutoUpdate、Focus、HeroUpdate、Stability、Lifecycle |
| 清空索引、最近查询或升级状态，模拟重启 | 释放旧实例并从相同文件重新打开；独立优先级场景使用全新目录 | AutoUpdate、Focus、HeroUpdate、Lifecycle |
| 替换私有网络委托 | 构造时注入传输；故障行为由测试持有的样本源控制 | 所有更新回归 |
| 将下一次请求时间设成过去 | 注入 `TestClock`，执行正常限速调度并推进虚拟时间 | AutoUpdate、Focus、HeroUpdate、Stability |
| 改写整理忙碌标志或下次检查时间 | 正常申请整理；等待所属工作结束；推进时间检查 59/61 秒及 24 小时边界 | Storage |
| 读取连接信号量计数 | 取消真实回环 HTTP 下载后，同时发起三个请求，确认都能进入服务端并完成 | Stability |
| 读取取消源、强制改变程序更新状态 | 用事件停在实际下载或取消阶段；检查保存设置、暂停、手动下载结果与网络次数 | Lifecycle |
| 读取英雄任务字典数量 | 完成 28 个不同英雄任务，验证只保留最近 24 项状态，旧项返回空闲 | HeroUpdate |
| 私有方法反射检查标题栏配色 | 直接调用无副作用的内部配色函数，检查其类型明确的返回值 | Settings |
| 反射推算安装注册表键 | 按隔离安装目录找到实际注册信息并检查唯一性 | 历史 DeploymentIntegration 夹具 |

全部原有故障场景继续保留，包括双向数据原子发布、损坏回退、磁盘占用、源版本变化、续传、取消、升级事务和个人文件保护。历史安装夹具仍标明原适用版本，不用它冒充当前版本的历史升级验证。

## 时间与同步边界

- `IRuntimeClock` 是更新请求调度和存储维护的可选依赖；生产不传参数时使用真实时间。它不改变来源版本、原子发布、API 或文件格式。
- 批量样本测试可用 `TestClock` 加速 600 毫秒请求间隔；报出的查询耗时与样本更新耗时应标明环境，不能当成公网下载速度。
- `ServiceIsolationTests` 明确保留默认真实时钟，验证完整英雄更新的四次请求间隔和取消；另用事件阻塞时钟，验证限速等待被取消后没有发出后续请求。
- 存储测试正常释放并重新打开运行时，验证持久化的一天整理期限；互斥占用测试在一分钟之前不重试、之后恢复。
- 优先用事件等待“已经开始/已经进入取消”等阶段，不以随意睡眠模拟完成。最终断言前等待该实例登记的后台工作结束；先释放运行时，再释放样本源的事件。
- 网络重试、启动延迟和程序更新冷却保持真实等待及原有文件记录规则，避免为测试引入统一调度框架。

## 运行与新增规则

在源码根目录运行对应 `tests/run-*.ps1`，完整检查使用 `scripts/check.ps1 -Browser`。完整命令和依赖见 [开发指南](DEVELOPER_GUIDE.zh-CN.md)。测试在独立临时目录生成文件，不应接触开发者正在运行的缓存。

新增故障测试应先说明触发条件和预期保留的数据，再通过公开于程序集的服务操作、注入依赖或真实文件准备故障。不要增加按字符串读写私有字段的万能适配器，不为断言暴露可变队列、取消源或信号量。仓库测试会阻止常见的私有成员反射和已删除适配器重新进入运行脚本。

当前测试能证明本机受控场景的行为；其他 Windows 版本、ARM64、不同 DPI/显示器、干净机器和公网签名发布链仍须在对应环境验证。

## English summary

Legacy fault tests now instantiate and dispose real runtimes, inject network functions, prepare real disk faults and coordinate asynchronous work with events. A narrow clock dependency accelerates fixture request pacing and exercises maintenance deadlines; production defaults retain real time and cancellation. Assertions observe published data, job status, downloads and successful concurrent requests. Private-member reflection is prohibited by a repository regression. The historical deployment harness remains historical; current upgrades use the unmodified predecessor runtime with `run-upgrade-compatibility.ps1`.


## 1.7.0 regressions

BackendRegressionTests covers receipt-write failures in both update paths, full expired audits versus unfinished checkpoints, and case-alias writer exclusion. ClientDraftTests uses injected discovery/transport and synthetic responses; browser-draft exercises ten stable slots and click-only queries through sanitized route fixtures. Never use production credentials or account/session captures as test fixtures. The HTTP suite reads disabled state and rejects invalid connect requests without enabling a real client.

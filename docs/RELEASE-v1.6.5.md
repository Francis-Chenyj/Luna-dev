# Luna 1.6.5 — local review / 本地验收版

2026-10-16 · FrancisChen

- 将查询、数据更新、存储维护、程序升级及设置/诊断状态迁入独立服务实例；业务服务不再读取 `Program` 的全局任务字段。
- 保留整库与单英雄共同调度，以及现有磁盘格式、原子发布、跨进程互斥、校验和回退规则。
- 后台任务统一登记、取消和释放；退出拒绝新英雄任务，避免永久排队。返回的英雄目录与内部索引分离。
- 新增真实双实例和生命周期测试，普通故障回归调用迁移为类型明确的服务调用。

Queries, data updates, maintenance and application upgrades now have explicit instance ownership. Existing API and disk formats remain compatible. Worker shutdown and returned-catalog ownership have dedicated regressions.

See [architecture](ARCHITECTURE.md) and [verification scope](QA-1.6.5.md). This is a local build; it does not imply GitHub publication, publisher signing or completion of third-party distribution evidence.

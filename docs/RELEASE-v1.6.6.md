# Luna 1.6.6 — local review / 本地验收版

2026-10-17 · FrancisChen

- 删除旧故障测试的私有字段反射适配器；启动模式、重启、取消和恢复通过真实服务生命周期验证。
- 用构造注入的数据源、事件和真实文件准备故障，保留双向原子发布、损坏回退、续传及个人文件保护检查。
- 为请求调度和存储维护提供小范围的时钟依赖；默认仍执行真实等待，增加生产限速、取消、一分钟重试及重启后一天整理期限验证。
- 增加防止反射适配回流的仓库检查，以及场景迁移和新增测试规则文档。

Legacy fault regressions now exercise typed services, real restarts and observable results. Production defaults retain existing request pacing, local API and disk formats. This completes the private-state test-adapter debt identified during the 1.6.5 service refactor.

See [architecture](ARCHITECTURE.md), [test maintenance](TESTING.md) and [verification scope](QA-1.6.6.md). This is a local build; it does not imply GitHub publication, publisher signing or completion of third-party distribution evidence.

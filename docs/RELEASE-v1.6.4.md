# Luna 1.6.4 — local review / 本地审阅

This build continues the local audit. It has not been published to GitHub; distribution permissions and publisher signing remain pending.

本版继续修复本地审查中发现的故障，未发布到 GitHub。第三方分发证据及发行者签名状态见 [release-status.json](../release-status.json)。

- An upgrade that passed startup validation stays committed if cache cleanup or result-file writing fails. The transaction journal can supply the installed receipt.
- Interrupted, damaged or unrecognized rollback directories are preserved before a verified replacement backup is created. Fully owned old backups are reclaimed after success.
- Corrupted downloaded archives are retained as evidence and downloaded again. Corrupted extracted files or manifests can be rebuilt from the authenticated archive. Installation validates them before handing off, leaves the app open on failure, and restores the retry action.
- A page whose JavaScript did not initialize uses the native close dialog. Health probes bind an ephemeral port directly and verify their own process/address record.
- Loopback header parsing and disconnect cancellation now have an independent module. Ambiguous/invalid headers are rejected; the header has a 10-second total deadline and a 16 KiB limit.
- Concurrent file readers allow atomic replacement while retaining their original snapshot, preventing a settings/status read from intermittently blocking data repair.
- Browser regression is runnable locally and included in the prepared Windows workflow, with locked development dependencies, isolated runtime/profile, failure screenshots and trace. It checks the new-user flow, keyboard/focus, refresh/history, offline recovery and 36 layouts.

升级清理失败不会再误报回滚；旧备份异常、下载文件损坏可保留证据后重试。窗口脚本未启动时可使用原生关闭选项。本地请求增加严格校验与总超时。真实浏览器测试接入统一检查入口，默认离线构建仍不依赖 npm。

Files retained for inspection use `cache/app-update/rollback-preserved-*`, `stage-preserved-*` and `package-rejected-*`. Unknown or changed files are not automatically erased. Normal settings and matchup data are retained.

See [QA scope](QA-1.6.4.md) and [developer instructions](DEVELOPER_GUIDE.md). Packages are `Luna-v1.6.4-Windows.zip` and `Luna-v1.6.4-Source.zip`, with SHA-256 and source provenance.

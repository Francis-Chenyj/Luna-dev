# Luna 1.6.3 — local audit repairs / 本地审计修复

2026-10-16 · FrancisChen · Local review, not a GitHub release.

- F02: Preserve malformed update records. Continue only with a verified installed manifest, or recover a verified backup; otherwise retain evidence and stop with recovery guidance.
- F03/F04: Embed the maintenance runtime target/manifest, copy the verified worker configuration, and use a shared file-I/O adapter for deep paths without requiring a system policy change. Test actual paths beyond 260 characters and the real helper executable.
- F01: Persist all eight scope values in the query URL. Restore reload/history/links and avoid fetching again for scope-only changes.
- F05/F06: Correct the public-download status, module/build documentation, English singular counts and close-dialog accessible label.
- U01/U02: Offer exact champion/profile downloads in missing-data states; explain storage measurement waiting behind a data operation while retaining prior totals.
- R02/R03/R05: Include a per-file source identity in both archives, require native WebView2/application readiness during upgrade health checks, and add runtime smoke tests, contributor guidance and real UI acceptance scenarios.
- R01/R04: Add an explicit third-party evidence inventory and publication validation. Permissions, publisher identity/certificate, formal GitHub publication and a real public-channel upgrade remain external work. The application and helper are not claimed to have Authenticode signatures.

既有用户数据格式保持兼容。损坏事务的原始记录保存在 `cache/app-update/transaction.invalid-<sha256>.json`。无法校验当前程序或备份时，保留原目录，在新目录解压完整包后迁移数据；不要删除现场来强行启动。

Local verification is recorded in [QA-1.6.3.md](QA-1.6.3.md). Public distribution status remains separate in [release-status.json](../release-status.json).

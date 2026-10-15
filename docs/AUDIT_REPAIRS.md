# 1.6.3 operation and verification / 操作与验证补充

## User behavior / 用户操作

查看范围的八个选项随页面地址保存，刷新、前进/后退和复制地址均能恢复。仅改变范围在已读取结果中筛选，不额外请求关系数据。恢复默认会回到阈值 1、严谨度 5、全部七类分段。

切换到未保存条件时，筛选区域显示覆盖情况，结果区提供“下载此英雄的当前条件”。它复用单英雄任务队列，只下载当前英雄、分路和条件的双向结果。两侧按钮共用任务；离线模式明确提示恢复默认或普通启动后下载。

空间统计与数据维护共用锁时显示等待原因；已有统计继续显示，操作结束后重新统计。统计是最近一次检查结果，不保证实时等于磁盘占用。

发现损坏的升级记录时先校验程序清单。当前程序完整则保留坏记录并继续；当前程序不完整但备份可验证时恢复备份；两者均不完整则停止，保留数据并提示在新目录重新解压。设置的“应用”页说明已完成的恢复。不要手工删除事务文件来绕过检查。

Scope is preserved in the URL. Missing-profile actions fetch only the selected champion/lane/profile pair and remain disabled offline. Storage explains waiting behind a data operation and retains previous measurements. A malformed update journal is preserved; only a verified installation or verified backup can resume use. Neither a missing journal nor a guessed state is treated as evidence of a successful update.

## Build provenance / 构建身份

Both packages include `docs/build-provenance.json`. `sourceId` hashes the sorted source file path/hash records, including source files that were untracked before repair. `baseCommit` is present when built from a Git checkout; it alone does not identify uncommitted changes. The generated provenance file excludes itself. `release-files.json`, `installed-files.json` and `SHA256SUMS.txt` bind the shipped artifacts. This is source traceability, not a claim of bit-for-bit compiler determinism or an Authenticode identity.

## Tests / 测试

Run `scripts/check.ps1 -OutputDirectory .\dist\checks-<new-id>`. The suite builds an isolated source snapshot, runs JavaScript/C# and HTTP regressions, validates both archives, tests a real malformed-journal startup, verifies the actual helper against deep paths, and rebuilds the extracted Source ZIP.

With WebView2 Runtime and an interactive Windows desktop, additionally run:

```powershell
.\tests\run-runtime-smoke.ps1 -RuntimeDirectory <extracted-runtime> -OutputDirectory <new-isolated-output> -Desktop
```

This confirms native readiness and rejects an intentionally broken JavaScript app. It does not publish or exercise GitHub/CDN delivery. Follow [UI_ACCEPTANCE.md](UI_ACCEPTANCE.md) for real keyboard, layout and tray acceptance.

`tests/run-upgrade-compatibility.ps1 -PreviousRuntimeDirectory <actual-old-runtime> -ReleaseDirectory <new-package-directory> -OutputDirectory <new-output>` exercises an unmodified historical runtime, current maintenance code, the new native health check and byte-identical historical executable rollback. It generates an ephemeral test signature in memory; it does not read publisher private keys or claim to test historical updater handoff/GitHub delivery.

## Publication / 发布边界

`scripts/check-release-readiness.ps1` validates the evidence inventory without claiming authorization. `-ForPublication -RuntimeDirectory <runtime>` additionally requires completed evidence review and valid Authenticode signatures. Sign before generating ownership/checksum manifests, then create the RSA update envelope. A certificate or permission record cannot be manufactured by changing a boolean. Private keys must remain outside source, reports and packages.

When a real code-signing certificate is available in the current user's certificate store, use `scripts/package.ps1 -OutputDirectory <new-directory> -SigningCertificateThumbprint <thumbprint> -TimestampServer <https-timestamp-url>`. The script signs both freshly built executables before producing inventories and hashes. It does not export the private key or create a substitute certificate. Omitting these options produces an unsigned local review package.

Long-path behavior follows Microsoft's [path normalization documentation](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/mitigation-path-normalization); Luna applies extended paths only at internal filesystem calls, retaining normal paths for ownership and reparse checks.

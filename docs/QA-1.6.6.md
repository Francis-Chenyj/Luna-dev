# Luna 1.6.6 verification / 验证记录

Date: 2026-10-17. Local Windows 11, installed Edge/WebView2 and Node.js 24.19.0.

Evidence directory: `D:\ChatGPT\Luna\2026-10-17-test-migration`. Baseline source: `6f9a0186c5bf7f8197d53e179303c4d8ddaa9f2b` (1.6.5).

## Scope / 本次范围

The legacy private-member reflection adapter has been removed. Settings, automatic updates, focused updates, hero updates, stability, storage and lifecycle regressions now use real service instances and typed operations. Long-path and session harnesses no longer carry an unused runtime adapter. The historical deployment harness looks up the actual installation registration by fixture directory instead of reflecting its private key-name helper.

Targeted settings, stability, storage, automatic-update, focused-update, hero-update and lifecycle runs passed after migration. The ten service-isolation scenarios passed, including two additional checks for production request spacing/clock cancellation and cancellation while waiting for a request slot. Storage coverage additionally verifies the saved 24-hour maintenance deadline across an actual runtime restart. Repository checks prohibit reintroducing member-name reflection and compiling the retired adapter.

The original 62 named fault scenario groups are retained. Their mapping and the limited clock scope are documented in [TESTING.md](TESTING.md). A real loopback test verifies that three simultaneous requests can still proceed after cancellation; it no longer infers resource recovery from a private semaphore count. Upgrade tests use actual cancellation/download events, saved signed offers and install receipts instead of assigning updater state fields. Signing in fixtures uses disposable test keys, never publisher private keys. The short-session runner reads the runtime manifest version instead of asserting the obsolete 1.6.0 literal, so it can exercise the current migrated harness.

## Reproducible evidence / 可复查证据

The final clean-checkout `scripts/check.ps1 -Browser` results, exact-package browser/native checks and predecessor upgrade/rollback verification are recorded in the delivered `verification-summary.json` and HTML report. These records identify the tested source commit, package hashes, logs and completed checks. This source note does not claim that a hosted GitHub workflow or public-channel update was executed.

Production defaults still use UTC and real cancellable request waits. Fixture clocks accelerate only request scheduling and storage time decisions; fixture update timings do not measure internet download performance. API, settings and data formats remain compatible.

## Remaining environment requirements / 外部验证条件

Windows 10, ARM64, clean-machine prerequisites, non-default Windows DPI and multiple monitors require those environments. Publisher signing, third-party evidence and an actual public-channel upgrade require separate maintainer inputs and authorization. Browser viewport tests do not establish native DPI coverage. The historical 1.5.2 → 1.6.0 harness remains historical; current predecessor testing uses a complete unmodified 1.6.5 package.

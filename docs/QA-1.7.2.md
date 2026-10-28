# Luna 1.7.2 verification / 验证范围

Date / 日期: 2026-10-29. This revision packages help, manifest, version and documentation corrections; earlier version records retain their original dates and scope.

## Checks for this revision

The complete local Windows workflow is `scripts/check.ps1 -Browser`: 23 base groups plus one browser group. It runs 126 JavaScript tests and 14 C# regression groups, compares all 856 bundled default-profile records through the offline HTTP service, builds and verifies both packages, checks recovery and rebuilds the extracted source package. Existing version assertions cover the executable, settings, Windows manifest, main header, About, Chinese/English help, release metadata and development package metadata.

Browser coverage comprises 36 results-page layouts, 21 BP-board layouts and 52 heading-focus layouts, plus actual search, settings, keyboard navigation, BP opt-in and focus-return interactions. It checks unexpected external requests and browser errors. The bilingual README screenshots use an isolated offline 1.7.2 release candidate; its interface files must match the final runtime package, and both executables must report 1.7.2.

Additional release checks run `tests/run-runtime-smoke.ps1 -Desktop` against the final runtime and `tests/run-upgrade-compatibility.ps1` with the original published 1.7.1 runtime. The latter verifies an upgrade and failure recovery using local files and a temporary test signature, without the publisher's private key. This is not a public stable-channel update test or an Authenticode signature.

Before upload, verify the exact ZIPs, per-file inventory, source identity and absence of standalone acceptance tools, personal reports, caches, recordings and private keys. After upload, compare GitHub asset hashes and the tag's source commit, and inspect that commit's Windows CI result and the current bilingual README images.

Actual results, environment, archive hashes and predecessor identity belong in the external delivery record beside the release artifacts. This document defines scope and does not itself assert that a listed check passed. The published commit's [CI run](https://github.com/Francis-Chenyj/Luna-dev/actions) provides the hosted automated result.

## Limits and remaining requirements

Browser viewport, forced-color and equivalent zoom checks do not establish native Windows DPI or multi-monitor behavior on other hardware. Local package upgrade tests do not establish public-channel signing or third-party permissions.

The earlier real-client session was training/custom mode. It populated allied slots and completed local queries but did not establish full ten-player picks, enemy visibility, actual swaps or end-to-end live selection latency. Those scenarios still need a suitable real client session. Windows/ARM64/clean-machine variants, regional clients and upstream data accuracy require their corresponding environments or maintainer evidence.

独立验收工具、源码及个人报告保留在项目之外；本版不会把历史训练／自定义模式记录描述成完整十人对局验证。第三方材料、Riot 注册与发布签名待办继续保留。

See [release notes](RELEASE-v1.7.2.md), [UI acceptance](UI_ACCEPTANCE.md), [test maintenance](TESTING.md) and [BP privacy/compatibility](CLIENT_DRAFT.md).

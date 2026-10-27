# Luna 1.7.1 verification / 验证范围

Date / 日期: 2026-10-27. This patch versions the BP heading-focus repair separately from 1.7.0. Historical checks and personal 1.7.0 session reports retain their original identities.

## Checks for this revision

The complete local Windows workflow is `scripts/check.ps1 -Browser`. It builds an isolated source snapshot, runs JavaScript and backend regressions, exercises the offline HTTP service, creates and verifies both packages, checks the packaged runtime and rebuilds the extracted source package. Browser coverage includes 52 heading-focus layouts, 21 BP-board layouts and 36 results-page layouts. It also checks queries, opt-in BP, keyboard navigation, focus return, settings persistence and the absence of unexpected external requests.

Additional release checks use `tests/run-runtime-smoke.ps1 -Desktop` and `tests/run-upgrade-compatibility.ps1` against the exact final runtime package and an original 1.7.0 predecessor. Upgrade testing uses local files and a temporary test signature; it does not establish a public GitHub update or a publisher signature. Package inventory and checksum checks must explicitly confirm that the separate BP acceptance tool and personal reports are absent.

The external delivery record supplies actual pass/fail results, package SHA-256 values, source identity, environment and predecessor identity. It is stored outside the repository with the release artifacts. A list of checks here is not itself a passing test result; do not silently substitute a package after its checks.

## Scope and remaining requirements

The heading repair is exercised through actual pointer and keyboard interactions in the browser, including Diana, Aurelion Sol, Chinese/English, light/dark, narrow widths, 200% equivalent CSS reflow and forced colors. The old style reproduced a title/outline overlap before repair. Browser emulation is not a native Windows DPI or multiple-monitor check.

The earlier user session was identified as training/custom mode. It populated allied slots and completed local queries but did not establish complete ten-player picks, enemy visibility, actual swaps or full game-action-to-Luna latency. Those real-client scenarios remain to be recorded separately for the repaired version. Other Windows versions, ARM64, clean devices, regional clients, upstream data accuracy and public-channel updates require their corresponding environments or maintainer inputs.

独立验收工具、其源码、浏览器配置、个人报告和演示素材不进入 GitHub 项目或任何发布包。1.7.1 通过 GitHub 开发预发布渠道分发，只包含 Luna 产品文件、普通项目测试及文档；既有第三方分发与发布签名要求不变。

See [release notes](RELEASE-v1.7.1.md), [UI acceptance](UI_ACCEPTANCE.md), [test maintenance](TESTING.md) and [BP privacy/compatibility](CLIENT_DRAFT.md).

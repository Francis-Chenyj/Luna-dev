# Luna 1.6.5 verification / 验证记录

Date: 2026-10-16. Local Windows 11, installed Edge/WebView2 and Node.js 24.19.0.

Evidence directory: `D:\ChatGPT\Luna\2026-10-16-refactor-03`. Baseline source: `3adb369b88677435c5bcde6ae2f4edd5dfb0e8d5` (1.6.4).

Targeted settings, stability and lifecycle regressions passed after service extraction. New tests first reproduced internal catalog aliasing and acceptance of a permanently queued hero job after shutdown. Both failed before their boundary fixes and passed afterward.

The seven new instance-service scenarios passed: detached snapshots; independent updates/cancellation/disposal; shared-directory writer handoff; publication during query and maintenance plus offline restart; stopped-runtime rejection; draining both updater types; and registered-worker cleanup. These tests create services directly without reflection or external network access.

The final clean-checkout suite, exact release packages, browser results, native smoke and historical upgrade/rollback verification are recorded in the delivered report after execution. Source metadata and package hashes identify the verified build. A prepared workflow is not evidence of a hosted GitHub run.

Limits remain: browser CSS widths do not validate Windows DPI/multiple monitors; Windows 10, ARM64 and clean-machine prerequisites require their environments. Signing, third-party evidence, GitHub publication and public-channel historical updater handoff require separate inputs and authorization.

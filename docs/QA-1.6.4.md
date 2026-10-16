# Luna 1.6.4 verification / 验证记录

Date: 2026-10-16. Local Windows 11 (NT 10.0.26300.0), Node 24.19.0, installed Edge/WebView2. No GitHub execution or release is implied.

Evidence: `D:\ChatGPT\Luna\2026-10-16-repair-02`. Historical 1.6.3 reports remain unchanged.

Confirmed regressions failed before their fixes: five maintenance faults (locked cleanup, locked receipt, incomplete/damaged/personal-file backup), damaged extracted upgrade files, malformed HTTP headers and a trickle request that exceeded 13 seconds. The actual old native window loaded HTML without app initialization and ignored Alt+F4; the corrected window displayed its native close choices.

Targeted checks have passed: lifecycle/receipt/retry recovery, settings close policy, 856 exact offline queries, 11 protocol/deadline scenarios, and a fresh-profile browser flow with 36 language/theme/width combinations. Browser requests stayed on the isolated loopback origin, with no JavaScript errors.

The initial full run exposed an intermittent file-sharing failure while repairing data under concurrent reads. A deterministic regression reproduced replacement failure with an open reader; the file adapter now shares deletion/rename access, preserving each reader's original snapshot. The failed run is retained as evidence, followed by the corrected verification runs.

The full suite, final package native smoke, upgrade/rollback compatibility and clean-checkout verification are recorded in the final local repair report after execution. `scripts/check.ps1 -Browser` includes browser regression; without `-Browser`, the offline baseline remains available without npm dependencies.

Limits: browser widths are CSS viewport tests, not Windows display-scaling tests. A native health probe does not by itself test tray gestures. Another Windows version/architecture, clean-machine prerequisites, publisher-certificate signing, third-party permission evidence, hosted GitHub CI and real public-channel updating still require separate evidence. The HTTP module extraction reduces shared state; the application still contains static coordination across storage/update/query services.

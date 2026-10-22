# Luna 1.7.0 verification / 验证记录

Dates: 2026-10-22–23. Windows 11 x64, .NET Framework, installed Edge/WebView2, Node.js 24.19.0. Baseline source: `c61ddd38aafd684e4718d92d412fb55593d258a6` (1.6.6).

Evidence directory: `D:\ChatGPT\Luna\2026-10-22-bp-review`. Ten numbered rounds retain individual reports, changes, commands and results. The delivered verification summary identifies source revisions, exact package hashes and whether each check passed. A successful baseline did not imply absence of defects: additional fault injection reproduced maintenance-finalization, expired-audit and case-alias-lock faults, while UI probes reproduced keyboard, debounce and stale-filter-label faults.

## Scope

Each complete round reruns the Windows checks against an isolated source/runtime snapshot. Focused checks exercise the round's particular failure or boundary. Synthetic BP fixtures cover ten slots, parallel picks, invisible enemy intents, swaps, missing/malformed fields, cancellation, stale replies and privacy projection. Browser testing uses sanitized route fixtures; it does not accidentally connect to an installed League client. The protocol suite verifies that reading BP state remains disabled and that mutation routes reject incorrect methods, origins and request bodies.

Real-client checks are separately recorded in the delivery report with the observed mode and number of populated slots. A single-player training room does not validate all ten players, enemy visibility, real swaps or multiple clients. The report does not present synthetic results as real matches.

## Remaining environment requirements

Windows 10, ARM64, clean machines, native DPI/multiple monitors and additional regional League versions require those environments. Public-channel installation/update, publisher signatures and third-party/Riot review require maintainer inputs and authorization. Browser widths and fixture clients are not substitutes. Existing user data remains outside the isolated test copies.

See [TESTING.md](TESTING.md), [BP contract](CLIENT_DRAFT.md) and [release draft](RELEASE-v1.7.0.md).

## Actual client boundary

On 2026-10-23 the user opened the Tencent client. WMI exposed the process but omitted its command line and executable path; limited native process queries recovered the required local connection information without elevating Luna. A real production-runtime/browser check connected, disconnected and reconnected successfully while local queries continued. No champion-selection session was entered: the user reported excessive client latency, and the training entry displayed a failure to create the custom game. Thus actual ten-player picks, enemy visibility timing and swaps remain unverified. Only Luna's projected states and screenshots were retained; account/client screenshots were not exported.

A baseline-source round failed when the test harness still treated 1.7.0 as a future update; upgrade fixtures now derive versions from the application. Another round exposed browser-runner/source-snapshot drift during concurrent development; the complete check now runs browser scripts from that round's own snapshot. Original failures and corrected reruns remain in the delivery report.

After comparison with pinned Akari source, native discovery became the first path and session publication gained a second gameflow check. The final service passed eight focused test groups and eleven independent concurrency/TLS boundary groups. A fresh real-client probe reached the ready state in 128 ms and stayed connected for ten seconds with zero populated champion slots; this is a single observed connection time, not a performance guarantee. Six 30-second isolated native sessions also passed, with 1,129 queries, no repeated downloads across the controlled resume sequence, and all six Luna main processes exiting within the deadline. The sessions did not separately count WebView2 descendants and do not represent cold reboots, real upstream networking or live BP gameplay.

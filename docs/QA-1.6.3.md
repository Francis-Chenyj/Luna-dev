# Luna 1.6.3 verification / 验证记录

Date: 2026-10-16. Local Windows verification; no GitHub release or cloud CI result is implied.

Verified locally on Windows 11 (NT 10.0.26300.0), Node 24.19.0 and the installed WebView2 Runtime. Evidence is retained in `D:\ChatGPT\Luna\2026-10-16-repair-01`; it is not shipped as user data in either package.

| Check | Observed result |
| --- | --- |
| Full isolated suite | 18/18 passed from a source path containing Chinese characters and spaces, with a nested output directory. Includes build, 102 JavaScript tests, C# regressions, HTTP checks, runtime smoke, package integrity and rebuilding the extracted Source ZIP. |
| Full bundled offline query coverage | 856 directional queries matched the bundled source exactly. Packaged HTTP median 4 ms, P95 6.99 ms, maximum 17.41 ms in this local run; these are server timings, not UI-paint or Internet benchmarks. |
| Damaged update journals | Empty/truncated/null/unknown records and valid interrupted transactions tested. A real packaged process started and answered an offline query after preserving a malformed journal and verifying its installed files. Invalid installation plus invalid backup stops safely. |
| Long paths | 280-character final files and 286-character temporary files passed; the actual maintenance EXE verified a 277-character owned path. Keep-data uninstall retained settings and an unknown personal file. Machine-wide long-path policy was not changed. |
| Native update health | WebView2 initialization, application JavaScript handshake and offline query passed. Deliberately broken app.js failed the health check. |
| Historical runtime | Unmodified local 1.6.2 package upgraded to 1.6.3 using current maintenance code. Forced failed health restored the byte-identical old executable, which answered an offline query; settings were retained. Uses a local archive and an ephemeral test signature. |
| Real browser regression | `tests/browser-smoke.mjs` passed 17 assertions: eight scope reloads, back/forward, translated close label, keyboard tabs, local license document, Escape focus restoration, counts and actual layout. |
| Responsive layout | 36 real-browser combinations passed: Chinese/English × light/dark × 360, 640, 720, 721, 800, 900, 1000, 1001 and 1440 CSS pixels. No measured card overlap, horizontal overflow or collapsed filter controls. |
| New-user flow | Chinese/English search, unknown/empty input, unavailable-lane choices, defaults, missing-profile action and local documents exercised. Lee Sin Jungle 2/5 downloaded through the inline action and displayed 21/37 results; counts are observations of that data snapshot. |
| Desktop lifecycle | Ctrl+K, close/cancel, English accessible name “Cancel”, minimize to tray and reopen the same query/window passed. Explicit Exit removed the test window. |
| Storage and offline restart | Download waiting reason was visible; completed measurement appeared (45.60 MiB in the test copy). Chinese/dark preferences survived restart. Offline missing conditions expose no download action and defaults restore results. |

Final follow-up: offline coverage text was clarified to avoid suggesting an unavailable download; the 102 JavaScript tests and actual offline UI were rerun successfully. The optional Authenticode packaging path was checked for invalid input; a successful certificate-backed signing run remains pending because no publisher certificate was supplied. Final package/source identities and subsequent clean-checkout results are recorded in the repair report.

Boundaries: third-party evidence remains pending; Windows Authenticode signing is not completed. Windows 10, ARM64, clean-machine prerequisites, historical-updater handoff and a GitHub/CDN production update require separate verification. The real-browser regression is separate from the default Windows CI job. Extracting common filesystem access reduces one source of duplication; broader service-boundary refactoring remains ongoing maintenance. UI state mocks alone do not prove layout, native focus or tray behavior.

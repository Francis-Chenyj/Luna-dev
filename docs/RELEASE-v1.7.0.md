# Luna 1.7.0 — local release draft

Optional BP champion synchronization displays five stable slots per team and opens local matchup details when a champion is selected. It uses only the local gameflow and champion-selection endpoints, discards unrelated response fields and leaves automatic picks, bans and player history outside the feature.

Existing faults repaired: stuck data jobs after maintenance I/O failure; completed audits failing to restart after expiry; directory-case aliases bypassing writer locks; search keyboard/debounce behavior; stale filter completeness labels; and an outdated fallback version label.

The October 27 heading-focus repair is now versioned separately as [1.7.1](RELEASE-v1.7.1.md), so repaired builds can be distinguished from the original 1.7.0 packages.

Validation includes backend fault injection, client projection and lifecycle tests, HTTP boundary checks, real-browser UI/layout checks, package integrity and upgrade/rollback checks. The [QA record](QA-1.7.0.md) distinguishes completed checks from real-client or external-environment requirements. Read the [BP privacy and compatibility contract](CLIENT_DRAFT.md) before enabling or distributing the feature.

Runtime and source ZIPs are local review artifacts. This work does not publish a GitHub release. `publicReleaseReady` remains false pending the existing third-party evidence, Riot review and publisher-signing work.

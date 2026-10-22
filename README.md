<p align="center">
  <img src="web/logo.svg" width="96" height="96" alt="Luna — a black L on a green background">
</p>

# Luna

**English** · [简体中文](README.zh-CN.md)

**v1.7.0** · Windows 10 / 11 · .NET Framework 4.7.2+ · [MIT — original code](LICENSE)

A local League of Legends matchup explorer for champion selection. Search for a champion to see **opponents that counter you** and **opponents you counter**, with lane and rank filters.

**Project home:** [Luna on GitHub](https://github.com/Francis-Chenyj/Luna-dev).

Switch instantly between English and Simplified Chinese in **Settings → General**; light, dark and system themes are available. The README, user guide and developer guide each have English and Simplified Chinese editions.

## Features

| Feature | What it does |
| --- | --- |
| Local queries | Reads the bundled default-profile library covering 173 champions and 428 supported champion–lane combinations. |
| Flexible search | Accepts Chinese and English names, titles, pinyin and curated nicknames. |
| Two-way results | Filters both matchup directions by lane, rank category, relationship threshold and sample strictness. |
| Background updates | Refreshes current and recent champions first; library downloads support resumption and fallback to saved data. |
| Settings and support | Persistent language/theme choices, startup data-update toggle, local folders and privacy-limited diagnostics. |
| Visible sources | Shows bundled, downloaded or fallback data, with source-generation and local-save times. |

Matchup results come from [MalzaharAI](https://www.malzaharai.top/counter). Relationship values are **not win rates**, and source timestamps are not game-patch dates.

## Preview

![Luna 1.5.0 showing Aatrox top-lane matchups in offline mode](docs/images/luna-matchups.png)

Luna 1.5.0 in English with the light theme, using bundled data: Aatrox, top lane, threshold 1, strictness 5, with the **全分段 (All ranks)** category selected. This is an interface example, not a live recommendation. [Screenshot details](docs/images/README.md).

## Download and quick start

**Local 1.7.0 revision:** removes the private-state reflection adapter from legacy fault tests. Real runtime restarts, controlled transports and events now verify cancellation and recovery; additional checks cover request pacing and maintenance deadlines. Query/update behavior and disk formats remain compatible. This build has not been uploaded to GitHub. See the [release notes](docs/RELEASE-v1.7.0.md), [verification scope](docs/QA-1.7.0.md) and [test maintenance guide](docs/TESTING.md).

**In development — local review.** This delivery provides `Luna-v1.7.0-Windows.zip`; `Luna-v1.7.0-Source.zip` contains source without an executable. The [Releases page](https://github.com/Francis-Chenyj/Luna-dev/releases) is a separate public distribution channel; local files do not imply a new online release. Use the accompanying `SHA256SUMS.txt` to verify the archives. Third-party distribution permissions and Riot registration remain open; see [release status](release-status.json) and [third-party notices](THIRD_PARTY_NOTICES.md).

**Requirements:** Windows 10 / 11, .NET Framework 4.7.2+ and Microsoft Edge WebView2 Runtime. Luna reuses the installed runtime; its ZIP does not bundle a complete browser. If missing, install the [Evergreen Runtime from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). `Luna.exe --browser` opens compatibility mode without tray support. Normal use needs no Node.js, Python, account, API key or administrator privileges.

After downloading the Windows package:

1. Extract the complete ZIP to a writable folder, such as `D:\Apps\Luna`.
2. Run **`Luna.exe`** or **`启动Luna.cmd`**.
3. Search for a champion, choose a lane and read the two matchup lists.

For use without external updates, run **`离线启动Luna.cmd`**. Installation, settings/data migration, shortcuts and troubleshooting are covered in the [user guide](docs/USER_GUIDE.md). Updating matchup data does not upgrade the Luna application.

## Closing Luna

The title-bar **×** and **Alt+F4** initially ask whether to **minimize to the system tray** or **exit the app**. Remembering your choice is optional. Change it later in **Settings → General → When closing the window**.

In the tray, already-started updates continue. Double-click the green **L** icon, choose **Open Luna**, or launch the same shortcut again to restore the existing window and query. Choose **Exit** from the page or tray menu to stop Luna and background updates immediately. Saved downloads remain available on the next launch.

When upgrading from an older version for the first time, fully exit Luna and extract the complete new runtime while retaining `cache`, or copy the old `cache` into the newly extracted runtime folder. Language, theme and downloaded data are retained. Old Edge-window recent queries are not automatically migrated; the new window stores them in `cache/webview2`. Browser compatibility mode keeps its previous idle-exit behavior.

## Startup and storage

Startup reads library manifests without hashing every saved profile and backup. Each query still validates its record and tries existing fallback data if damaged. A throttled background audit persists progress between launches. Local queries never wait for external requests.

**Settings → Data → Storage** shows core program and bundled data, downloaded libraries, champion updates, browser storage, app-update staging/rollback, logs and other runtime files. Measurements are timestamped and exclude source code, extra files outside cache and shared system runtimes. Disk usage is not process memory usage.

- Valid individual champion caches retain at most 64 pairs and 8 MiB. Each complete profile retains its current version and one usable backup.
- Confirmed invalid champion records and damaged preference backups are quarantined, limited to 20 files, 10 MiB total and 7 days.
- Incomplete dataset downloads and app-update staging may be cleaned after 7 days without progress. One previous app version is kept for rollback.
- Disposable browser caches are cleared daily while idle, retaining recent queries and settings. Manual maintenance does not wipe usable offline libraries.
- Locked files, unknown ownership, damaged activation records or potentially unique recovery data are retained conservatively. These limits are not a hard cap on the entire directory.

The runtime ZIP is approximately **9.1 MiB**, with approximately **10.6 MiB** of extracted program files before user caches. In the native-window test, core files plus early runtime storage measured about 21 MiB; browser profiles, downloaded libraries and an update backup add to this. Check the timestamped storage view for your own copy, rather than estimating by months of use.

Six actual 30-second sessions retained update progress **40 → 88 → 136 records** with zero cross-session duplicate downloads. All 1,141 local queries succeeded (maximum 17.91 ms on this computer). Updates used controlled responses; these timings are not public-network or all-machine guarantees. See [the full verification record](docs/QA-1.6.0.md).

## Application updates

**Settings → Application** is separate from matchup data updates. Automatic checking defaults to on; automatic downloading defaults to off. No GitHub account is needed. Only authenticated stable releases with a higher version number are eligible, not source commits or prereleases.

| Your action | Result |
| --- | --- |
| Turn automatic downloads on | Both switches are saved together; a background check starts immediately and an eligible update downloads automatically. No second click is needed. |
| Turn automatic checks on | Check immediately; download only if automatic downloads are also on. |
| Turn automatic checks off | Also turn automatic downloads off and stop automatic work, retaining saved progress. |
| Turn automatic downloads off | Pause an automatic download, keep checking for new versions and retain downloaded bytes. |
| Click Check or Download manually | Remains available with automatic checks off; switching automation off does not cancel an explicitly requested manual download. |

An active check/download is reused rather than duplicated. If a cancelled task is still stopping when you re-enable automation, one new check follows its cleanup. Progress and a save confirmation appear immediately in Application settings. Network failure does not undo the saved preferences or affect local lookup.

On later normal launches, the app waits about 8 seconds, then checks if the last successful check was at least 24 hours ago and the last attempt at least one hour ago. If a check is not due, an eligible saved download can resume when automatic downloads are enabled. This is a startup schedule, not a continuous timer. Manually checking or turning a switch on bypasses the intervals. Pause prevents the pending startup task from restarting work in the same session; manually resuming or re-enabling allows it again.

Offline launch saves the switches but makes no update requests. A `--no-auto-update` launch also suppresses automatic work for that session, including enabling a switch; manual checking remains available. Settings explain both cases, and the next normal launch follows the saved choices.

Downloaded bytes survive exit. When the server supports it, HTTP Range resumes the partial package; a server that does not support ranges causes a safe full restart of that download. A verified package is not downloaded again. Installation only starts after **Restart and update** and confirmation; minimizing to the tray does not install an update.

After a successful restart, Application settings reports the installed update and removes the old offer. Upgrade health now requires the native window, application initialization and a local query to succeed. See the [current QA scope](docs/QA-1.7.0.md). Historical short-session checks retain their original version records; they do not establish a public-channel upgrade for this build.

The app accepts update metadata from the project's stable release source only after verification with the embedded publisher public key. ZIP size, SHA-256, the file inventory and paths are checked. The new version must pass a local startup/query check; failure restores the old program. An interrupted replacement is recovered at the next launch. Settings and matchup libraries are excluded from program replacement.

**Publishing prerequisite:** the maintainer must publish the Windows ZIP and matching `luna-update.json` in a stable GitHub Release. Local packaging/signing does not publish anything. A missing compatible release or network failure leaves the current app available and is not reported as a successful upgrade. Source folders cannot be automatically overwritten; their downloaded ZIP can be deployed manually.

## Optional installation and uninstall

Portable use requires no installation. To register Luna in Windows Installed Apps, run **安装Luna.cmd** from the runtime package and choose a new empty folder. Installation is per-user, preserves the original portable copy and adds a Start-menu entry when it would not overwrite another installation's shortcut.

Use **Settings → Application → Uninstall Luna**, or the Windows app list for a registered installation. User data under `cache` is retained by default; app-update staging and rollback files are removed. You may explicitly select deletion of settings, downloaded data and recent queries. Exit other Luna instances from the same folder first. Modified program files, extra files and copies in other directories are preserved. Edge, WebView2 and .NET are not uninstalled. Uninstall is disabled in source folders.

## Documentation

| I want to… | Read |
| --- | --- |
| Install Luna, understand results or manage data | [User guide](docs/USER_GUIDE.md) |
| Build, test or package the source | [Developer guide](docs/DEVELOPER_GUIDE.md) |
| See what changed between versions | [Changelog](CHANGELOG.md) |
| Review data and asset notices | [Third-party notices](THIRD_PARTY_NOTICES.md) |

## Feedback

The [bug report](https://github.com/Francis-Chenyj/Luna-dev/issues/new?template=bug-report.yml) and [feature request](https://github.com/Francis-Chenyj/Luna-dev/issues/new?template=feature-request.yml) forms support English and Simplified Chinese. For a bug, include the Luna version, Windows version, steps, and expected/actual results. **Settings → Support → Copy diagnostics** provides a short summary; screenshots and relevant log excerpts are optional. Remove personal information before sharing.

## Author and acknowledgements

Created and maintained by [**FrancisChen**](https://github.com/Francis-Chenyj), with development assistance from [OpenAI Codex](https://openai.com/codex/).

The README layout draws on [Yifan Ad Skipper](https://github.com/CharryLee0426/yifan-ad-skipper). [League Akari](https://github.com/LeagueAkari/LeagueAkari) is a reference for GitHub project presentation; [Codex Team Token Tracker](https://github.com/CharryLee0426/codex_team_token_tracker) informed the separation of user and developer documentation.

## License

Copyright © 2026 FrancisChen. Luna's original code, scripts, documentation and green L icon use the [MIT License](LICENSE). Third-party game assets, upstream data and third-party content in screenshots retain their own terms; see [Third-party notices](THIRD_PARTY_NOTICES.md).

Luna isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.


## Optional BP synchronization (1.7.0)

Connect the running League client from Luna's home page. Five champion slots per team appear during champion selection; click a portrait to open its local matchup details. The feature is off after every app start, uses only local read-only endpoints, and does not query player history or rank. Unknown lanes use a clearly identified local default rather than the previously selected champion's lane. [Usage, privacy boundaries and compatibility](docs/CLIENT_DRAFT.md).

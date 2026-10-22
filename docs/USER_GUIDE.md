# Luna user guide

<a id="english-guide"></a>

**English** · [简体中文](USER_GUIDE.zh-CN.md) · [Project overview](../README.md) · [Developer guide](DEVELOPER_GUIDE.md) · [Changelog](../CHANGELOG.md)

**Applies to version 1.7.0 · Author: FrancisChen**

See the [1.7.0 repair guide](AUDIT_REPAIRS.md) for scope restoration, missing-profile downloads, storage waiting messages and damaged-update recovery.

Project home: [Luna on GitHub](https://github.com/Francis-Chenyj/Luna-dev). Luna is in development and distributed as a pre-release; [distribution status](../release-status.json) is tracked separately.

This guide describes current operation by task. Version history belongs in the changelog; build and test instructions belong in the developer guide. The interface supports English and Simplified Chinese; examples below include the corresponding Chinese labels.

## Contents

- [Install and launch](#install-and-launch)
- [Settings, appearance and support](#settings-appearance-and-support)
- [Search and choose a lane](#search-and-choose-a-lane)
- [Filters and results](#filters-and-results)
- [Data updates and offline use](#data-updates-and-offline-use)
- [Sources and timestamps](#sources-and-timestamps)
- [Upgrades, backups and storage](#upgrades-backups-and-storage)
- [Troubleshooting](#troubleshooting)
- [Example: winning lane and winning the game](#example-winning-lane-and-winning-the-game)
- [Data sources and notices](#data-sources-and-notices)

## Install and launch

Luna requires Windows 10 / 11, .NET Framework 4.7.2+ and Microsoft Edge WebView2 Runtime. It uses a native desktop window; install the [Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) if needed. Browser compatibility mode (`Luna.exe --browser`) does not support tray controls. Normal use needs no developer tools, login, API key or administrator privileges.

Version 1.7.0 is a local revision. The public release is listed separately on GitHub. With the local Windows package:

1. **Fully extract** `Luna-v1.7.0-Windows.zip` to a writable folder, such as `D:\Apps\Luna`. Do not run from inside the ZIP or copy only the EXE.
2. Keep `Luna.exe`, `Luna.exe.config`, both Microsoft.Web.WebView2 DLLs, `runtimes`, `web` and `data` together.
3. Run `Luna.exe` or `启动Luna.cmd`, then wait for the local-service status at the top right.
4. Search for a champion and choose a lane. The default profile is bundled and ready to query.

| Entry point | Purpose |
| --- | --- |
| `Luna.exe` / `启动Luna.cmd` | Query local data and allow automatic/manual external updates |
| `离线启动Luna.cmd` | Query saved data while prohibiting external updates |
| `创建Luna桌面快捷方式.cmd` | Create or update a desktop shortcut for this folder |
| **退出 (Exit)** in the application | Immediately stop the local service and background updates |

The source ZIP contains no executable; build it using the [developer guide](DEVELOPER_GUIDE.md). If `SHA256SUMS.txt` accompanies a package, use PowerShell's `Get-FileHash -Algorithm SHA256` and compare the ZIP hash with its matching filename. Checksums verify content consistency.

Luna listens only on this computer. Normal launch defaults to port 17653; the offline script uses 17683, with subsequent ports tried if occupied. Luna does not configure system startup, proxies or firewall rules. The title-bar close button and Alt+F4 ask whether to minimize to the tray or exit. The tray keeps ongoing updates alive; double-click its green L icon or relaunch the same shortcut to restore the existing query. Exit in the page or tray menu stops the service immediately. Remembering a choice is optional; change it in **Settings → General → When closing the window**. Windows shutdown/logoff is never blocked by this choice. Browser/headless mode retains the 10–11 minute idle timeout.

## Settings, appearance and support

Open **Settings** at the top right. Changes are saved in `cache/settings.json` beside the app, shared by normal/offline launches and different local ports. Keep this folder when upgrading. Separate copies of Luna have separate preferences.

| Section | Controls |
| --- | --- |
| General | Language: Follow system, 简体中文 or English. Appearance: Follow system, Dark or Light. Changes apply immediately and preserve your selected champion, lane, filters and active work. |
| Data | Update data automatically at startup. The switch applies **on the next service launch** and does not start or stop an existing task. Open Data management for manual checks, full-library downloads or Stop update. |
| Support | Open logs/data folders and copy diagnostics. A preview shows exactly what was copied. If clipboard access fails, select the preview and press Ctrl+C. |
| About | Luna icon, version, FrancisChen, Luna homepage, local changelog, original-code MIT license and third-party notices. These documents remain readable offline. |

Follow system uses the Windows display language at launch (Chinese selects Simplified Chinese; other languages select English) and responds to system light/dark appearance changes. An explicit choice overrides the system. Closing a window is not necessarily a new service launch: use **Exit**, then start Luna again to apply a changed startup-update preference.

In 1.6.0, the owned desktop window follows the selected theme on supported Windows versions. App changes are applied immediately; system-theme changes are detected within about one second. Windows high contrast preserves system caption colors. Older Windows versions and browser compatibility mode may keep system/browser frame colors. Restart after upgrading the EXE. See [QA](QA-1.6.0.md) for measured results and limits.

Offline mode always disables network updates. The `--no-auto-update` launch option always disables automatic updates for that run; saved preferences cannot override either restriction. This switch updates data, not the Luna application itself.

The data-folder button opens `cache/`; logs are in `cache/logs/`. Older `cache/error.log` files are left untouched. Startup failures remain in `启动错误.txt` beside the EXE. Diagnostic copying includes only the Luna version, run mode, effective startup-update flag and error-category counts for the current process. It excludes raw errors, paths, accounts, tokens and search history, and sends nothing automatically. Log files themselves can contain local technical details.

If preferences are unreadable, Luna uses system appearance/language and disables startup updates until you save a choice. Saving preserves the unreadable file locally. A settings file from an unsupported future version is not overwritten.

## Search and choose a lane

Search by Chinese or English name, title, pinyin, abbreviation or curated nickname. Examples include `李青 / 盲僧 / Lee Sin / the Blind Monk / 瞎子` and `厄加特 / Urgot / 螃蟹 / 蟹`. English case, spaces and common punctuation do not affect matching; ambiguous nicknames keep multiple suggestions.

Use the arrow keys to select a suggestion and Enter to confirm. **Ctrl+K** returns to search. Click a result card to explore that champion.

| Action | Lane behavior |
| --- | --- |
| Search/select a suggestion on home, or click a Recent / Popular champion on any page | Start with that champion's common lane |
| Search within a champion detail page, or click a matchup card | Retain the current lane |
| Click a lane manually | Use it and retain it for subsequent detail-page searches |
| Browser back / forward | Restore the lane saved in that history entry |

The common lane is the available lane with the largest game count in the local catalog. The interface labels the context as default, retained, selected or restored. Clicking Luna to return home ends the current lane context.

For example, searching for Ahri while viewing Aatrox top retains top. Returning home before searching uses Ahri's common lane. If a retained lane is unavailable, select one of the listed valid lanes. Valid empty results do not switch lanes automatically, and a background catalog update does not change the lane being viewed.

Recent queries use browser local storage and may differ when you change browsers or service ports.

## Filters and results

The left **Countered by (严父)** list contains opponents that counter the selected champion; the right **Strong against (大儿)** list contains opponents it counters. Each direction preserves the upstream categories, order, values and blue conjugate markers independently.

| Control | Meaning |
| --- | --- |
| Lane | Top, jungle, middle, bottom or support; unavailable lanes cannot be selected |
| Counter threshold (严父阈值) | Strength filter, levels 1–5, affecting both directions; higher is stricter |
| 场次严谨度 (Sample strictness) | Upstream sample-size filter, levels 1–5 |
| 查看范围 (Rank category) | Filters saved categories without additional downloads |

The defaults are threshold **1** and strictness **5**. The bundled profile covers 173 champions, 428 valid champion–lane combinations and 856 direction records. Coverage does not mean every champion has data in every lane.

There are 25 threshold/strictness profiles, of which one is bundled in full. Other profiles need their own exact-match data. If a profile is missing, restore the default to query immediately or download the selected profile through data management.

The seven categories are All ranks, High / Mid ranks, High ranks only, High / Low ranks, Mid ranks only, Mid / Low ranks and Low ranks only. The upstream labels describe High as Master+, Mid as Diamond+ and Low as Emerald+.

- **仅高分段 (High ranks only)** shows only that standalone category, excluding High / Mid, High / Low and All ranks.
- **全分段 (All ranks)** is itself an upstream category. Choose **全部七类分段 (All seven categories)** to show every category.
- Relationship values are **not win rates**; do not add a percent sign. Slider levels are not direct numeric cutoffs for the card values.
- Blue **共轭 (conjugate)** markers follow the upstream definition: relationships can reverse between rank categories.
- A valid empty result is distinct from missing data, corruption or a failed read.

## Data updates and offline use

**Queries always read local data; external updates run separately in the background.** With startup data updates enabled (the initial default), a new normal service schedules one version check about one second after startup. It checks the default and previously completed profiles. Unchanged, complete profiles are not downloaded again; all 25 profiles are not filled automatically.

| Control | Scope |
| --- | --- |
| **更新离线库 (Update library)** on home, or **数据管理 (Data management)** | Check, download or resume complete profiles; manual updates include the selected profile and can optionally fill all 25 |
| **联网更新此英雄 (Update this champion)** on a detail page | Refresh both directions for this champion, lane, threshold and strictness, even when the upstream version is unchanged |
| Status below search | Inspect progress, open management, stop an update or retry manually |

Background work prioritizes current and recent champions before continuing the complete library. A validated pair can be used before the library finishes; this does not mark the entire library as updated. Manual champion requests can take priority within an ongoing library task.

- Successful updates trigger the current page to reread its data.
- Failure, cancellation or exit preserves previously usable complete results and validated champion pairs.
- Resumption requires the same upstream edition and retained temporary records. Extra profiles that have never completed must be selected manually again.
- Closing the management panel does not stop downloading. **停止更新 (Stop update)** prevents the automatic task from restarting during this service run; manual updates remain available.
- The default profile contains 856 records and downloads are rate-limited. Filling every profile can take hours; local results remain available first.

The offline launcher prohibits external updates while retaining access to all saved default and extra profiles. Network buttons are disabled in offline windows; use the normal launcher to update. To disable only the startup check while allowing manual updates, see [local launch options](DEVELOPER_GUIDE.md#local-launch-options).

Data updates **do not install a newer Luna application**. Manage application updates separately in **Settings → Application**. Automatic download is initially off; installation requires confirmation and a compatible signed stable release.

In 1.7.0, enabling automatic downloads enables checks too and immediately checks/downloads an eligible update. Turning checks off turns automatic downloads off; turning only downloads off retains checks and saved progress. Manual downloads remain available. Offline or manual-only launches explain that choices are saved without automatic networking this session. After a successful restart, the app reports success. See [Application updates in the README](../README.md#application-updates) for the complete rules.

## Sources and timestamps

Each direction shows its own source and dates instead of combining different records into one “last updated” time.

| Chinese source label | Meaning |
| --- | --- |
| 程序自带数据 (Bundled data) | The offline library shipped with the package |
| 联网更新的数据 (Downloaded data) | A complete library or validated pair saved by an earlier download; this query still reads local files |
| 备用旧数据 (Fallback data) | A previous library, legacy cache or exact-query result from this window, with the original source retained |

**Source generated (数据生成)** is the upstream generation time, not the query time or game-patch date. **Saved locally (本机更新)** is when that record was downloaded and saved; bundled records say **Included with the app (此份随程序提供)**. Missing dates are marked as unrecorded.

The management panel separately records the **last successful upstream check**. An unchanged-version check does not refresh the local-save time. Retries and fallback preserve original dates. Moving the folder preserves those records too; they do not become the installation date on the new computer.

## Upgrades, backups and storage

Complete 1.6.0 runtime folders support **Settings → Application → Check for app updates**. Downloads can resume; confirming Restart and update replaces program files while keeping preferences and offline data. A failed local startup/query health check restores the old program. Without a signed stable update, the current version remains usable. Source folders cannot be upgraded in place.

For manual migration or older releases, use **退出 (Exit)** to stop the old service first:

1. Keep the old folder as a backup and extract the complete new Windows package into another writable folder.
2. Before launching the new copy, **copy** the old `cache` folder into it to preserve language/theme/startup-update preferences, downloaded profiles, champion updates and resume progress. Keep the original folder intact.
3. Launch from the new folder. Rerun `创建Luna桌面快捷方式.cmd` if you want a desktop shortcut.
4. Check **Settings → About** for the version, confirm your preferences and a saved query, then retain the old folder until you are satisfied. The package does not migrate or delete old folders automatically.

Native-window recent queries use local storage inside `cache/webview2`; browser compatibility mode uses that browser's profile. Records are tied to an origin, so different browsers or ports may show different histories. When two local review ZIPs share the same version, identify the intended build using the accompanying `SHA256SUMS.txt` and release-file inventory; do not identify it by its filename alone.

| Location | Contents |
| --- | --- |
| `data/` | Bundled catalog, relationships, search terms and portraits |
| `cache/settings.json` | Language, appearance and startup-data-update preferences |
| `cache/library/` | Downloaded profiles, previous editions and resumable downloads |
| `cache/focus/` | Validated champion pairs and priority records |
| `cache/detail-*.json` | Preserved legacy single-query records |
| `cache/logs/error.log` / `启动错误.txt` | Update or startup diagnostics |

Deleting `cache` removes preferences, downloaded updates, local logs and resume progress, while leaving the bundled default library intact. It should not be the first response to an update failure.

Maintenance is delayed after startup, retains a 24-hour successful-run interval across restarts, and also runs after new downloads. Each profile retains its current library and one previous usable edition; unfinished downloads can expire after seven days without progress. Champion caches normally retain at most 64 pairs and 8 MiB. Confirmed damaged champion caches and settings backups enter a quarantine capped at 20 files, 10 MiB and seven days. Locked files, unowned files and possibly last-recoverable data are kept conservatively; these are not whole-directory hard quotas.

**Settings → Data → Storage** shows categorized file bytes and their measurement time, including interface caches and application backups. Queries do not continuously add complete libraries; downloading more profiles and editions increases storage. These numbers are neither process memory nor Windows disk allocation size.

Optionally run **安装Luna.cmd** to install into a new empty folder and register with Windows Installed apps. Uninstall from **Settings → Application**, **卸载Luna.cmd**, or the installed copy's Windows entry. User data is kept by default while app-update staging is removed; the entire local `cache` is removed only when explicitly selected. Extra or modified files, other copies and shared runtimes remain. See the [README maintenance instructions](../README.md#optional-installation-and-uninstall).

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Luna does not start | Fully extract the package, check that its folder is writable and .NET Framework meets the requirement; inspect `启动错误.txt` and try a shorter folder path |
| Local service disconnected | Run the launcher again, then choose **重新连接 (Reconnect)** or use the new window; previously displayed results remain readable |
| Selected profile has not been saved | Restore default 1 / 5 or download that profile; missing data does not mean no matchup exists |
| Current lane is unavailable | Select one of the valid lanes offered on the page |
| Update did not finish | Continue using saved data and retry later from a normal launch; offline mode cannot update |
| Query fails or times out | Transient failures retry at most twice within a total five-second wait; then choose **重新查询 (Retry query)**. Corrupt or mismatched data is reported separately |
| Old dates or backup data appear | Inspect each direction's source and management status; checking or rereading does not mean a download completed |
| Shortcut fails after moving Luna | Rerun the shortcut script from the new folder |
| Browser taskbar icon is not Luna | A normal browser tab uses the browser's taskbar icon; launch Luna.exe directly for its native window and icon |
| Service continues after closing a window | Tray mode intentionally stays active; use **Exit** from the page or tray menu. Only browser/headless mode uses the idle timeout |
| Old interface remains after an upgrade | Verify that you started from the new complete folder, then refresh with Ctrl+R; the previous folder's shortcut may still launch the old copy |

For a useful problem report, record the Luna and Windows versions, normal/offline launch, champion/lane/filter settings, reproduction steps, and expected versus actual results. For display issues, include language, theme, Windows display scale and browser zoom. Screenshots and short log excerpts are optional. **Settings → Support → Copy diagnostics** provides a limited summary; check any extra logs for private information before sharing. Use the bug-report and feature-request links in the README.

## Example: winning lane and winning the game

Lane performance and full-game outcomes measure different things. This existing user-provided screenshot is an interpretation example, not a current-patch recommendation or live statistics calculated by Luna.

| Screenshot metric | Urgot | Dr. Mundo |
| --- | ---: | ---: |
| Full-game win rate when the two meet | 40.85% | 59.15% |
| Lane-kill rate | 61.11% | 38.89% |
| KDA | 2.16:1 | 1.70:1 |
| Kill participation | 36.03% | 31.64% |
| Damage to champions | 30,547 | 19,766 |
| Time to destroy the first turret | 15m 59s | 17m 30s |
| Overall win rate in the position | 51.82% | 49.40% |

[View the original user-provided example](urgot-mundo-example.png).

Urgot leads several lane metrics while having a lower full-game win rate. The turret metric is a time, not a first-turret rate; overall positional win rate is not the win rate of this specific matchup. The screenshot does not identify its patch, ranks, period or sample size. It illustrates that these measurements can differ without establishing why.

Luna continues to show actual upstream results for the selected lane, threshold and strictness. This example does not derive the upstream model formula or override any current relationship.

## Data sources and notices

Relationships come from [MalzaharAI](https://www.malzaharai.top/counter), names and basic labels from Tencent's catalog, and English/Traditional Chinese labels from Riot Data Dragon. Community-term references are recorded in the bundled search data. Luna preserves upstream results, does not train a model and does not equate generation dates with game patches. Other upstream votes and ratings remain on the original site.

The application is developed by **FrancisChen** and has no affiliation with or endorsement from those sources. See [third-party notices](../THIRD_PARTY_NOTICES.md) for content scope and [README acknowledgements](../README.md#author-and-acknowledgements) for reference projects.

## Desktop-window upgrade (1.6.0)

The owned window uses Microsoft WebView2, preserving language/theme/settings and `cache/library`. Old Edge localStorage is not copied, so its recent-query chips start fresh. New recent history resides in `cache/webview2`. Disposable WebView2 DiskCache/CacheStorage is cleared asynchronously at startup; this never clears localStorage or matchup files. The WebView2 profile and shared installed runtime are additional to the matchup-data space estimate. No fixed total profile size is promised.

On Windows 11, supported title-bar colors follow app light/dark/system appearance. Windows 10 and older runtimes may retain system frame colors. High-contrast mode leaves caption colors to Windows. If the native page cannot load, closing the window offers a native Yes (tray) / No (exit) / Cancel fallback; this startup fallback does not remember a choice.


## Optional BP synchronization (1.7.0)

Connect the running League client from Luna's home page. Five champion slots per team appear during champion selection; click a portrait to open its local matchup details. The feature is off after every app start, uses only local read-only endpoints, and does not query player history or rank. Unknown lanes use a clearly identified local default rather than the previously selected champion's lane. [Usage, privacy boundaries and compatibility](CLIENT_DRAFT.md).

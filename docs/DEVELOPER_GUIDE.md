# Luna developer guide

**English** · [简体中文](DEVELOPER_GUIDE.zh-CN.md) · [Project overview](../README.md) · [User guide](USER_GUIDE.md) · [Changelog](../CHANGELOG.md)

**Applies to version 1.7.2 · Author: FrancisChen**

Fault-test conventions and the migration from private-state reflection are described in [test maintenance](TESTING.md). New tests use typed services, injected dependencies and actual runtime lifecycles.

See [audit repairs and test commands](AUDIT_REPAIRS.md), [contributing](CONTRIBUTING.md) and [security reporting](SECURITY.md).

Project home: [Luna on GitHub](https://github.com/Francis-Chenyj/Luna-dev).

This guide covers the existing build, tests and local packaging workflow. Start with a source checkout or `Luna-v1.7.2-Source.zip`; the Windows package does not contain the development source or tests. Run commands from the source root.

## Environment

| Tool | Needed for |
| --- | --- |
| Windows with .NET Framework 4.7.2+ | Building and running Luna |
| Windows PowerShell | Existing build and C# test scripts |
| Node.js 18+ | JavaScript and HTTP tests |
| Microsoft Edge WebView2 Runtime | Native windows and the real `-Desktop` readiness smoke test |
| Python 3, standard library | Optional offline-library acquisition and search-index maintenance |

The build uses the installed .NET Framework `csc.exe`, looking in `Framework64/v4.0.30319` before `Framework/v4.0.30319`. The hero-update and long-path test wrappers currently use the Framework64 compiler directly. The complete-suite entry point requires 64-bit Windows and the Framework64 compiler.

The build and baseline checks need no npm installation; optional browser verification uses locked Playwright development dependencies. No frontend bundler or external C# restore is required. Node.js and Python are not runtime dependencies for end users.

## Repository layout

| Path | Responsibility |
| --- | --- |
| `src/ApplicationRuntime.cs` | Service composition, launch options and worker lifetime |
| `src/Program.cs` | HTTP/native process host calling explicit services |
| `src/ClientDraft.cs`, `web/draft.js` | Optional local-client BP synchronization, champion-only projection and on-demand details |
| `src/Library.cs`, `src/QueryService.cs` | Indexes, validation, atomic publication and local queries |
| `src/DataUpdates.cs` | Shared full-library/champion scheduling, progress and cancellation |
| `src/Storage.cs`, `src/QuarantineStore.cs` | Conservative retention, space reports and quarantine |
| `src/Settings.cs`, `src/DiagnosticLog.cs` | Preferences and independently owned diagnostics |
| `web/i18n.js`, `web/locales.js`, `web/settings.js` | Bilingual messages and settings; themes use semantic CSS colors |
| `src/LocalHttp.cs` | Strict body-free request parser, total header deadline and per-connection cancellation |
| `src/LocalIO.cs` | Long-path file I/O; callers retain ownership and reparse-point validation |
| `src/DesktopWindow.cs` | WinForms/WebView2, tray, close policy and application readiness |
| `src/AppUpdate.cs`, `src/UpdateTrust.cs` | Application updates, resumable downloads and publisher public key |
| `src/Deployment.cs` | Independent maintenance, manifests, transaction recovery and uninstall |
| `src/DesktopIcons.cs` | Icons for Luna's own Edge app windows |
| `src/AssemblyInfo.cs` | Product name, author and version metadata |
| `web/` | Plain HTML, CSS and JavaScript, search index and Luna icons |
| `data/` | Bundled catalog, default relationship library, search labels and portraits |
| `scripts/` | Packaging, icon/shortcut generation and data-maintenance tools |
| `tests/` | Existing JavaScript, C# and HTTP verification |
| `.github/` | Bilingual issue forms and Windows verification workflow |
| `docs/` | User/developer guides, review records and illustrations |
| `cache/` | Local runtime state, excluded from source and packages |
| `dist/` | Generated packages, excluded from source |
| `research/` | Local acquisition, isolated checks and archives, excluded from source and packages |

Queries read local files. External updates validate champion/lane/profile identity and the upstream edition before activating results. A champion's two directions are saved together; a complete profile is activated only when all its records pass validation. The frontend preserves the source's categories and ordering.

Preferences update one allowlisted field under a cross-process lock and atomic replacement. Startup updates read the preference only at process launch; offline and command-line restrictions take precedence. Add UI text to the locale table using named or existing Chinese-source keys, without rewriting upstream records. Diagnostic fields must stay explicitly allowlisted.

## Build

Exit any Luna instance whose executable you are replacing, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script regenerates icons, builds `Luna.exe` and `Luna.Maintenance.exe`, and copies two WebView2 managed DLLs plus the three architecture loaders under `runtimes`. The SDK is pinned and hash-checked in `vendor/webview2`; no network restore is required. Running also requires `Luna.exe.config`, `web` and `data`. Installation/update/uninstall require the generated `installed-files.json`. The maintenance executable embeds the .NET 4.7.2 target and long-path manifest; temporary workers verify and copy the main runtime config. File I/O supports deep paths without changing Windows long-path policy.

To put those compiled files, configuration and runtime dependencies in a different build directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\dist\dev-build
```

This alternate output does **not** copy `web` or `data`; use a full runtime layout to launch it. Icon generation still writes to the source tree's `web` folder.

## Local launch options

| Option | Behavior |
| --- | --- |
| `--offline` | Prohibit external updates; retain local reads and explicitly enabled local BP synchronization |
| `--no-auto-update` | Disable automatic data/application updates for this run, including preference-triggered checks; allow manual updates |
| `--no-browser` | Run the local service without opening a window |
| `--browser` | Use browser compatibility mode instead of the native window; no tray controls |
| `--port <number>` | Choose a starting port between 1024 and 65000; try up to 29 subsequent ports if occupied |

The EXE's default starting port is 17653. The provided offline launcher additionally selects 17683. Exit an existing normal service before testing `--no-auto-update`: reopening the same instance does not replace its startup configuration.

For manual offline testing after a normal root build:

```powershell
.\Luna.exe --offline --no-browser --port 18783
```

The service writes its actual URL to `cache/running-url.txt`. Open that local URL to inspect the interface. Use the in-app **退出 (Exit)** button when finished; closing a tab alone leaves the idle timeout in effect.

## Existing tests

The optional browser suite needs Node 20+ and the exact development dependency lockfile. Install with `npm ci --ignore-scripts --no-audit --no-fund`, then `node node_modules/playwright/cli.js install chromium`. Run `scripts/check.ps1 -Browser`; for an already installed Edge, add `-BrowserChannel msedge` instead of downloading Chromium. The Windows workflow uses Chromium. Browser checks create a verified package copy and temporary browser profile, block external origins, and retain `browser/result.json`, screenshots and `trace.zip`. They never use a personal browser profile or cache. The default offline suite/build does not need npm. Installation and execution are separate so a test never silently downloads tools.

For a packaged runtime alone: `node tests/browser-runner.mjs --runtime <unpacked-Luna> --output <new-output-directory> --channel msedge`. The output directory must be new and outside the input package.

Maintenance recovery has a durable boundary at the committed journal. Cleanup/receipt failures after that boundary do not trigger rollback. Abnormal backup/stage/archive contents are retained under `cache/app-update/*-preserved-*` or `package-rejected-*`; only completely owned backups are automatically reclaimed. Application readiness probes request port zero, read the per-token child PID/port record and check the same token in the health response. Normal launches still use the documented port range.

### Complete local verification

Run the same entry point used by the Windows workflow:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
```

This creates a fresh `dist/checks/<unique-id>/` directory with an isolated source snapshot, a `report.json`, a readable `summary.md`, per-step logs, packages and extracted test copies. It preserves the working executable, icons and personal cache. Node.js must be on PATH, or pass `-NodePath <absolute-node.exe-path>`. To choose the output directory, use `-OutputDirectory .\dist\my-check`; it must be new and under this source tree's `dist` directory. Existing results are never overwritten.

The sequence is JavaScript and repository consistency checks → runner failure/timeout checks → build → all 14 C# suites → offline HTTP → packaging and inventory/hash checks → full offline HTTP against the extracted Windows ZIP → runtime/recovery smoke checks → rebuild from the extracted Source ZIP. These are 23 check groups; `-Browser` adds the 24th group. Each HTTP pass compares all bundled default-profile records and verifies request boundaries, settings, documents and assets. The runner stops on failure, uses bounded waits, stops only the processes it starts, and preserves logs for inspection. Temporary fixture files remain under the check directory's `temp` folder.

The source package's `.github/workflows/windows-checks.yml` uses `windows-2022`, Windows PowerShell and Node.js 24. It runs on branch pushes, pull requests and manual requests, with read-only repository permissions, pinned official actions and no artifact upload or Release step. Its setup can download Node.js/actions and the locked Playwright/Chromium test tools; Luna tests use local fixtures and offline services, without contacting data providers or using personal credentials. Development configuration is excluded from the Windows package. [GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [runner image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md).

Check the repository Actions page for the result of each GitHub-hosted run. Local results are recorded separately. Checksums confirm content integrity; human review of language, themes and layout remains part of release review. Successful technical checks do not resolve third-party distribution conditions.

For layout changes, also open an isolated offline query in a real browser and check English/Simplified Chinese, light/dark themes, and widths 360, 640, 720, 721, 800, 900, 1000, 1001 and 1440 CSS pixels. Keep all rank groups open to include conjugate labels and long champion titles. Import `inspectLayout` from `tests/browser-smoke.mjs` into the browser automation session and evaluate it in the page after each change to measure overlap and card boundaries, then inspect screenshots. The same layout probe runs in the optional browser suite and Windows CI; screenshots still need human review. The Node UI simulation alone does not exercise a rendering engine. In particular, 721–1000 pixels must not be skipped: an old negative metric margin caused overlapping text there.

### Individual suites

Run checks relevant to the change. The default C# suites use isolated test directories and local fixtures or simulated sources.

```powershell
node --test .\tests\core.test.cjs .\tests\ui.test.cjs .\tests\i18n.test.cjs .\tests\repository.test.cjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-settings.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-stability.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-storage.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-auto-update.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-focus.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-hero-update.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-icons.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-longpath.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-lifecycle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-http-transport.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-service-isolation.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-backend-regressions.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-client-draft.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-client-draft-boundaries.ps1
```

| Suite | Main coverage |
| --- | --- |
| `i18n.test.cjs`, `run-settings.ps1` | Translation coverage, placeholders, contrast, persistence, concurrent saves, corrupt settings and diagnostic privacy |
| `core.test.cjs`, `ui.test.cjs` | Search labels, lane selection, filters, UI state and query behavior |
| `run-stability.ps1` | Query/update stability and validated fallback |
| `run-storage.ps1` | Retention and conservative cleanup boundaries |
| `run-auto-update.ps1` | Startup scheduling, failures, resumption and reads during activation |
| `run-focus.ps1` | Champion priority, two-direction updates and cache reuse |
| `run-hero-update.ps1` | Manual champion-update queue and state |
| `run-icons.ps1` | Native icon resources |
| `run-longpath.ps1` | Deeper install paths and update temporary paths |
| `run-lifecycle.ps1` | Signed package validation, upgrade rollback, uninstall and retention |
| `run-http-transport.ps1` | HTTP framing, timeouts and disconnect cancellation |
| `run-service-isolation.ps1` | Independent runtime ownership and concurrent operations |
| `run-backend-regressions.ps1` | Maintenance retries, receipts and directory identity |
| `run-client-draft.ps1` | BP session parsing, opt-in lifecycle and privacy |
| `run-client-draft-boundaries.ps1` | Client discovery, response limits and phase-transition races |

The automatic-update suite uses the bundled 856 records as default-profile fixtures; extra conditions are simulated. `run-focus.ps1 -Live` explicitly contacts the real upstream service and is not part of the default offline checks. A real upstream check is separate from compiling and packaging.

## Isolated HTTP verification

Use a new runtime copy with no personal cache so the test compares bundled snapshots, not newer downloaded data. After building in the source root, this example creates an isolated copy under ignored `research/`, checks its actual local port and stops only the process it launches:

```powershell
$ErrorActionPreference = 'Stop'
$projectPath = (Get-Location).Path
$testPath = Join-Path $projectPath ('research\http-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
foreach ($item in @('Luna.exe', 'Luna.exe.config', 'web', 'data', 'LICENSE', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectPath $item) -Destination $testPath -Recurse
}
$testProcess = Start-Process -FilePath (Join-Path $testPath 'Luna.exe') `
    -ArgumentList '--no-browser', '--offline', '--port', '18783' `
    -WorkingDirectory $testPath -WindowStyle Hidden -PassThru
try {
    $addressFile = Join-Path $testPath 'cache\running-url.txt'
    $testUrl = ''
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if (Test-Path -LiteralPath $addressFile) {
            $testUrl = (Get-Content -LiteralPath $addressFile -Raw).Trim()
            if ($testUrl -match '^http://127\.0\.0\.1:\d+$') { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($testUrl -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'Test service did not write its local address.' }
    node .\tests\server.mjs $testUrl --offline
    if ($LASTEXITCODE -ne 0) { throw 'HTTP checks failed.' }
} finally {
    $testProcess.Refresh()
    if (-not $testProcess.HasExited) { Stop-Process -Id $testProcess.Id }
}
```

The HTTP suite checks all 856 bundled records, correct source dates, valid empty lists, profile isolation, local HTTP access restrictions and response timing. It leaves the isolated copy available for inspection.

For deterministic checks of a normal service, change the launch arguments to `--no-browser --no-auto-update --port 18753` and omit `--offline` from the Node command. Keep the isolated directory. Do not click update controls during this snapshot comparison.

## Package and verify

Use a **new empty output directory**. The default `dist/v1.7.2/` may already contain local artifacts; the following example chooses a separate directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -OutputDirectory .\dist\v1.7.2-review
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-package.ps1 -ReleaseDirectory .\dist\v1.7.2-review
```

Choose another unused directory for a repeat run. Packaging refuses to mix with existing files.

| Output | Contents |
| --- | --- |
| `Luna-v1.7.2-Windows.zip` | Rebuilt EXE, runtime files, bundled data, both READMEs and documentation |
| `Luna-v1.7.2-Source.zip` | Source, scripts, tests, data and documentation; no EXE |
| `SHA256SUMS.txt` | SHA-256 values for both ZIPs and the manifest |
| `release-files.json` | Every archive entry's path, size and hash |
| `_build/` | Intermediate local build, not a download attachment |

The script derives the version from `src/AssemblyInfo.cs` and checks it against `release-status.json`. It rebuilds instead of copying the root executable and verifies the resulting version and author.

Both ZIPs have a `Luna/` prefix and include the license, notices and review status. Explicit roots/extensions exclude caches, research, personal shortcuts and common secret-file formats. **Both packages currently include third-party data and assets.** Packaging success does not change `publicReleaseReady` or create a GitHub repository/Release.

The package check verifies hashes, file lists, path safety, release metadata and runtime/source separation. Source packages also include `.gitattributes`, issue forms, the workflow and the unified check scripts; runtime packages exclude development configuration. `repository.test.cjs` checks current version/author consistency, primary project links, and referenced local document/image files. Human review should also cover language switches, anchors and layout; ZIP integrity does not prove that navigation is correct.

## Runtime acceptance

Before handing a package to users, follow the [release checklist](RELEASE_CHECKLIST.md). Use one selected Windows ZIP for a fresh extraction and another extraction for cache migration. Check ordinary-user launch, search and filters, language/themes, offline About/diagnostics, startup-update preferences, Exit and relaunch. Use test-created settings and fixture data rather than a personal cache.

Windows display scaling and browser zoom are separate controls. Check real Windows scaling at 125% and 150% with browser zoom at 100%; record the observed pixel ratio, language/theme, viewport and screenshots. Restore the original display setting afterwards. Changing only the browser width or zoom does not count as a Windows scaling check. Document separately any OS/browser combination or clean-machine environment that was not available.

Use the release inventory and SHA-256 to distinguish local builds sharing a version number. The [QA scope](QA-1.7.2.md) describes verification boundaries; the [release notes](RELEASE-v1.7.2.md) describe the selected build. Run relevant checks again when the code or package contents change; do not silently replace an already verified ZIP.

## Data and search maintenance

Ordinary users update through Luna. These scripts are maintainer tools:

| Tool | Behavior |
| --- | --- |
| `scripts/build-search-index.py` | Without arguments, regenerate `web/aliases.js` from the saved combined search list |
| `scripts/build-search-index.py --english <path> --traditional <path>` | Merge matching Data Dragon catalogs and `data/community-aliases.json`, then rebuild the combined list and browser index |
| `scripts/prepare-library.py --profile default` / `--profile all` | Contact upstream APIs and save resumable acquisition under `research/acquisition/` |
| `scripts/download-icons.mjs` | Download portraits from the source configured in that script |
| `scripts/build-icon.ps1` | Generate Luna's own geometric icon assets |

Use matching official catalog coverage for a search rebuild, record source/version changes and rerun the search tests. Verify acquisition manifests before replacing `data/library/<threshold-strictness>/`. Acquisition is not required for a normal build and should be considered separately from the unresolved distribution review.

## Documentation and release metadata

| Document | Purpose |
| --- | --- |
| [English README](../README.md) / [简体中文 README](../README.zh-CN.md) | Project introduction, preview, requirements and navigation |
| [User guide](USER_GUIDE.md) / [简体中文用户指南](USER_GUIDE.zh-CN.md) | Installation, operation, updates and troubleshooting |
| [Developer guide](DEVELOPER_GUIDE.md) / [简体中文开发指南](DEVELOPER_GUIDE.zh-CN.md) | Layout, build, testing and local packaging |
| [Changelog](../CHANGELOG.md) | Versioned changes; an Unreleased section is used when unpublished work exists |
| [Repository details](REPOSITORY_SETUP.md) | About description, project URL and topics |
| [Release notes](RELEASE-v1.7.2.md) | Development pre-release description and attachment list |
| [Release checklist](RELEASE_CHECKLIST.md) | Source/upload boundaries, runtime acceptance and outstanding publication items |
| [QA record](QA-1.7.2.md) | Dated verification scope and its limits |
| [Third-party review](THIRD_PARTY_REVIEW.md) | Distribution evidence and unresolved items |

Keep the two language editions in step when changing user-visible behavior. The product is **Luna**, author **FrancisChen**, repository **Francis-Chenyj/Luna-dev**; reference-project links belong in README acknowledgements.

For a future application version, update versioned source/UI expectations and release metadata together; the HTTP and package checks derive the expected release version from metadata. Tooling/documentation-only work stays under **Unreleased** unless a new version is intentionally prepared. Issue forms and the Windows workflow are active in the published repository. Local builds do not upload code or create a Release; publication remains a separate maintainer action.

## Desktop host / 桌面窗口 (1.7.2)

`src/DesktopWindow.cs` owns the WinForms/WebView2 window and NotifyIcon. HTTP runs on a dedicated thread; query and update workers are unchanged. Close choices are stored as `closeAction: ask|tray|exit` in schema-1 preferences (missing field defaults to ask). Explicit Exit and Windows session end bypass the preference. `/api/window/close` and `/api/window/show` require the same POST, X-Luna, loopback Host and Origin checks as other mutations. Do not use them from external origins.

自有窗口在主 STA 线程运行，本地 HTTP 使用独立线程。托盘保持后台任务；退出取消更新、关闭监听并释放托盘和 WebView2。标题栏写入不再依赖可能不受支持的 DWM 属性读取。Windows 10 兼容性及高对比度仍应在发布前实机验证。

`scripts/desktop-dependencies.ps1` verifies and copies the pinned Microsoft SDK from `vendor/webview2`; builds do not fetch packages. Both runtime and source ZIPs include the required dependencies and Microsoft license. The installed Evergreen Runtime remains separate. Native navigation stays on this Luna origin; user-selected HTTP(S) external links open in the default browser. No remote debugging port or host-object bridge is exposed.

WebView2 uses `cache/webview2` and clears only DiskCache/CacheStorage asynchronously after an idle delay, at most once daily. Never delete the complete profile to clean caches: it contains the recent-query localStorage. Tests run in private folders and headless HTTP does not initialize WebView2.


See [Application lifecycle / 程序维护](APPLICATION_LIFECYCLE.md) for signing, installation, updates, rollback and uninstall.

See [runtime ownership](ARCHITECTURE.md) for service boundaries, concurrency rules and fixture migration. Run the new suite with `tests/run-service-isolation.ps1`.


## Optional BP synchronization (1.7.2)

Connect the running League client from Luna's home page. Five champion slots per team appear during champion selection; click a portrait to open its local matchup details. The feature is off after every app start, uses only local read-only endpoints, and does not query player history or rank. Unknown lanes use a clearly identified local default rather than the previously selected champion's lane. [Usage, privacy boundaries and compatibility](CLIENT_DRAFT.md).

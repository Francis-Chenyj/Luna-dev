# Luna developer guide

**English** · [简体中文](DEVELOPER_GUIDE.zh-CN.md) · [Project overview](../README.md) · [User guide](USER_GUIDE.md) · [Changelog](../CHANGELOG.md)

**Applies to version 1.6.3 · Author: FrancisChen**

See [audit repairs and test commands](AUDIT_REPAIRS.md), [contributing](CONTRIBUTING.md) and [security reporting](SECURITY.md).

Project home: [Luna on GitHub](https://github.com/Francis-Chenyj/Luna-dev).

This guide covers the existing build, tests and local packaging workflow. Start with a source checkout or `Luna-v1.6.3-Source.zip`; the Windows package does not contain the development source or tests. Run commands from the source root.

## Environment

| Tool | Needed for |
| --- | --- |
| Windows with .NET Framework 4.7.2+ | Building and running Luna |
| Windows PowerShell | Existing build and C# test scripts |
| Node.js 18+ | JavaScript and HTTP tests |
| Microsoft Edge WebView2 Runtime | Native windows and the real `-Desktop` readiness smoke test |
| Python 3, standard library | Optional offline-library acquisition and search-index maintenance |

The build uses the installed .NET Framework `csc.exe`, looking in `Framework64/v4.0.30319` before `Framework/v4.0.30319`. The hero-update and long-path test wrappers currently use the Framework64 compiler directly. A 64-bit Windows development environment is therefore recommended for the complete suite.

There is no npm install, frontend bundler or external C# package restore in the current workflow. Node.js and Python are not runtime dependencies for end users.

## Repository layout

| Path | Responsibility |
| --- | --- |
| `src/Settings.cs` | Atomic preferences, fixed local folders/documents and limited diagnostics |
| `web/i18n.js`, `web/locales.js`, `web/settings.js` | Bilingual messages and settings; themes use semantic CSS colors |
| `src/Program.cs` | Loopback HTTP service, launch options, transport and request validation |
| `src/Library.cs` | Local queries, profile validation, background downloads, resumption and activation |
| `src/Focus.cs` | Champion priorities, validated two-direction caching and catalog reuse |
| `src/HeroUpdates.cs` | Manual champion-update queue and task state |
| `src/Storage.cs` | Retention, expired staged downloads and storage reporting |
| `src/LocalIO.cs` | Long-path file I/O; callers retain ownership and reparse-point validation |
| `src/DesktopWindow.cs` | WinForms/WebView2, tray, close policy and application readiness |
| `src/AppUpdate.cs`, `src/UpdateTrust.cs` | Application updates, resumable downloads and publisher public key |
| `src/Deployment.cs` | Independent maintenance, manifests, transaction recovery and uninstall |
| `src/Space.cs` | Background audit, quarantine and categorized storage measurement |
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
| `--offline` | Prohibit external updates; retain local reads |
| `--no-auto-update` | Skip the startup check while allowing manual updates |
| `--no-browser` | Run the local service without opening a browser |
| `--port <number>` | Choose a starting port between 1024 and 65000; try up to 29 subsequent ports if occupied |

The EXE's default starting port is 17653. The provided offline launcher additionally selects 17683. Exit an existing normal service before testing `--no-auto-update`: reopening the same instance does not replace its startup configuration.

For manual offline testing after a normal root build:

```powershell
.\Luna.exe --offline --no-browser --port 18783
```

The service writes its actual URL to `cache/running-url.txt`. Open that local URL to inspect the interface. Use the in-app **退出 (Exit)** button when finished; closing a tab alone leaves the idle timeout in effect.

## Existing tests

### Complete local verification

Run the same entry point used by the prepared Windows workflow:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
```

This creates a fresh `dist/checks/<unique-id>/` directory with an isolated source snapshot, a `report.json`, a readable `summary.md`, per-step logs, packages and extracted test copies. It preserves the working executable, icons and personal cache. Node.js must be on PATH, or pass `-NodePath <absolute-node.exe-path>`. To choose the output directory, use `-OutputDirectory .\dist\my-check`; it must be new and under this source tree's `dist` directory. Existing results are never overwritten.

The sequence is JavaScript and repository consistency checks → runner failure/timeout checks → build → all nine C# suites → offline HTTP → packaging and inventory/hash checks → full offline HTTP against the extracted Windows ZIP → rebuild from the extracted Source ZIP. Each HTTP pass compares all bundled default-profile records and verifies request boundaries, settings, documents and assets. The runner stops on failure, uses bounded waits, stops only the processes it starts, and preserves logs for inspection. Temporary fixture files remain under the check directory's `temp` folder.

The source package's `.github/workflows/windows-checks.yml` uses `windows-2022`, Windows PowerShell and Node.js 24. It is prepared for branch pushes, pull requests and manual runs, with read-only repository permissions, pinned official actions and no artifact upload or Release step. Its setup can download Node.js/actions; Luna tests use local fixtures and offline services, without contacting data providers or using personal credentials. Development configuration is excluded from the Windows package. [GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [runner image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md).

Check the repository Actions page for the result of each GitHub-hosted run. Local results are recorded separately. Checksums confirm content integrity; human review of language, themes and layout remains part of release review. Successful technical checks do not resolve third-party distribution conditions.

For layout changes, also open an isolated offline query in a real browser and check English/Simplified Chinese, light/dark themes, and widths 360, 640, 720, 721, 800, 900, 1000, 1001 and 1440 CSS pixels. Keep all rank groups open to include conjugate labels and long champion titles. Import `inspectLayout` from `tests/browser-smoke.mjs` into the browser automation session and evaluate it in the page after each change to measure overlap and card boundaries, then inspect screenshots. This browser check complements the automated workflow and is not run by the Node UI simulation. In particular, 721–1000 pixels must not be skipped: an old negative metric margin caused overlapping text there.

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

Use a **new empty output directory**. The default `dist/v1.6.3/` may already contain local artifacts; the following example chooses a separate directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -OutputDirectory .\dist\v1.6.3-review
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-package.ps1 -ReleaseDirectory .\dist\v1.6.3-review
```

Choose another unused directory for a repeat run. Packaging refuses to mix with existing files.

| Output | Contents |
| --- | --- |
| `Luna-v1.6.3-Windows.zip` | Rebuilt EXE, runtime files, bundled data, both READMEs and documentation |
| `Luna-v1.6.3-Source.zip` | Source, scripts, tests, data and documentation; no EXE |
| `SHA256SUMS.txt` | SHA-256 values for both ZIPs and the manifest |
| `release-files.json` | Every archive entry's path, size and hash |
| `_build/` | Intermediate local build, not a download attachment |

The script derives the version from `src/AssemblyInfo.cs` and checks it against `release-status.json`. It rebuilds instead of copying the root executable and verifies the resulting version and author.

Both ZIPs have a `Luna/` prefix and include the license, notices and review status. Explicit roots/extensions exclude caches, research, personal shortcuts and common secret-file formats. **Both packages currently include third-party data and assets.** Packaging success does not change `publicReleaseReady` or create a GitHub repository/Release.

The package check verifies hashes, file lists, path safety, release metadata and runtime/source separation. Source packages also include `.gitattributes`, issue forms, the workflow and the unified check scripts; runtime packages exclude development configuration. `repository.test.cjs` checks current version/author consistency, primary project links, and referenced local document/image files. Human review should also cover language switches, anchors and layout; ZIP integrity does not prove that navigation is correct.

## Runtime acceptance

Before handing a package to users, follow the [release checklist](RELEASE_CHECKLIST.md). Use one selected Windows ZIP for a fresh extraction and another extraction for cache migration. Check ordinary-user launch, search and filters, language/themes, offline About/diagnostics, startup-update preferences, Exit and relaunch. Use test-created settings and fixture data rather than a personal cache.

Windows display scaling and browser zoom are separate controls. Check real Windows scaling at 125% and 150% with browser zoom at 100%; record the observed pixel ratio, language/theme, viewport and screenshots. Restore the original display setting afterwards. Changing only the browser width or zoom does not count as a Windows scaling check. Document separately any OS/browser combination or clean-machine environment that was not available.

Use the release inventory and SHA-256 to distinguish local builds sharing a version number. The [QA record](QA-1.6.3.md) records completed checks; the [release draft](RELEASE-v1.6.3.md) describes the selected build. Run relevant checks again when the code or package contents change; do not silently replace an already verified ZIP.

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
| [Changelog](../CHANGELOG.md) | Versioned changes, including an Unreleased section |
| [Repository details](REPOSITORY_SETUP.md) | About description, project URL and topics |
| [Release draft](RELEASE-v1.6.3.md) | Future release description and attachment list |
| [Release checklist](RELEASE_CHECKLIST.md) | Source/upload boundaries, runtime acceptance and outstanding publication items |
| [QA record](QA-1.6.3.md) | Dated validation results and their limits |
| [Third-party review](THIRD_PARTY_REVIEW.md) | Distribution evidence and unresolved items |

Keep the two language editions in step when changing user-visible behavior. The product is **Luna**, author **FrancisChen**, repository **Francis-Chenyj/Luna-dev**; reference-project links belong in README acknowledgements.

For a future application version, update versioned source/UI expectations and release metadata together; the HTTP and package checks derive the expected release version from metadata. Tooling/documentation-only work stays under **Unreleased** unless a new version is intentionally prepared. Issue forms and the Windows workflow are locally prepared; GitHub activation and actual Release publication remain separate authorized actions.

## Desktop host / 桌面窗口 (1.6.3)

`src/DesktopWindow.cs` owns the WinForms/WebView2 window and NotifyIcon. HTTP runs on a dedicated thread; query and update workers are unchanged. Close choices are stored as `closeAction: ask|tray|exit` in schema-1 preferences (missing field defaults to ask). Explicit Exit and Windows session end bypass the preference. `/api/window/close` and `/api/window/show` require the same POST, X-Luna, loopback Host and Origin checks as other mutations. Do not use them from external origins.

自有窗口在主 STA 线程运行，本地 HTTP 使用独立线程。托盘保持后台任务；退出取消更新、关闭监听并释放托盘和 WebView2。标题栏写入不再依赖可能不受支持的 DWM 属性读取。Windows 10 兼容性及高对比度仍应在发布前实机验证。

`scripts/desktop-dependencies.ps1` verifies and copies the pinned Microsoft SDK from `vendor/webview2`; builds do not fetch packages. Both runtime and source ZIPs include the required dependencies and Microsoft license. The installed Evergreen Runtime remains separate. Native navigation stays on this Luna origin; user-selected HTTP(S) external links open in the default browser. No remote debugging port or host-object bridge is exposed.

WebView2 uses `cache/webview2` and clears only DiskCache/CacheStorage asynchronously after an idle delay, at most once daily. Never delete the complete profile to clean caches: it contains the recent-query localStorage. Tests run in private folders and headless HTTP does not initialize WebView2.


See [Application lifecycle / 程序维护](APPLICATION_LIFECYCLE.md) for signing, installation, updates, rollback and uninstall.

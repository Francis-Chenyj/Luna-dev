[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releasePath = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $releasePath 'release-files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema -ne 1 -or @($manifest.packages).Count -ne 2) { throw 'Expected a version-1 two-package manifest.' }
if ($manifest.version -notmatch '^\d+\.\d+\.\d+$' -or [string]::IsNullOrWhiteSpace($manifest.author)) { throw 'Invalid release identity.' }
if (@($manifest.packages.kind | Sort-Object -Unique).Count -ne 2) { throw 'Expected distinct Windows and Source packages.' }
$sumNames = @{}
foreach ($line in (Get-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt') -Encoding UTF8)) {
    if ($line -notmatch '^([a-f0-9]{64})  ([^/\\]+)$') { throw 'Malformed checksum line.' }
    $hash = $Matches[1]; $name = $Matches[2]
    if ($sumNames.ContainsKey($name)) { throw 'Duplicate checksum filename.' }
    $sumNames[$name] = $true
    if ((Get-FileHash -LiteralPath (Join-Path $releasePath $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $name" }
}
if ($sumNames.Count -ne 3 -or -not $sumNames.ContainsKey('release-files.json')) { throw 'Checksum list must cover both ZIPs and the manifest.' }
$results = @()
foreach ($package in $manifest.packages) {
    if ($package.name -ne ('Luna-v' + $manifest.version + '-' + $package.kind + '.zip') -or $package.kind -notin @('Windows','Source')) { throw 'Unexpected package name/kind.' }
    if (-not $sumNames.ContainsKey($package.name)) { throw 'A ZIP is missing from the checksum list.' }
    $zipPath = Join-Path $releasePath $package.name
    if ((Get-Item -LiteralPath $zipPath).Length -ne $package.bytes -or (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) { throw 'ZIP does not match its manifest.' }
    $expected = @{}
    foreach ($file in $package.files) {
        if ($expected.ContainsKey($file.path)) { throw 'Duplicate manifest path.' }
        $expected[$file.path] = $file
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            $path = $entry.FullName
            if (-not $path.StartsWith('Luna/') -or $path.Contains('\') -or $path -match '(^|/)\.\.?(/|$)|:|(^|/)(cache|research|dist|node_modules|\.git|__pycache__|\.vs)(/|$)|\.(lnk|log|tmp|pfx|p12|pem|key)$|(^|/)\.env($|\.)') { throw "Unexpected or unsafe package path: $path" }
            if ($seen.ContainsKey($path) -or -not $expected.ContainsKey($path)) { throw "Duplicate/unlisted entry: $path" }
            $seen[$path] = $true
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose(); $stream.Dispose() }
            if ($entry.Length -ne $expected[$path].bytes -or $hash -ne $expected[$path].sha256) { throw "Entry integrity failure: $path" }
        }
        if ($seen.Count -ne $expected.Count) { throw 'Some manifest entries are missing from the ZIP.' }
        foreach ($required in @('LICENSE','README.md','README.zh-CN.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md','release-status.json','Luna.exe.config','docs/USER_GUIDE.md','docs/THIRD_PARTY_REVIEW.md',('docs/QA-' + $manifest.version + '.md'),'data/library/1-5/manifest.json','web/index.html','web/i18n.js','web/locales.js','web/settings.js','scripts/create-shortcut.ps1')) {
            if (-not $seen.ContainsKey('Luna/' + $required)) { throw "Missing package requirement: $required" }
        }
        $reader = [IO.StreamReader]::new($zip.GetEntry('Luna/release-status.json').Open(), [Text.Encoding]::UTF8)
        try { $status = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($status.version -ne $manifest.version -or $status.author -ne $manifest.author -or $status.publicReleaseReady -ne $manifest.publicReleaseReady -or $status.distributionStatus -ne $manifest.distributionStatus) { throw 'Packaged release metadata differs from the manifest.' }
        if ($package.kind -eq 'Windows') {
            if (-not $seen.ContainsKey('Luna/Luna.exe') -or @($seen.Keys | Where-Object { $_ -match '^Luna/(src|tests|\.github)/' }).Count -gt 0) { throw 'Runtime package must contain the EXE but no development source/tests/workflows.' }
        } else {
            if ($seen.ContainsKey('Luna/Luna.exe') -or -not $seen.ContainsKey('Luna/build.ps1') -or -not $seen.ContainsKey('Luna/src/Program.cs') -or -not $seen.ContainsKey('Luna/tests/run-package.ps1')) { throw 'Source package has the wrong build/test contents.' }
            foreach ($required in @('.gitattributes','.github/ISSUE_TEMPLATE/bug-report.yml','.github/ISSUE_TEMPLATE/feature-request.yml','.github/ISSUE_TEMPLATE/config.yml','.github/workflows/windows-checks.yml','scripts/check.ps1','scripts/check-tools.ps1','tests/run-check-tools.ps1','tests/run-http.ps1','tests/repository.test.cjs','tests/layout-probe.js')) {
                if (-not $seen.ContainsKey('Luna/' + $required)) { throw "Source package is missing verification files: $required" }
            }
        }
        $results += [pscustomobject]@{ package = $package.name; entries = $seen.Count; sha256 = $package.sha256; passed = $true }
    } finally { $zip.Dispose() }
}
$results | ConvertTo-Json -Depth 3

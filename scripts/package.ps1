[CmdletBinding()]
param([string]$OutputDirectory,[string]$SigningCertificateThumbprint,[string]$TimestampServer)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'hash-file.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$signingCertificate = $null
if ($SigningCertificateThumbprint -or $TimestampServer) {
    if ($SigningCertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Supply the exact code-signing certificate thumbprint from CurrentUser/My.' }
    $timestampUri = $null
    if (-not [Uri]::TryCreate($TimestampServer,[UriKind]::Absolute,[ref]$timestampUri) -or $timestampUri.Scheme -ne 'https') { throw 'Signing requires an explicit HTTPS timestamp service URL.' }
    $signingCertificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $SigningCertificateThumbprint)
    $codeSigningUsage = @($signingCertificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | Where-Object Value -eq '1.3.6.1.5.5.7.3.3')
    if (-not $signingCertificate.HasPrivateKey -or $codeSigningUsage.Count -eq 0) { throw 'The certificate must have a private key and Code Signing usage.' }
}
$projectPath = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$assembly = Get-Content -LiteralPath (Join-Path $projectPath 'src\AssemblyInfo.cs') -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assembly, 'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success) { throw 'A semantic release version is required in AssemblyInfo.cs.' }
$version = $versionMatch.Groups[1].Value
$status = Get-Content -LiteralPath (Join-Path $projectPath 'release-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'check-release-readiness.ps1')
if ($status.version -ne $version) { throw 'release-status.json and AssemblyInfo.cs versions differ.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectPath ('dist\v' + $version) }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory).TrimEnd('\')
if ($projectPath -eq $outputPath -or $projectPath.StartsWith($outputPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must not be the project directory or one of its parents.'
}
foreach ($inputFolder in @('.github','src','web','data','scripts','tests','docs','vendor','cache')) {
    $inputPath = Join-Path $projectPath $inputFolder
    if ($outputPath -eq $inputPath -or $outputPath.StartsWith($inputPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Do not put package output inside $inputFolder. Use dist or another empty directory."
    }
}
if (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container) -or @(Get-ChildItem -LiteralPath $outputPath -Force).Count -ne 0) {
        throw 'Use a new empty output directory to avoid mixing or overwriting releases.'
    }
}
function Assert-RegularPath([string]$Path) {
    $current = $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Package paths must not traverse symlinks or junctions: $current"
            }
        }
        $current = Split-Path -Parent $current
    }
}
Assert-RegularPath $outputPath
Assert-RegularPath $projectPath

# Explicit roots and extensions keep caches, local shortcuts and secrets out.
$rootFiles = @('README.md','README.zh-CN.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md','release-status.json',
    'Luna.exe.config','启动Luna.cmd','离线启动Luna.cmd','创建Luna桌面快捷方式.cmd','安装Luna.cmd','卸载Luna.cmd')
$extensions = @{
    '.github' = @('.yml','.yaml')
    src = @('.cs','.manifest'); web = @('.html','.css','.js','.svg','.ico','.png')
    vendor = @('.dll','.txt','.json','.md'); data = @('.json','.gz','.png'); scripts = @('.ps1','.py','.mjs')
    tests = @('.cs','.ps1','.js','.cjs','.mjs','.json'); docs = @('.md','.png','.jpg','.jpeg','.json')
}
foreach ($relative in ($rootFiles + @('build.ps1','.gitignore','.gitattributes','scripts\create-shortcut.ps1'))) {
    $file = Join-Path $projectPath $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing required package file: $relative" }
    Assert-RegularPath $file
}
foreach ($folder in $extensions.Keys) {
    $folderPath = Join-Path $projectPath $folder
    Assert-RegularPath $folderPath
    foreach ($item in (Get-ChildItem -LiteralPath $folderPath -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Package input contains a link: $($item.FullName)" }
    }
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$buildPath = Join-Path $outputPath '_build'
# Always build from the current source; never copy the possibly stale root EXE.
& (Join-Path $projectPath 'build.ps1') -OutputDirectory $buildPath
$exePath = Join-Path $buildPath 'Luna.exe'
if ($signingCertificate) {
    foreach ($name in @('Luna.exe','Luna.Maintenance.exe')) {
        $signature = Set-AuthenticodeSignature -LiteralPath (Join-Path $buildPath $name) -Certificate $signingCertificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        if ($signature.Status -ne 'Valid') { throw ('Publisher signing failed: ' + $name + ' / ' + $signature.Status) }
    }
}
$exeInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
if ($exeInfo.ProductVersion -ne $version -or $exeInfo.CompanyName -ne $status.author) {
    throw 'The newly built executable does not match release version/author metadata.'
}
function Entry([string]$Relative, [string]$Source = '') {
    if (-not $Source) { $Source = Join-Path $projectPath $Relative }
    [pscustomobject]@{ Path = ('Luna/' + $Relative.Replace('\','/')); Source = $Source }
}
function Folder-Entries([string]$Folder) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $projectPath $Folder) -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($projectPath.Length + 1).Replace('\','/')
        if ($relative -match '(^|/)(cache|research|dist|node_modules|__pycache__|\.git|\.vs)(/|$)') { continue }
        if ($file.Extension -notin $extensions[$Folder]) { continue }
        Entry $relative $file.FullName
    }
}
$common = @($rootFiles | ForEach-Object { Entry $_ }) + @(Folder-Entries 'web') + @(Folder-Entries 'data') + @(Folder-Entries 'docs')
$desktopFiles = @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','runtimes/win-x64/native/WebView2Loader.dll','runtimes/win-x86/native/WebView2Loader.dll','runtimes/win-arm64/native/WebView2Loader.dll')
$desktopEntries = @($desktopFiles | ForEach-Object { Entry $_ (Join-Path $buildPath $_) }) + @(Entry 'vendor/webview2/LICENSE.txt') + @(Entry 'vendor/webview2/README.md') + @(Entry 'vendor/webview2/manifest.json')
$sourceEntries = $common + @(Folder-Entries 'vendor') + @(Entry 'build.ps1') + @(Entry '.gitignore') + @(Entry '.gitattributes') + @(Entry 'CONTRIBUTING.md') + @(Entry 'SECURITY.md') + @(Folder-Entries '.github') + @(Folder-Entries 'src') + @(Folder-Entries 'scripts') + @(Folder-Entries 'tests')
# Identify every shipped source file, including previously untracked modules. Do not
# imply that compiler timestamps or signed artifacts are bit-for-bit reproducible.
$sourceEntries += @(Entry 'package.json') + @(Entry 'package-lock.json')
$sourceRecords = @($sourceEntries | Where-Object Path -ne 'Luna/docs/build-provenance.json' | Sort-Object Path | ForEach-Object { [ordered]@{path=$_.Path.Substring(5);sha256=(Get-LunaFileHash $_.Source)} })
$canonical = ($sourceRecords | ForEach-Object { $_.sha256 + '  ' + $_.path }) -join "`n"
$sourceHasher = [Security.Cryptography.SHA256]::Create()
try { $sourceId = [BitConverter]::ToString($sourceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-','').ToLowerInvariant() } finally { $sourceHasher.Dispose() }
$baseCommit = $null
if (Test-Path -LiteralPath (Join-Path $projectPath '.git')) { $baseCommit = (& git -C $projectPath rev-parse HEAD 2>$null | Select-Object -First 1) }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
$provenance = [ordered]@{schema=1;version=$version;sourceId=$sourceId;baseCommit=$baseCommit;identity='sourceId identifies the exact shipped source; baseCommit alone does not';compiler=[Diagnostics.FileVersionInfo]::GetVersionInfo($compilerPath).FileVersion;files=$sourceRecords}
$provenancePath = Join-Path $buildPath 'build-provenance.json'
[IO.File]::WriteAllText($provenancePath,($provenance | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$provenanceEntry = Entry 'docs/build-provenance.json' $provenancePath
$definitions = @(
    @{ Kind = 'Windows'; Entries = @($common | Where-Object Path -ne 'Luna/docs/build-provenance.json') + $desktopEntries + @($provenanceEntry) + @(Entry 'Luna.exe' $exePath) + @(Entry 'Luna.Maintenance.exe' (Join-Path $buildPath 'Luna.Maintenance.exe')) + @(Entry 'scripts/create-shortcut.ps1') },
    @{ Kind = 'Source'; Entries = @($sourceEntries | Where-Object Path -ne 'Luna/docs/build-provenance.json') + @($provenanceEntry) }
)
$packages = @()
foreach ($definition in $definitions) {
    $name = 'Luna-v' + $version + '-' + $definition.Kind + '.zip'
    $zipPath = Join-Path $outputPath $name
    $entries = @($definition.Entries | Sort-Object Path)
    if ($definition.Kind -eq 'Windows') {
        $ownedFiles = @($entries | ForEach-Object { [ordered]@{path=$_.Path.Substring(5);bytes=(Get-Item -LiteralPath $_.Source).Length;sha256=(Get-LunaFileHash $_.Source)} })
        $ownedManifest = [ordered]@{owner='Luna.Runtime.1';version=$version;files=$ownedFiles}
        $ownedPath = Join-Path $buildPath 'installed-files.json'
        [IO.File]::WriteAllText($ownedPath, ($ownedManifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        $entries += Entry 'installed-files.json' $ownedPath
    }
    $seen = @{}
    $records = @()
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            if ($seen.ContainsKey($entry.Path)) { throw "Duplicate archive path: $($entry.Path)" }
            $seen[$entry.Path] = $true
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Source, $entry.Path, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $records += [pscustomobject]@{ path = $entry.Path; bytes = (Get-Item -LiteralPath $entry.Source).Length; sha256 = (Get-LunaFileHash $entry.Source) }
        }
    } finally { $zip.Dispose() }
    $packages += [pscustomobject]@{ name = $name; kind = $definition.Kind; bytes = (Get-Item -LiteralPath $zipPath).Length; sha256 = (Get-LunaFileHash $zipPath); files = $records }
}
$manifest = [ordered]@{ schema = 1; version = $version; author = $status.author; distributionStatus = $status.distributionStatus; publicReleaseReady = $status.publicReleaseReady; publisherSignatureApplied = [bool]$signingCertificate; generatedAt = [DateTime]::UtcNow.ToString('o'); packages = $packages }
$utf8 = [Text.UTF8Encoding]::new($false)
$manifestPath = Join-Path $outputPath 'release-files.json'
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + "`n"), $utf8)
$sums = @($packages | ForEach-Object { $_.sha256 + '  ' + $_.name })
$sums += (Get-LunaFileHash $manifestPath) + '  release-files.json'
[IO.File]::WriteAllText((Join-Path $outputPath 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), $utf8)
Write-Output ('Packaged Luna ' + $version + ' / ' + $status.distributionStatus)
if (-not $status.publicReleaseReady) { Write-Output 'Third-party distribution review remains open: see docs/THIRD_PARTY_REVIEW.md.' }
Get-ChildItem -LiteralPath $outputPath -File | Select-Object Name,Length

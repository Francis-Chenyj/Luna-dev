[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectPath = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$assembly = Get-Content -LiteralPath (Join-Path $projectPath 'src\AssemblyInfo.cs') -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assembly, 'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success) { throw 'A semantic release version is required in AssemblyInfo.cs.' }
$version = $versionMatch.Groups[1].Value
$status = Get-Content -LiteralPath (Join-Path $projectPath 'release-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($status.version -ne $version) { throw 'release-status.json and AssemblyInfo.cs versions differ.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectPath ('dist\v' + $version) }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory).TrimEnd('\')
if ($projectPath -eq $outputPath -or $projectPath.StartsWith($outputPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must not be the project directory or one of its parents.'
}
foreach ($inputFolder in @('.github','src','web','data','scripts','tests','docs','cache')) {
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
    'Luna.exe.config','启动Luna.cmd','离线启动Luna.cmd','创建Luna桌面快捷方式.cmd')
$extensions = @{
    '.github' = @('.yml','.yaml')
    src = @('.cs','.manifest'); web = @('.html','.css','.js','.svg','.ico','.png')
    data = @('.json','.gz','.png'); scripts = @('.ps1','.py','.mjs')
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
$definitions = @(
    @{ Kind = 'Windows'; Entries = $common + @(Entry 'Luna.exe' $exePath) + @(Entry 'scripts/create-shortcut.ps1') },
    @{ Kind = 'Source'; Entries = $common + @(Entry 'build.ps1') + @(Entry '.gitignore') + @(Entry '.gitattributes') + @(Folder-Entries '.github') + @(Folder-Entries 'src') + @(Folder-Entries 'scripts') + @(Folder-Entries 'tests') }
)
$packages = @()
foreach ($definition in $definitions) {
    $name = 'Luna-v' + $version + '-' + $definition.Kind + '.zip'
    $zipPath = Join-Path $outputPath $name
    $entries = @($definition.Entries | Sort-Object Path)
    $seen = @{}
    $records = @()
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            if ($seen.ContainsKey($entry.Path)) { throw "Duplicate archive path: $($entry.Path)" }
            $seen[$entry.Path] = $true
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Source, $entry.Path, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $records += [pscustomobject]@{ path = $entry.Path; bytes = (Get-Item -LiteralPath $entry.Source).Length; sha256 = (Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    } finally { $zip.Dispose() }
    $packages += [pscustomobject]@{ name = $name; kind = $definition.Kind; bytes = (Get-Item -LiteralPath $zipPath).Length; sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); files = $records }
}
$manifest = [ordered]@{ schema = 1; version = $version; author = $status.author; distributionStatus = $status.distributionStatus; publicReleaseReady = $status.publicReleaseReady; generatedAt = [DateTime]::UtcNow.ToString('o'); packages = $packages }
$utf8 = [Text.UTF8Encoding]::new($false)
$manifestPath = Join-Path $outputPath 'release-files.json'
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + "`n"), $utf8)
$sums = @($packages | ForEach-Object { $_.sha256 + '  ' + $_.name })
$sums += (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  release-files.json'
[IO.File]::WriteAllText((Join-Path $outputPath 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), $utf8)
Write-Output ('Packaged Luna ' + $version + ' / ' + $status.distributionStatus)
if (-not $status.publicReleaseReady) { Write-Output 'Third-party distribution review remains open: see docs/THIRD_PARTY_REVIEW.md.' }
Get-ChildItem -LiteralPath $outputPath -File | Select-Object Name,Length

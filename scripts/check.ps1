[CmdletBinding()]
param([string]$OutputDirectory, [string]$NodePath = 'node', [switch]$Browser, [string]$BrowserChannel)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'check-tools.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectPath = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$node = Resolve-LunaApplication $NodePath
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not [Environment]::Is64BitOperatingSystem -or -not (Test-Path -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'))) {
    throw 'The complete suite requires 64-bit Windows and the .NET Framework compiler.'
}
$nodeVersion = (& $node --version).Trim()
if ($LASTEXITCODE -ne 0 -or [int]$nodeVersion.TrimStart('v').Split('.')[0] -lt 18) { throw 'Node.js 18 or later is required; CI uses Node.js 24.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectPath ('dist\checks\' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory).TrimEnd('\')
if (-not $outputPath.StartsWith((Join-Path $projectPath 'dist') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Keep verification output in a new directory under this source tree''s dist folder.' }
Assert-LunaRegularPath $projectPath
Assert-LunaRegularPath $outputPath
if (Test-Path -LiteralPath $outputPath) { throw 'Verification output already exists. Choose a new directory; existing results are never overwritten.' }
$sourcePath = Join-Path $outputPath 'source'
$logPath = Join-Path $outputPath 'logs'
$tempPath = Join-Path $outputPath 'temp'
New-Item -ItemType Directory -Path $sourcePath,$logPath,$tempPath -Force | Out-Null
$startedAt = [DateTime]::UtcNow
$results = [Collections.Generic.List[object]]::new()
$status = Get-Content -LiteralPath (Join-Path $projectPath 'release-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$failure = $null
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
function Run-Check([string]$Name,[string]$Executable,[string[]]$ProcessArguments,[string]$Directory = $sourcePath,[int]$Seconds = 300) {
    Write-Host "Checking: $Name"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        Invoke-LunaCheckProcess -FilePath $Executable -Arguments $ProcessArguments -WorkingDirectory $Directory `
            -LogPrefix (Join-Path $logPath $Name) -TimeoutSeconds $Seconds
        $results.Add([pscustomobject]@{name=$Name;passed=$true;seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2)})
    } catch {
        $results.Add([pscustomobject]@{name=$Name;passed=$false;seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2)})
        throw
    }
}
function PowerShell-Arguments([string]$Script,[string[]]$Extra = @()) {
    @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$Script) + $Extra
}
try {
    # A private source snapshot prevents icon builds, tests and preferences from changing the working app.
    $rootFiles = @('build.ps1','.gitignore','.gitattributes','README.md','README.zh-CN.md','CHANGELOG.md','LICENSE',
        'THIRD_PARTY_NOTICES.md','release-status.json','CONTRIBUTING.md','SECURITY.md','package.json','package-lock.json','Luna.exe.config','启动Luna.cmd','离线启动Luna.cmd','创建Luna桌面快捷方式.cmd','安装Luna.cmd','卸载Luna.cmd')
    foreach ($relative in $rootFiles) { Assert-LunaRegularPath (Join-Path $projectPath $relative); Copy-Item -LiteralPath (Join-Path $projectPath $relative) -Destination $sourcePath }
    foreach ($folder in @('.github','src','web','data','scripts','tests','docs','vendor')) {
        $inputPath = Join-Path $projectPath $folder
        Assert-LunaRegularPath $inputPath
        foreach ($item in (Get-ChildItem -LiteralPath $inputPath -Recurse -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Verification input contains a link: $($item.FullName)" }
        }
        Copy-Item -LiteralPath $inputPath -Destination $sourcePath -Recurse
    }
    $env:TEMP = $tempPath
    $env:TMP = $tempPath
    Run-Check 'javascript' $node @('--test','tests\core.test.cjs','tests\ui.test.cjs','tests\i18n.test.cjs','tests\repository.test.cjs')
    Run-Check 'check-tools' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'tests\run-check-tools.ps1') @('-NodePath',$node))
    Run-Check 'build' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'build.ps1'))
    foreach ($suite in @('settings','stability','storage','auto-update','focus','hero-update','icons','longpath','lifecycle','http-transport','service-isolation')) {
        Run-Check ('csharp-' + $suite) $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath ('tests\run-' + $suite + '.ps1')))
    }
    Run-Check 'offline-http' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'tests\run-http.ps1') @('-RuntimeDirectory',$sourcePath,'-LogDirectory',(Join-Path $logPath 'offline'),'-NodePath',$node))
    $packagesPath = Join-Path $outputPath 'packages'
    Run-Check 'package' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'scripts\package.ps1') @('-OutputDirectory',$packagesPath))
    Run-Check 'package-integrity' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'tests\run-package.ps1') @('-ReleaseDirectory',$packagesPath))
    $runtimeExtract = Join-Path $outputPath 'extracted-runtime'
    $sourceExtract = Join-Path $outputPath 'extracted-source'
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $packagesPath ('Luna-v' + $status.version + '-Windows.zip')),$runtimeExtract)
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $packagesPath ('Luna-v' + $status.version + '-Source.zip')),$sourceExtract)
    Run-Check 'packaged-offline-http' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'tests\run-http.ps1') @('-RuntimeDirectory',(Join-Path $runtimeExtract 'Luna'),'-LogDirectory',(Join-Path $logPath 'packaged-offline'),'-NodePath',$node))
    Run-Check 'runtime-smoke' $windowsPowerShell (PowerShell-Arguments (Join-Path $sourcePath 'tests\run-runtime-smoke.ps1') @('-RuntimeDirectory',(Join-Path $runtimeExtract 'Luna'),'-OutputDirectory',(Join-Path $outputPath 'runtime-smoke')))
    $unpackedSource = Join-Path $sourceExtract 'Luna'
    Run-Check 'source-package-build' $windowsPowerShell (PowerShell-Arguments (Join-Path $unpackedSource 'build.ps1')) $unpackedSource
    if ($Browser) {
        $browserArguments = @((Join-Path $projectPath 'tests\browser-runner.mjs'),'--runtime',(Join-Path $runtimeExtract 'Luna'),'--output',(Join-Path $outputPath 'browser'))
        if ($BrowserChannel) { $browserArguments += @('--channel',$BrowserChannel) }
        Run-Check 'browser' $node $browserArguments $projectPath 240
    }
} catch { $failure = $_ }
finally {
    $env:TEMP = $originalTemp
    $env:TMP = $originalTmp
    $report = [ordered]@{schema=1;version=$status.version;passed=($null -eq $failure);startedAt=$startedAt.ToString('o');finishedAt=[DateTime]::UtcNow.ToString('o');node=$nodeVersion;windows=[Environment]::OSVersion.VersionString;publicReleaseReady=$status.publicReleaseReady;checks=@($results.ToArray())}
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $outputPath 'report.json'),(($report | ConvertTo-Json -Depth 5) + "`n"),$utf8)
    $summary = @('# Luna Windows verification / Windows 验证', '', ('Version: ' + $status.version), ('Result: ' + $(if ($null -eq $failure) {'PASS'} else {'FAIL'})), '', '| Check | Result | Seconds |', '| --- | --- | --- |')
    foreach ($result in $results) { $summary += '| ' + $result.name + ' | ' + $(if ($result.passed) {'PASS'} else {'FAIL'}) + ' | ' + $result.seconds + ' |' }
    $summary += @('', 'Tests use local fixtures and offline services. No upstream acquisition or publication is performed.', '测试使用本地样本和离线服务，不采集外部数据、不发布。', '', 'Logs, packages and temporary test copies remain in this verification directory.', '日志、程序包与临时测试副本保留在本次验证目录中。', '', ('Distribution review unchanged; publicReleaseReady = ' + $status.publicReleaseReady.ToString().ToLowerInvariant() + '.'), 'GitHub-hosted execution and human UI review require separate verification.')
    [IO.File]::WriteAllText((Join-Path $outputPath 'summary.md'),(($summary -join "`n") + "`n"),$utf8)
    Write-Host ('Verification results: ' + $outputPath)
}
if ($failure) { throw $failure }
Write-Host 'PASS: Windows build, regression, offline HTTP and package verification.'

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-settings-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testPath 'SettingsTests.exe'
$sources = @('DesktopIcons.cs','Program.cs','Library.cs','Focus.cs','Storage.cs','HeroUpdates.cs','Settings.cs') | ForEach-Object { Join-Path $projectPath ('src\' + $_) }
& $compiler '/nologo' '/target:exe' '/main:SettingsTests' '/utf8output' '/codepage:65001' "/out:$testExe" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' $sources (Join-Path $PSScriptRoot 'SettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settings test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Settings checks failed' }

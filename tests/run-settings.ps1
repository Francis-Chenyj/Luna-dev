$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-settings-tests-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testPath 'SettingsTests.exe'
$sources = @('DesktopIcons.cs','Program.cs','Library.cs','Storage.cs','Settings.cs') | ForEach-Object { Join-Path $projectPath ('src\' + $_) }
. (Join-Path $projectPath "scripts/desktop-dependencies.ps1") -ProjectPath $projectPath -OutputPath $testPath
& $compiler $desktopReferences "/win32manifest:$projectPath\src\app.manifest" '/nologo' '/target:exe' '/main:SettingsTests' '/utf8output' '/codepage:65001' "/out:$testExe" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' $sources (Join-Path $PSScriptRoot 'SettingsTests.cs') (Join-Path $PSScriptRoot 'TestApplication.cs') $desktopSource
if ($LASTEXITCODE -ne 0) { throw 'Settings test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Settings checks failed' }

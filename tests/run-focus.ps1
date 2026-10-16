param([switch]$Live)
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-focus-tests-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
New-Item -ItemType Directory -Path (Join-Path $testPath 'data\library') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'data\library\1-5') -Destination (Join-Path $testPath 'data\library') -Recurse
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testPath 'FocusTests.exe'
. (Join-Path $projectPath "scripts/desktop-dependencies.ps1") -ProjectPath $projectPath -OutputPath $testPath
& $compiler $desktopReferences "/win32manifest:$projectPath\src\app.manifest" '/nologo' '/target:exe' '/main:FocusTests' '/utf8output' "/out:$testExe" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' (Join-Path $projectPath 'src\DesktopIcons.cs') (Join-Path $projectPath 'src\Settings.cs') (Join-Path $projectPath 'src\Program.cs') (Join-Path $projectPath 'src\Library.cs') (Join-Path $projectPath 'src\Storage.cs') (Join-Path $PSScriptRoot 'FocusTests.cs') (Join-Path $PSScriptRoot 'TestApplication.cs') $desktopSource
if ($LASTEXITCODE -ne 0) { throw 'Focused update test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
if ($Live) { & $testExe '--live' } else { & $testExe }
if ($LASTEXITCODE -ne 0) { throw 'Focused update checks failed' }

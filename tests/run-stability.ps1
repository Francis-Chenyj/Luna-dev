$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([System.IO.Path]::GetTempPath()) ('luna-tests-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testPath 'StabilityTests.exe'
. (Join-Path $projectPath "scripts/desktop-dependencies.ps1") -ProjectPath $projectPath -OutputPath $testPath
& $compiler $desktopReferences "/win32manifest:$projectPath\src\app.manifest" '/nologo' '/target:exe' '/main:StabilityTests' '/utf8output' "/out:$testExe" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' (Join-Path $projectPath 'src\DesktopIcons.cs') (Join-Path $projectPath 'src\Settings.cs') (Join-Path $projectPath 'src\Program.cs') (Join-Path $projectPath 'src\Library.cs') (Join-Path $projectPath 'src\Storage.cs') (Join-Path $PSScriptRoot 'StabilityTests.cs') (Join-Path $PSScriptRoot 'TestClock.cs') $desktopSource
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Stability checks failed' }

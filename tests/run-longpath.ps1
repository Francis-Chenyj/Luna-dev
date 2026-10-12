$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-longpath-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $testPath 'data\library') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'data\library\1-5') -Destination (Join-Path $testPath 'data\library') -Recurse
$testExe = Join-Path $testPath 'LongPathTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler '/nologo' '/target:exe' '/main:LongPathTests' '/utf8output' "/win32manifest:$projectPath\src\app.manifest" "/out:$testExe" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' (Join-Path $projectPath 'src\DesktopIcons.cs') (Join-Path $projectPath 'src\Settings.cs') (Join-Path $projectPath 'src\Program.cs') (Join-Path $projectPath 'src\Library.cs') (Join-Path $projectPath 'src\Focus.cs') (Join-Path $projectPath 'src\Storage.cs') (Join-Path $projectPath 'src\HeroUpdates.cs') (Join-Path $PSScriptRoot 'LongPathTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Long-path test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Long-path checks failed' }




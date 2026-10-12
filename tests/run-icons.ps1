$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-icons-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testPath 'IconTests.exe'
& $compiler '/nologo' '/target:exe' "/out:$testExe" '/reference:System.Drawing.dll' (Join-Path $PSScriptRoot 'IconTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon test build failed' }
& $testExe (Join-Path $projectPath 'web\app-1.4.1.ico')
if ($LASTEXITCODE -ne 0) { throw 'Native icon checks failed' }

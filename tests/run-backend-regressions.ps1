param([string]$Case = '')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-backend-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
. (Join-Path $projectPath 'scripts/desktop-dependencies.ps1') -ProjectPath $projectPath -OutputPath $testPath
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $testPath 'BackendRegressionTests.exe'
& $compiler $desktopReferences '/nologo' '/target:exe' '/main:BackendRegressionTests' '/utf8output' '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' "/out:$testExe" $sources (Join-Path $PSScriptRoot 'BackendRegressionTests.cs') (Join-Path $PSScriptRoot 'TestClock.cs')
if ($LASTEXITCODE -ne 0) { throw 'Backend regression test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe $Case
if ($LASTEXITCODE -ne 0) { throw 'Backend regression checks failed' }

$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$testPath=Join-Path ([IO.Path]::GetTempPath()) ('luna-http-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testExe=Join-Path $testPath 'HttpTransportTests.exe'
& $compiler /nologo /target:exe /utf8output "/out:$testExe" /reference:System.Core.dll (Join-Path $projectPath 'src/LocalHttp.cs') (Join-Path $PSScriptRoot 'HttpTransportTests.cs')
if($LASTEXITCODE -ne 0){throw 'HTTP transport test build failed'}
& $testExe
if($LASTEXITCODE -ne 0){throw 'HTTP transport tests failed'}

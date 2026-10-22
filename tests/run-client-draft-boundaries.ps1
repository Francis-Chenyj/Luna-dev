param()
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-client-boundaries-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
$testExe = Join-Path $testPath 'ClientDraftBoundaryTests.exe'
& $compiler '/nologo' '/target:exe' '/main:ClientDraftBoundaryTests' '/utf8output' '/reference:System.Core.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Management.dll' '/reference:System.Security.dll' "/out:$testExe" (Join-Path $projectPath 'src/ClientDraft.cs') (Join-Path $PSScriptRoot 'ClientDraftBoundaryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Client draft boundary test build failed' }
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe + '.config')
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Client draft boundary checks failed' }

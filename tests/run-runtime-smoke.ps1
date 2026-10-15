[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$Desktop)
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$outputPath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if(Test-Path -LiteralPath $outputPath){throw 'Use a new smoke output directory.'}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$testExe=Join-Path $outputPath 'RuntimeSmoke.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /main:RuntimeSmoke /utf8output "/out:$testExe" "/win32manifest:$projectPath/src/app.manifest" /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll (Join-Path $PSScriptRoot 'RuntimeSmoke.cs') (Join-Path $projectPath 'src/LocalIO.cs') (Join-Path $projectPath 'src/Deployment.cs') (Join-Path $projectPath 'src/UpdateTrust.cs') (Join-Path $projectPath 'src/AssemblyInfo.cs')
if($LASTEXITCODE -ne 0){throw 'Runtime smoke build failed'}
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe+'.config')
$arguments=@($RuntimeDirectory,(Join-Path $outputPath 'fixtures'))
if($Desktop){$arguments+='--desktop'}
& $testExe @arguments
if($LASTEXITCODE -ne 0){throw 'Runtime smoke failed'}

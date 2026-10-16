[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$NodePath='node')
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new verification directory'}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$fixture=Join-Path $output 'controlled-runtime';$normal=Join-Path $output 'actual-runtime'
Copy-Item -LiteralPath $runtime -Destination $fixture -Recurse
Copy-Item -LiteralPath $runtime -Destination $normal -Recurse
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
. (Join-Path $projectPath 'scripts/desktop-dependencies.ps1') -ProjectPath $projectPath -OutputPath $fixture
$sources=@(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler $desktopReferences "/win32manifest:$projectPath\src\app.manifest" '/nologo' '/target:winexe' '/main:SessionHarness' '/utf8output' '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' ('/out:'+(Join-Path $fixture 'SessionHarness.exe')) $sources (Join-Path $PSScriptRoot 'SessionHarness.cs') (Join-Path $PSScriptRoot 'TestApplication.cs')
if($LASTEXITCODE -ne 0){throw 'Session harness build failed'}
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination (Join-Path $fixture 'SessionHarness.exe.config')
& $NodePath (Join-Path $PSScriptRoot 'short-sessions.mjs') $normal $fixture $output
if($LASTEXITCODE -ne 0){throw 'Short-session verification failed'}

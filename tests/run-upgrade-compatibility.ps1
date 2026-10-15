[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PreviousRuntimeDirectory,[Parameter(Mandatory=$true)][string]$ReleaseDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
. (Join-Path $projectPath 'scripts/hash-file.ps1')
$outputPath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if(Test-Path -LiteralPath $outputPath){throw 'Use a new compatibility output directory'}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$release=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'release-files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$package=@($release.packages | Where-Object kind -eq 'Windows')[0]
$archive=Join-Path $ReleaseDirectory $package.name
$utf8=[Text.UTF8Encoding]::new($false)
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.PersistKeyInCsp=$false
try {
    $body=[ordered]@{schema=1;product='Luna';version=$release.version;bytes=(Get-Item -LiteralPath $archive).Length;sha256=(Get-LunaFileHash $archive);url=('https://github.com/Francis-Chenyj/Luna-dev/releases/download/v'+$release.version+'/'+$package.name)}
    $payload=$utf8.GetBytes(($body | ConvertTo-Json -Compress))
    $envelope=[ordered]@{payload=[Convert]::ToBase64String($payload);signature=[Convert]::ToBase64String($rsa.SignData($payload,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))}
    [IO.File]::WriteAllText((Join-Path $outputPath 'envelope.json'),($envelope | ConvertTo-Json),$utf8)
    [IO.File]::WriteAllText((Join-Path $outputPath 'TestTrust.cs'),('namespace Luna { internal static class UpdateTrust { internal const string PublicKey = @"'+$rsa.ToXmlString($false)+'"; } }'),$utf8)
} finally {$rsa.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testExe=Join-Path $outputPath 'UpgradeCompatibility.exe'
& $compiler /nologo /target:exe /main:UpgradeCompatibility /utf8output "/out:$testExe" "/win32manifest:$projectPath/src/app.manifest" /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll (Join-Path $PSScriptRoot 'UpgradeCompatibility.cs') (Join-Path $projectPath 'src/LocalIO.cs') (Join-Path $projectPath 'src/Deployment.cs') (Join-Path $outputPath 'TestTrust.cs') (Join-Path $projectPath 'src/AssemblyInfo.cs')
if($LASTEXITCODE -ne 0){throw 'Compatibility harness build failed'}
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe+'.config')
& $testExe $PreviousRuntimeDirectory $archive (Join-Path $outputPath 'envelope.json') (Join-Path $outputPath 'fixtures')
if($LASTEXITCODE -ne 0){throw 'Compatibility tests failed'}

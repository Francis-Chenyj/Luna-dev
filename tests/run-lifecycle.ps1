$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$testPath=Join-Path ([IO.Path]::GetTempPath()) ('luna-lifecycle-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path (Join-Path $testPath 'data/library') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'data/library/1-5') -Destination (Join-Path $testPath 'data/library') -Recurse
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
. (Join-Path $projectPath 'scripts/desktop-dependencies.ps1') -ProjectPath $projectPath -OutputPath $testPath
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.PersistKeyInCsp=$false
try {
 [IO.File]::WriteAllText((Join-Path $testPath 'test-private.xml'),$rsa.ToXmlString($true))
 [IO.File]::WriteAllText((Join-Path $testPath 'TestTrust.cs'),('namespace Luna { internal static class UpdateTrust { internal const string PublicKey = @"'+$rsa.ToXmlString($false)+'"; } }'))
} finally { $rsa.Dispose() }
$sourceFiles=@(Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | Where-Object Name -ne 'UpdateTrust.cs' | ForEach-Object FullName)
$testExe=Join-Path $testPath 'LifecycleTests.exe'
& $compiler $desktopReferences "/win32manifest:$projectPath\src\app.manifest" '/nologo' '/target:exe' '/main:LifecycleTests' '/utf8output' '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' "/out:$testExe" $sourceFiles (Join-Path $testPath 'TestTrust.cs') (Join-Path $PSScriptRoot 'LifecycleTests.cs') (Join-Path $PSScriptRoot 'TestApplication.cs')
if($LASTEXITCODE -ne 0){throw 'Lifecycle test build failed'}
Copy-Item -LiteralPath (Join-Path $projectPath 'Luna.exe.config') -Destination ($testExe+'.config')
& $testExe
if($LASTEXITCODE -ne 0){throw 'Lifecycle tests failed'}

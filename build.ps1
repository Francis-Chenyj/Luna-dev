param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& (Join-Path $PSScriptRoot 'scripts\build-icon.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
& $compiler '/nologo' '/target:winexe' '/optimize+' '/utf8output' '/codepage:65001' "/out:$OutputDirectory\Luna.exe" "/win32icon:$PSScriptRoot\web\app.ico" "/win32manifest:$PSScriptRoot\src\app.manifest" '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' "$PSScriptRoot\src\DesktopIcons.cs" "$PSScriptRoot\src\Settings.cs" "$PSScriptRoot\src\Program.cs" "$PSScriptRoot\src\Library.cs" "$PSScriptRoot\src\Focus.cs" "$PSScriptRoot\src\Storage.cs" "$PSScriptRoot\src\HeroUpdates.cs" "$PSScriptRoot\src\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ([IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') -ne [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Luna.exe.config') -Destination $OutputDirectory -Force
}
Write-Output 'Built Luna.exe'

[CmdletBinding()]
param([string]$DestinationDirectory = [Environment]::GetFolderPath('DesktopDirectory'))
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$desktopPath = (Resolve-Path -LiteralPath $DestinationDirectory).Path
$shellObject = New-Object -ComObject WScript.Shell
$shortcut = $shellObject.CreateShortcut((Join-Path $desktopPath 'Luna.lnk'))
$shortcut.TargetPath = Join-Path $projectPath 'Luna.exe'
$shortcut.WorkingDirectory = $projectPath
$shortcut.IconLocation = (Join-Path $projectPath 'web\app-1.4.1.ico') + ',0'
$shortcut.Description = 'Luna 1.5.0 - FrancisChen'
$shortcut.Save()
Write-Output 'Desktop shortcut created.'

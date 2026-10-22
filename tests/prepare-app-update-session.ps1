[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[int]$DelayMilliseconds=150)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot '../scripts/hash-file.ps1')
$project=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new empty fixture directory'}
$release=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'release-files.json') -Raw | ConvertFrom-Json
$sourceVersion=[regex]::Match([IO.File]::ReadAllText((Join-Path $project 'src/Settings.cs')),'const string AppVersion\s*=\s*"([^"]+)"').Groups[1].Value
if($release.version -ne $sourceVersion){throw 'Use the matching current release package for this updater fixture'}
$currentVersion=[Version]$release.version
if($currentVersion.Build -gt 0){$predecessorVersion=[Version]::new($currentVersion.Major,$currentVersion.Minor,$currentVersion.Build-1).ToString()}
elseif($currentVersion.Minor -gt 0){$predecessorVersion=[Version]::new($currentVersion.Major,$currentVersion.Minor-1,0).ToString()}
elseif($currentVersion.Major -gt 0){$predecessorVersion=[Version]::new($currentVersion.Major-1,0,0).ToString()}
else{throw 'The release must have a lower version for update-session fixtures'}
$package=@($release.packages | Where-Object kind -eq 'Windows')[0]
$archive=Join-Path $ReleaseDirectory $package.name
New-Item -ItemType Directory -Path $output | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($archive,$output)
$runtime=Join-Path $output 'Luna'
$fixtureSources=Join-Path $output 'fixture-source'
New-Item -ItemType Directory -Path $fixtureSources | Out-Null
# Exercise this revision's updater from a lower fixture version. This is not a
# claim that an unmodified historical executable already has the new behavior.
$utf8=[Text.UTF8Encoding]::new($false)
foreach($name in @('Settings.cs','AssemblyInfo.cs')){
 $text=[IO.File]::ReadAllText((Join-Path $project ('src/'+$name))).Replace($release.version,$predecessorVersion)
 [IO.File]::WriteAllText((Join-Path $fixtureSources $name),$text,$utf8)
}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
. (Join-Path $project 'scripts/desktop-dependencies.ps1') -ProjectPath $project -OutputPath $runtime
$sources=@(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | Where-Object Name -notin @('Settings.cs','AssemblyInfo.cs') | ForEach-Object FullName)
& $compiler $desktopReferences '/nologo' '/target:winexe' '/main:AppUpdateSession' '/optimize+' '/utf8output' '/reference:System.Web.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Core.dll' ('/win32icon:'+(Join-Path $project 'web/app.ico')) ('/win32manifest:'+(Join-Path $project 'src/app.manifest')) ('/out:'+(Join-Path $runtime 'Luna.exe')) $sources (Join-Path $fixtureSources 'Settings.cs') (Join-Path $fixtureSources 'AssemblyInfo.cs') (Join-Path $PSScriptRoot 'AppUpdateSession.cs')
if($LASTEXITCODE -ne 0){throw 'Update fixture build failed'}
$index=Join-Path $runtime 'web/index.html'
[IO.File]::WriteAllText($index,[IO.File]::ReadAllText($index).Replace($release.version,$predecessorVersion),$utf8)
$manifest=Get-Content -LiteralPath (Join-Path $runtime 'installed-files.json') -Raw | ConvertFrom-Json
$manifest.version=$predecessorVersion
foreach($row in $manifest.files){if($row.path -in @('Luna.exe','web/index.html')){$file=Join-Path $runtime $row.path;$row.bytes=(Get-Item -LiteralPath $file).Length;$row.sha256=Get-LunaFileHash $file}}
[IO.File]::WriteAllText((Join-Path $runtime 'installed-files.json'),($manifest|ConvertTo-Json -Depth 6),$utf8)
New-Item -ItemType Directory -Path (Join-Path $runtime 'cache') | Out-Null
[IO.File]::WriteAllText((Join-Path $runtime 'cache/settings.json'),' {"schema":1,"language":"zh-CN","theme":"light","autoUpdate":false,"appAutoCheck":false,"appAutoDownload":false,"closeAction":"exit"}',$utf8)
[IO.File]::WriteAllText((Join-Path $runtime 'personal-note.txt'),'Retain this unowned file.',$utf8)
$config=[ordered]@{archive=$archive;metadata=(Join-Path $ReleaseDirectory 'luna-update.json');metadataUrl=('https://github.com/Francis-Chenyj/Luna-dev/releases/download/v'+$release.version+'/luna-update.json');delayMs=$DelayMilliseconds;predecessorVersion=$predecessorVersion}
[IO.File]::WriteAllText((Join-Path $runtime 'fixture.json'),($config|ConvertTo-Json),$utf8)
Write-Output $runtime

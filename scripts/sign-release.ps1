[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,[Parameter(Mandatory=$true)][string]$PrivateKeyPath,[string]$Notes='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'hash-file.ps1')
$manifest=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'release-files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$package=@($manifest.packages | Where-Object kind -eq 'Windows')[0]
$zip=Join-Path $ReleaseDirectory $package.name
if ((Get-LunaFileHash $zip) -ne $package.sha256) { throw 'Package checksum mismatch' }
$keyFull=[IO.Path]::GetFullPath($PrivateKeyPath)
$projectFull=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
if ($keyFull.StartsWith($projectFull+'\',[StringComparison]::OrdinalIgnoreCase) -or $keyFull.StartsWith([IO.Path]::GetFullPath($ReleaseDirectory).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the signing private key outside source and release folders.' }
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp=$false
try {
  $rsa.FromXmlString([IO.File]::ReadAllText($keyFull))
  $trust=Get-Content -LiteralPath (Join-Path $projectFull 'src\UpdateTrust.cs') -Raw
  if (-not $trust.Contains($rsa.ToXmlString($false))) { throw 'Private key does not match the embedded public verification key.' }
  $body=[ordered]@{schema=1;product='Luna';version=$manifest.version;bytes=$package.bytes;sha256=$package.sha256;url=('https://github.com/Francis-Chenyj/Luna-dev/releases/download/v'+$manifest.version+'/'+$package.name);notes=$Notes}
  $bytes=[Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Compress))
  $signature=$rsa.SignData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
  $envelope=[ordered]@{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}
  [IO.File]::WriteAllText((Join-Path $ReleaseDirectory 'luna-update.json'),($envelope|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
} finally { $rsa.Dispose() }
Write-Output 'Created signed update metadata. No upload or publication performed.'

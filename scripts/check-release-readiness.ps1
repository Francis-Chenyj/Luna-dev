[CmdletBinding()]
param([switch]$ForPublication,[string]$RuntimeDirectory)
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$status=Get-Content -LiteralPath (Join-Path $projectPath 'release-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$evidence=Get-Content -LiteralPath (Join-Path $projectPath 'docs/THIRD_PARTY_EVIDENCE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($evidence.schema -ne 1 -or @($evidence.items).Count -ne 3){throw 'Invalid third-party evidence inventory.'}
$pending=@($evidence.items | Where-Object status -ne 'verified')
foreach($item in $evidence.items){
    if($item.status -notin @('pending','verified')){throw 'Unknown evidence status.'}
    if($item.status -eq 'verified'){
        if($item.evidence -notmatch '^docs/evidence/[a-zA-Z0-9_/-]+\.(md|json|pdf|txt)$' -or $item.evidence.Contains('..')){throw 'Verified evidence needs a repository evidence record.'}
        if(-not(Test-Path -LiteralPath (Join-Path $projectPath $item.evidence) -PathType Leaf)){throw 'Referenced evidence is missing.'}
    }
}
if($status.publicReleaseReady -and $pending.Count){throw 'Public readiness cannot be true while evidence is pending.'}
if($ForPublication){
    if(-not $status.publicReleaseReady -or $pending.Count){throw 'Publication remains blocked by the recorded third-party evidence review.'}
    if(-not $RuntimeDirectory){throw 'Provide the exact runtime directory to verify publisher signatures.'}
    foreach($name in @('Luna.exe','Luna.Maintenance.exe')){
        $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $RuntimeDirectory $name)
        if($signature.Status -ne 'Valid'){throw "Publisher signature is not valid: $name. Sign before packaging, then regenerate and verify ownership/checksum manifests."}
    }
}
Write-Output ('Evidence inventory valid; unresolved items: '+$pending.Count+'. Public readiness: '+$status.publicReleaseReady)

[CmdletBinding()]
param([string]$NodePath = 'node')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\check-tools.ps1')
$node = Resolve-LunaApplication $NodePath
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('luna-check-tools-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testPath | Out-Null
$firstToolPath = Join-Path $testPath 'first installation'
$secondToolPath = Join-Path $testPath 'second installation'
New-Item -ItemType Directory -Path $firstToolPath,$secondToolPath | Out-Null
$toolName = 'luna-resolution-' + [guid]::NewGuid().ToString('N') + '.exe'
$firstTool = Join-Path $firstToolPath $toolName
$secondTool = Join-Path $secondToolPath $toolName
New-Item -ItemType File -Path $firstTool,$secondTool | Out-Null
$originalPath = $env:PATH
try {
    $env:PATH = $firstToolPath + ';' + $secondToolPath + ';' + $originalPath
    if (@(Get-Command $toolName -CommandType Application).Count -ne 2) { throw 'Duplicate-PATH fixture is invalid.' }
    if ((Resolve-LunaApplication $toolName) -cne $firstTool) { throw 'Executable resolution did not preserve PATH priority.' }
    if ((Resolve-LunaApplication $secondTool) -cne $secondTool) { throw 'Explicit executable path was not respected.' }
} finally { $env:PATH = $originalPath }
Write-Output 'PASS: duplicate executable installations preserve PATH priority and explicit paths.'
$values = @('two words','a"b','C:\trailing space\','')
Invoke-LunaCheckProcess -FilePath $node -Arguments (@('-e','process.stdout.write(JSON.stringify(process.argv.slice(1)))') + $values) `
    -WorkingDirectory $testPath -LogPrefix (Join-Path $testPath 'arguments')
$actual = Get-Content -LiteralPath (Join-Path $testPath 'arguments.stdout.log') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($actual.Count -ne $values.Count) { throw 'Native argument count changed.' }
for ($i=0; $i -lt $values.Count; $i++) { if ($actual[$i] -cne $values[$i]) { throw "Native argument $i changed." } }
Write-Output 'PASS: native arguments preserve spaces, quotes, trailing slashes and empty strings.'
$failedAsExpected = $false
try {
    Invoke-LunaCheckProcess -FilePath $node -Arguments @('-e','process.exit(17)') -WorkingDirectory $testPath -LogPrefix (Join-Path $testPath 'failure')
} catch {
    if ($_.Exception.Message -notmatch 'code 17') { throw }
    $failedAsExpected = $true
}
if (-not $failedAsExpected) { throw 'A failing child process was reported as successful.' }
Write-Output 'PASS: a nonzero exit fails verification.'
$marker = Join-Path $testPath 'child.json'
$childScript = "const c=require('node:child_process').spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{windowsHide:true});require('node:fs').writeFileSync(process.argv[1],JSON.stringify({pid:c.pid}));setInterval(()=>{},1000);"
$timedOut = $false
try {
    Invoke-LunaCheckProcess -FilePath $node -Arguments @('-e',$childScript,$marker) -WorkingDirectory $testPath -LogPrefix (Join-Path $testPath 'timeout') -TimeoutSeconds 3
} catch {
    if ($_.Exception.Message -notmatch 'timed out') { throw }
    $timedOut = $true
}
if (-not $timedOut) { throw 'A hanging process was not stopped on timeout.' }
if (-not (Test-Path -LiteralPath $marker)) { throw 'Timeout fixture did not start its child.' }
$childId = (Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json).pid
$remaining = Get-Process -Id $childId -ErrorAction SilentlyContinue
if ($remaining) {
    try { Stop-LunaCheckProcess $remaining } finally { $remaining.Dispose() }
    throw 'Timeout left a child process running.'
}
Write-Output 'PASS: timeout is a failure and stops the owned process tree.'

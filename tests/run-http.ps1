[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$LogDirectory,
    [string]$NodePath = 'node',
    [ValidateRange(1,120)][int]$StartupTimeoutSeconds = 30,
    [ValidateRange(1,600)][int]$TestTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\check-tools.ps1')
$runtimePath = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
Assert-LunaRegularPath $runtimePath
# Never launch tests against someone's installed instance or personal cache.
if (Test-Path -LiteralPath (Join-Path $runtimePath 'cache')) { throw 'HTTP verification requires a fresh isolated runtime with no cache directory.' }
foreach ($required in @('Luna.exe','Luna.exe.config','web\index.html','data\library\1-5\manifest.json','LICENSE','CHANGELOG.md','THIRD_PARTY_NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimePath $required) -PathType Leaf)) { throw "Missing runtime file: $required" }
}
$node = Resolve-LunaApplication $NodePath
$logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogDirectory)
Assert-LunaRegularPath $logPath
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
$portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
try { $portProbe.Start(); $port = $portProbe.LocalEndpoint.Port } finally { $portProbe.Stop() }
if ($port -gt 65000) { $port = 18783 }
$process = Start-Process -FilePath (Join-Path $runtimePath 'Luna.exe') `
    -ArgumentList '--offline','--no-browser','--port',$port -WorkingDirectory $runtimePath -WindowStyle Hidden -PassThru
$null = $process.Handle
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $addressPath = Join-Path $runtimePath 'cache\running-url.txt'
    $ready = $false
    $baseUrl = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "The isolated Luna process exited before becoming ready (code $($process.ExitCode))." }
        if (Test-Path -LiteralPath $addressPath) {
            $baseUrl = (Get-Content -LiteralPath $addressPath -Raw -Encoding ASCII).Trim()
            if ($baseUrl -notmatch '^http://127\.0\.0\.1:([0-9]{4,5})$') { throw 'The isolated runtime wrote an invalid loopback address.' }
            $actualPort = [int]$Matches[1]
            if ($actualPort -lt $port -or $actualPort -gt ($port + 29)) { throw 'The service selected a port outside its launch range.' }
            try {
                $request = [Net.HttpWebRequest]::Create($baseUrl + '/api/health')
                $request.Proxy = $null
                $request.Timeout = 1000
                $request.ReadWriteTimeout = 1000
                $request.Headers.Add('X-Luna','1')
                $response = $request.GetResponse()
                try {
                    $reader = [IO.StreamReader]::new($response.GetResponseStream())
                    try { $health = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                    $ready = $health.app -eq 'luna-local' -and $health.offline -eq $true
                } finally { $response.Dispose() }
                if ($ready) { break }
            } catch [Net.WebException] { }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw "The isolated offline service was not ready within $StartupTimeoutSeconds seconds." }
    Invoke-LunaCheckProcess -FilePath $node -Arguments @((Join-Path $PSScriptRoot 'server.mjs'),$baseUrl,'--offline') `
        -WorkingDirectory $runtimePath -LogPrefix (Join-Path $logPath 'http') -TimeoutSeconds $TestTimeoutSeconds
    Get-Content -LiteralPath (Join-Path $logPath 'http.stdout.log') -Raw -Encoding UTF8 | Write-Output
} finally {
    try { Stop-LunaCheckProcess $process } finally { $process.Dispose() }
}

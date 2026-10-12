# Shared by local verification and its regression checks. No network setup or downloads.
Set-StrictMode -Version Latest

function Assert-LunaRegularPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Verification paths must not traverse links: $current"
            }
        }
        $current = Split-Path -Parent $current
    }
}

function ConvertTo-LunaProcessArgument([string]$Value) {
    # Windows native argument quoting, including embedded quotes and trailing slashes.
    return '"' + (($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

function Stop-LunaCheckProcess([Diagnostics.Process]$Process) {
    $Process.Refresh()
    if ($Process.HasExited) { return }
    # Only target the process object this check launched, including its children.
    $stopInfo = [Diagnostics.ProcessStartInfo]::new()
    $stopInfo.FileName = Join-Path $env:WINDIR 'System32\taskkill.exe'
    $stopInfo.Arguments = '/PID ' + $Process.Id + ' /T /F'
    $stopInfo.UseShellExecute = $false
    $stopInfo.CreateNoWindow = $true
    $stopInfo.RedirectStandardOutput = $true
    $stopInfo.RedirectStandardError = $true
    $stopper = [Diagnostics.Process]::Start($stopInfo)
    try {
        if (-not $stopper.WaitForExit(10000)) { $stopper.Kill() }
    } finally { $stopper.Dispose() }
    if (-not $Process.WaitForExit(5000)) { throw 'A verification child process could not be stopped.' }
}

function Invoke-LunaCheckProcess {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory=$true)][string]$WorkingDirectory,
        [Parameter(Mandatory=$true)][string]$LogPrefix,
        [ValidateRange(1,1800)][int]$TimeoutSeconds = 300
    )
    $stdout = $LogPrefix + '.stdout.log'
    $stderr = $LogPrefix + '.stderr.log'
    New-Item -ItemType Directory -Path (Split-Path -Parent $LogPrefix) -Force | Out-Null
    $quoted = @($Arguments | ForEach-Object { ConvertTo-LunaProcessArgument $_ }) -join ' '
    $process = Start-Process -FilePath $FilePath -ArgumentList $quoted -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Verification timed out after $TimeoutSeconds seconds. Logs: $LogPrefix"
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Verification process exited with code $($process.ExitCode). Logs: $LogPrefix"
        }
    } catch {
        foreach ($log in @($stdout,$stderr)) {
            if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 35 -Encoding UTF8 | ForEach-Object { Write-Host $_ } }
        }
        throw
    } finally {
        try { Stop-LunaCheckProcess $process } finally { $process.Dispose() }
    }
}

# Keep release tooling independent of PowerShell module auto-loading.
function Get-LunaFileHash([string]$LiteralPath) {
    $stream = [IO.File]::OpenRead($LiteralPath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

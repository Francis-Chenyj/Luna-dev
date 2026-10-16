# Shared compiler inputs. SDK files are pinned in vendor; builds need no network.
param([Parameter(Mandatory=$true)][string]$ProjectPath, [Parameter(Mandatory=$true)][string]$OutputPath)
$vendorPath = Join-Path $ProjectPath 'vendor\webview2'
$sdkManifest = Get-Content -LiteralPath (Join-Path $vendorPath 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($file in $sdkManifest.files) {
    if ($file.path -match '(^|/)\.\.(/|$)|^/|:|\\') { throw 'Invalid SDK manifest path' }
    $stream = [IO.File]::OpenRead((Join-Path $vendorPath $file.path)); $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose(); $stream.Dispose() }
    if ($hash -ne $file.sha256) { throw ('WebView2 SDK checksum mismatch: ' + $file.path) }
}
$desktopReferences = @('/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:Microsoft.CSharp.dll')
foreach ($assembly in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')) {
    $source = Join-Path $vendorPath ('lib\net462\' + $assembly)
    $desktopReferences += '/reference:' + $source
    Copy-Item -LiteralPath $source -Destination (Join-Path $OutputPath $assembly) -Force
}
Copy-Item -LiteralPath (Join-Path $vendorPath 'runtimes') -Destination $OutputPath -Recurse -Force
$desktopSource = @('LocalIO.cs','LocalHttp.cs','DesktopWindow.cs','Space.cs','AppUpdate.cs','Deployment.cs','UpdateTrust.cs' | ForEach-Object { Join-Path $ProjectPath ('src\' + $_) })

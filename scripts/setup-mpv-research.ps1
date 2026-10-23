$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $projectRoot '.deps\mpv-7c4f662c0'
$url = 'https://github.com/mpv-player/mpv/releases/download/git-release/mpv-v0.41.0-dev-g7c4f662c0-38598545310-x86_64-pc-windows-msvc.zip'
$archiveHash = '58060D3F140AE35C6578BD3606DD13E3A5A5EEF2F78B025C2FD790CA70E1C98C'
$executableHash = '82BD066203AA8997164D9BB8F6555DA37D905D1BD3CF50A8D7FFDDB589684EC4'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$archivePath = Join-Path $folder 'mpv-package.zip'
if (-not (Test-Path -LiteralPath $archivePath)) { Invoke-WebRequest -Uri $url -OutFile $archivePath }
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $archiveHash) { throw 'Pinned archive SHA-256 mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($name in @('mpv.exe','vulkan-1.dll')) {
        $entry = $archive.GetEntry($name)
        if (-not $entry) { throw "Missing expected archive entry $name" }
        $destination = Join-Path $folder $name
        if (-not (Test-Path -LiteralPath $destination)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination) }
    }
} finally { $archive.Dispose() }
if ((Get-FileHash -LiteralPath (Join-Path $folder 'mpv.exe') -Algorithm SHA256).Hash -ne $executableHash) { throw 'Pinned executable SHA-256 mismatch.' }
$executableHash | Set-Content -LiteralPath (Join-Path $folder 'mpv.sha256') -Encoding ascii
[ordered]@{ source='https://github.com/mpv-player/mpv/releases/tag/git-release'; asset=$url; archiveSha256=$archiveHash; executableSha256=$executableHash; registrationScriptsExecuted=$false; extracted=@('mpv.exe','vulkan-1.dll') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'package.json') -Encoding utf8
Write-Output "Pinned portable mpv ready: $folder (no installation or registration)"

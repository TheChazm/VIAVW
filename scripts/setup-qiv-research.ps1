$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $projectRoot '.deps\qiv-3.2.0.452'
$url = 'https://github.com/icyhoty2k/QuickImageViewer/releases/download/v3.2.0.452/QuickImageViewer.exe'
$expectedHash = '852F10EFFE6A99C96B73EFD1720C56749C8AAA659C7057DA72F9D833AA0E5CD4'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$executable = Join-Path $folder 'QuickImageViewer.exe'
if (-not (Test-Path -LiteralPath $executable)) { Invoke-WebRequest -Uri $url -OutFile $executable }
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Pinned qIV executable SHA-256 mismatch.' }
$expectedHash | Set-Content -LiteralPath (Join-Path $folder 'qiv.sha256') -Encoding ascii
[ordered]@{version='3.2.0.452';source='https://github.com/icyhoty2k/QuickImageViewer/releases/tag/v3.2.0.452';asset=$url;sha256=$expectedHash;installationPerformed=$false;note='Launch only with an explicit project-local existing INI whose Instance.Dedicated flag is 1. Ordinary portable mode registers shell settings.'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'package.json') -Encoding utf8
Write-Output "Pinned qIV ready: $folder (not launched or registered)"

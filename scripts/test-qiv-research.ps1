param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv
}
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\qiv-research-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$runs = @()
foreach ($experiment in @(
    @{ name='Ordinary hidden Direct2D image viewer and stateful native input'; operation='--research-qiv'; folder='native' },
    @{ name='Actual qIV viewer controls, modifier wheel, drag and ownership'; operation='--test-qiv-viewer'; folder='viewer' },
    @{ name='Two independent ordinary image viewers'; operation='--test-concurrent-qiv-viewers'; folder='concurrent' },
    @{ name='Real qIV launcher, duplicate guard and cleanup'; operation='--test-qiv-viewer-launcher'; folder='launcher' }
)) {
    Write-Output $experiment.name
    & $hostExe $experiment.operation (Join-Path $output $experiment.folder)
    $runs += [pscustomobject]@{name=$experiment.name;exitCode=$LASTEXITCODE;folder=$experiment.folder}
    $runs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "qIV research failed; inspect $output" }
}
Write-Output "qIV evidence: $output"

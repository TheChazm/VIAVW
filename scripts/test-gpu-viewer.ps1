param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-mpv-research.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Dynamic
}
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\gpu-viewer-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$runs = @()
foreach ($experiment in @(
    @{ name='Native dynamic GPU viewer and fixture-only scoped modifier'; operation='--test-dynamic-gpu-viewer'; folder='dynamic' },
    @{ name='Hidden GPU viewer, ownership, resize and stalled reader'; operation='--test-gpu-bridge-viewer'; folder='single' },
    @{ name='Two independent GPU viewers and source applications'; operation='--test-concurrent-gpu-viewers'; folder='concurrent' },
    @{ name='Real launcher, duplicate guard, target refusal and cleanup'; operation='--test-mpv-viewer-launcher'; folder='launcher' }
)) {
    Write-Output $experiment.name
    & $hostExe $experiment.operation (Join-Path $output $experiment.folder)
    $runs += [pscustomobject]@{ name=$experiment.name; exitCode=$LASTEXITCODE; folder=$experiment.folder }
    $runs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Experimental GPU viewer verification failed; inspect $output" }
}
Write-Output "GPU viewer evidence: $output"

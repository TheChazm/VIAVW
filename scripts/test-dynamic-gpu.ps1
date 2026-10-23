param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-mpv-research.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Dynamic }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\dynamic-gpu-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$runs = @()
foreach ($experiment in @(
    @{ name='Present1, resize, device recreation and recovery'; operation='--test-dynamic-gpu-bridge'; folder='fixture' },
    @{ name='Ordinary mpv hidden GPU capture and native input'; operation='--research-mpv-gpu'; folder='mpv' },
    @{ name='Two independent ordinary GPU applications, BGRA/RGBA'; operation='--test-concurrent-mpv-gpu'; folder='concurrent' }
)) {
    Write-Output $experiment.name
    & $hostExe $experiment.operation (Join-Path $output $experiment.folder)
    $runs += [pscustomobject]@{ name=$experiment.name; exitCode=$LASTEXITCODE; folder=$experiment.folder }
    $runs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Experiment failed; inspect $output" }
}
Write-Output "Dynamic GPU evidence: $output"

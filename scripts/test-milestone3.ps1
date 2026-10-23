param([switch]$SkipBuild, [switch]$SkipPerformance)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-native.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$probeExe = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe'
$outputDir = Join-Path $projectRoot ('artifacts\milestone3-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
function Invoke-Experiment([string]$name, [string[]]$arguments) {
    Write-Output $name
    & $hostExe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed; inspect $outputDir" }
}
Invoke-Experiment 'Native baseline regression' @('--self-test', $probeExe, (Join-Path $outputDir 'native'))
Invoke-Experiment 'Private input-state compatibility' @('--test-queue-input', (Join-Path $outputDir 'queue-input'))
Invoke-Experiment 'Private software preview shortcuts' @('--test-shortcut-viewer', 'software', (Join-Path $outputDir 'shortcut-software'))
Invoke-Experiment 'Private GPU cooperative preview shortcuts' @('--test-shortcut-viewer', 'gpu', (Join-Path $outputDir 'shortcut-gpu'))
Invoke-Experiment 'Alternative GPU presentation research' @('--test-presentation', (Join-Path $outputDir 'presentation'))
Invoke-Experiment 'Ordinary GPU browser native capture' @('--test-offscreen-browser', (Join-Path $outputDir 'ordinary-browser'))
Invoke-Experiment 'GPU browser viewer and handoff' @('--test-gpu-viewer', (Join-Path $outputDir 'gpu-viewer'))
if (-not $SkipPerformance) { Invoke-Experiment 'Input separation and activity measurements' @('--test-milestone3', (Join-Path $outputDir 'performance')) }
Write-Output "Milestone 3 evidence: $outputDir"

param([switch]$SkipBuild, [switch]$SkipVisibleLauncherTest, [switch]$SkipBenchmark)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-native.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$probeExe = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe'
$wpfExe = Join-Path $projectRoot 'src\WpfProbe\bin\Release\net10.0-windows\WpfProbe.exe'
$gpuExe = Join-Path $projectRoot 'artifacts\native-build\D3DProbe.exe'
$outputDir = Join-Path $projectRoot ('artifacts\milestone2-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
function Invoke-Experiment([string]$Name, [string[]]$Arguments)
{
    & $hostExe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name did not complete; inspect $outputDir" }
}
Invoke-Experiment 'M1 regression' @('--self-test', $probeExe, (Join-Path $outputDir 'native-regression'))
Invoke-Experiment 'Viewer regression' @('--test-viewer', $probeExe, (Join-Path $outputDir 'viewer-regression'))
Invoke-Experiment 'Save opportunity' @('--test-save', (Join-Path $outputDir 'save'))
Invoke-Experiment 'Worker recovery' @('--test-recovery', (Join-Path $outputDir 'recovery'))
Invoke-Experiment 'Hidden cooperative capture' @('--test-hidden-capture', (Join-Path $outputDir 'hidden-capture'))
if (-not $SkipVisibleLauncherTest) { Invoke-Experiment 'Visible launcher' @('--test-launcher', (Join-Path $outputDir 'launcher')) }
Invoke-Experiment 'Unmodified Win32' @('--test-win32-expanded', (Join-Path $outputDir 'win32'))
Invoke-Experiment 'WPF' @('--test-framework', 'WPF', $wpfExe, (Join-Path $outputDir 'wpf'), '--enable-grab-adapter')
Invoke-Experiment 'Qt Quick GPU' @('--test-framework', 'Quick', 'C:\Python314\python.exe', (Join-Path $outputDir 'quick-gpu'), (Join-Path $projectRoot 'experiments\quick_probe.py'), '--enable-grab-adapter')
Invoke-Experiment 'Qt Quick software configuration' @('--test-framework', 'QuickSoftware', 'C:\Python314\python.exe', (Join-Path $outputDir 'quick-software'), (Join-Path $projectRoot 'experiments\quick_probe.py'), '--enable-grab-adapter')
Invoke-Experiment 'Qt Quick software viewer handoff' @('--test-quick-viewer', 'software', (Join-Path $outputDir 'quick-software-viewer'))
Invoke-Experiment 'Qt Quick GPU cooperative viewer handoff' @('--test-quick-viewer', 'gpu', (Join-Path $outputDir 'quick-gpu-viewer'))
Invoke-Experiment 'Direct3D' @('--test-framework', 'D3D', $gpuExe, (Join-Path $outputDir 'd3d'))
Invoke-Experiment 'Chromium regression and modifier adapter' @('--test-browser', 'C:\Program Files\Google\Chrome\Application\chrome.exe', (Join-Path $projectRoot 'experiments\browser-probe.html'), (Join-Path $outputDir 'chromium'))
if (-not $SkipVisibleLauncherTest) { Invoke-Experiment 'WGC control capture' @('--capture-calibration', (Join-Path $outputDir 'wgc-control')) }
if (-not $SkipBenchmark) { Invoke-Experiment 'Four-layer benchmark' @('--benchmark', (Join-Path $outputDir 'performance')) }
Write-Output "Milestone 2 evidence: $outputDir"

param([switch]$SkipBuild, [switch]$SkipBenchmarks)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-native.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$probeExe = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe'
$outputDir = Join-Path $projectRoot ('artifacts\development-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
function Invoke-Experiment([string]$name, [string[]]$arguments) {
    Write-Output $name
    & $hostExe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed; inspect $outputDir" }
}
Invoke-Experiment 'Native regression and fault containment' @('--self-test', $probeExe, (Join-Path $outputDir 'native'))
Invoke-Experiment 'Legacy viewer regression' @('--test-viewer', $probeExe, (Join-Path $outputDir 'viewer'))
Invoke-Experiment 'Synchronous keyboard regression plus posted-input research evidence' @('--test-queue-input', (Join-Path $outputDir 'keyboard'), 'synchronous-regression')
Invoke-Experiment 'Private pointer state research' @('--test-mouse-state', (Join-Path $outputDir 'pointer'))
Invoke-Experiment 'Native private GPU viewer and handoff' @('--test-private-gpu-viewer', (Join-Path $outputDir 'quick-gpu-viewer'))
Invoke-Experiment 'Native WPF software/layered viewer and handoff' @('--test-native-wpf-viewer', (Join-Path $outputDir 'wpf-native-viewer'))
Invoke-Experiment 'Unchanged WPF process-local render adapter controls' @('--test-wpf-hook', (Join-Path $outputDir 'wpf-startup-hook'))
if (Test-Path -LiteralPath (Join-Path $projectRoot '.deps\ILSpy-10.1.0.8386-x64\app\ILSpy.exe')) {
    Invoke-Experiment 'Ordinary unchanged WPF capture and input controls' @('--research-ilspy', (Join-Path $outputDir 'ilspy'))
    Invoke-Experiment 'Ordinary ILSpy viewer, selection and ownership' @('--test-ilspy-viewer', (Join-Path $outputDir 'ilspy-viewer'))
}
Invoke-Experiment 'Ordinary Chrome native private viewer' @('--test-private-browser-viewer', (Join-Path $outputDir 'chrome-private-viewer'))
Invoke-Experiment 'Private GPU presentation, bounded input and visibility restoration' @('--test-private-browser-presentation', (Join-Path $outputDir 'chrome-presentation'))
Invoke-Experiment 'Concurrent independent private GPU input bursts' @('--test-concurrent-browser-bursts', (Join-Path $outputDir 'chrome-concurrent-bursts'))
Invoke-Experiment 'Browser activation viewer with retained selection and hidden text' @('--test-browser-activation-viewer', (Join-Path $outputDir 'activation-viewer'))
Invoke-Experiment 'Chrome hidden text, selection, Unicode and recovery' @('--test-hidden-browser-keyboard', (Join-Path $outputDir 'hidden-text-chrome'))
Invoke-Experiment 'Opt-in viewer hidden shortcut and ownership' @('--test-browser-notification-viewer', (Join-Path $outputDir 'notification-viewer'))
Invoke-Experiment 'Short-settle viewer hidden inspection and ownership' @('--test-fast-inspection-viewer', (Join-Path $outputDir 'fast-inspection-viewer'))
Invoke-Experiment 'Chrome hidden notification keyboard, pointer and activity' @('--test-notification-browser', (Join-Path $outputDir 'notification-chrome'))
if (Test-Path -LiteralPath 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe') {
    Invoke-Experiment 'Edge hidden text, selection, Unicode and recovery' @('--test-hidden-browser-keyboard', (Join-Path $outputDir 'hidden-text-edge'), 'edge')
    Invoke-Experiment 'Independent Chrome and Edge keyboard-only activation bursts' @('--test-concurrent-browser-activation', (Join-Path $outputDir 'concurrent-activation'))
    Invoke-Experiment 'Edge hidden notification keyboard, pointer and activity' @('--test-notification-browser', (Join-Path $outputDir 'notification-edge'), 'edge')
    Invoke-Experiment 'Independent hidden notification Chrome and Edge bursts' @('--test-concurrent-browser-notification', (Join-Path $outputDir 'concurrent-notification'))
    Invoke-Experiment 'Concurrent short-settle native GPU inspection' @('--test-concurrent-fast-inspection', (Join-Path $outputDir 'concurrent-fast-inspection'))
}
Invoke-Experiment 'Reusable offscreen capture viewer regression' @('--test-gpu-viewer', (Join-Path $outputDir 'gpu-viewer'))
Invoke-Experiment 'Worker recovery regression' @('--test-recovery', (Join-Path $outputDir 'recovery'))
Invoke-Experiment 'Application helper created during worker recovery' @('--test-worker-infrastructure', (Join-Path $outputDir 'worker-infrastructure'))
Invoke-Experiment 'Save opportunity regression' @('--test-save', (Join-Path $outputDir 'save'))
Invoke-Experiment 'Launcher and duplicate checks on private QA desktop' @('--test-private-launcher', (Join-Path $outputDir 'launcher'))
if (-not $SkipBenchmarks) {
    Invoke-Experiment 'Reusable capture performance, resize and stall recovery' @('--test-capture-reuse', (Join-Path $outputDir 'capture-reuse'))
    Invoke-Experiment 'Concurrent private GPU activity' @('--benchmark-private-gpu', (Join-Path $outputDir 'private-gpu-performance'))
    Invoke-Experiment 'Native WPF capture-mode activity' @('--benchmark-native-wpf', (Join-Path $outputDir 'wpf-native-performance'))
}
Write-Output "Development evidence: $outputDir"

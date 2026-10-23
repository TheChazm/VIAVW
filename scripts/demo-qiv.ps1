param([switch]$SkipBuild, [switch]$AllowDuplicate, [switch]$NativeStartup, [ValidateRange(0,500)][int]$NativeInspectionSettleMilliseconds=30, [string]$StatusFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv
}
& (Join-Path $PSScriptRoot 'demo.ps1') -SkipBuild -AllowDuplicate:$AllowDuplicate -Executable (Join-Path $projectRoot '.deps\qiv-3.2.0.452\QuickImageViewer.exe') -ExperimentalGpuBridge qiv -QivNativeStartup:$NativeStartup -InspectionSettleMilliseconds $NativeInspectionSettleMilliseconds -RootInput -QueuePointer -StatusFile $StatusFile

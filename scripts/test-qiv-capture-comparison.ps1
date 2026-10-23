param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv
}
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\qiv-capture-comparison-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$runs=@()
$snapshot = & (Join-Path $projectRoot 'artifacts\gpu-qiv-build\ModuleSnapshotTests.exe')
$snapshotExit = $LASTEXITCODE
$snapshot | Set-Content -LiteralPath (Join-Path $output 'module-snapshot-tests.json') -Encoding utf8
if($snapshotExit -ne 0 -or -not ($snapshot|ConvertFrom-Json).passed){throw "Module snapshot checks failed; inspect $output"}
$runs += [pscustomobject]@{name='Bounded module snapshot retry and handle lifetime';folder='.';exitCode=$snapshotExit}
foreach($case in @(
    @{name='Changed-image native/GPU comparison and native-only handoff';operation='--compare-qiv-capture';folder='comparison';extra=@()},
    @{name='Cold native inspection without a shared GPU frame';operation='--compare-qiv-capture';folder='cold-native';extra=@('cold-native')},
    @{name='Actual viewer capture switches, ownership, pan and recovery';operation='--test-qiv-capture-switch-viewer';folder='viewer';extra=@()},
    @{name='Concurrent mixed native/GPU viewers';operation='--test-concurrent-qiv-capture-switch';folder='concurrent';extra=@()}
)){
    Write-Output $case.name
    & $hostExe $case.operation (Join-Path $output $case.folder) @($case.extra)
    $runs += [pscustomobject]@{name=$case.name;folder=$case.folder;exitCode=$LASTEXITCODE}
    $runs|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if($LASTEXITCODE -ne 0){throw "qIV capture comparison failed; inspect $output"}
}
Write-Output "qIV capture comparison: $output"

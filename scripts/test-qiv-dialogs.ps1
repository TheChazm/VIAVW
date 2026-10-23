param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1');& (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv}
$hostExe=Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output=Join-Path $projectRoot ('artifacts\qiv-dialog-research-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output|Out-Null
$runs=@()
foreach($case in @(
    @{name='Ordinary file picker, nested error and native input';operation='--research-qiv-dialogs';folder='direct'},
    @{name='Bounded visual observation, cancellation and ownership';operation='--test-visual-readiness';folder='readiness'},
    @{name='Actual GPU-startup viewer modal workflow';operation='--test-qiv-dialog-viewer';folder='gpu-viewer'},
    @{name='Actual native-startup viewer modal workflow';operation='--test-qiv-native-dialog-viewer';folder='native-viewer'},
    @{name='Independent simultaneous ordinary file pickers';operation='--test-concurrent-qiv-dialogs';folder='concurrent'}
)){
    Write-Output $case.name
    & $hostExe $case.operation (Join-Path $output $case.folder)
    $code=$LASTEXITCODE
    $runs+=[pscustomobject]@{name=$case.name;folder=$case.folder;exitCode=$code}
    $runs|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if($code -ne 0){throw "qIV dialog research failed; inspect $output"}
}
Write-Output "qIV dialog research evidence: $output"

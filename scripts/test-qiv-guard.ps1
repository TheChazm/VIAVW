param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1');& (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv}
$hostExe=Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output=Join-Path $projectRoot ('artifacts\qiv-guard-research-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output|Out-Null
$runs=@()
foreach($case in @(
    @{name='Guard-only setup, identity refusal, native/GPU transition and recovery';operation='--research-qiv-guard';folder='guard'},
    @{name='Cache-constrained large-image freshness';operation='--research-qiv-large-images';folder='large'},
    @{name='Native-startup viewer and repeated capture switching';operation='--test-qiv-native-startup-viewer';folder='viewer'},
    @{name='Two native-startup viewers with mixed capture backends';operation='--test-concurrent-qiv-native-startup';folder='concurrent'},
    @{name='Actual native-startup launcher, duplicate refusal and cleanup';operation='--test-qiv-native-launcher';folder='launcher'}
)){
    Write-Output $case.name
    & $hostExe $case.operation (Join-Path $output $case.folder)
    $runs+=[pscustomobject]@{name=$case.name;folder=$case.folder;exitCode=$LASTEXITCODE}
    $runs|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if($LASTEXITCODE -ne 0){throw "qIV guard research failed; inspect $output"}
}
Write-Output "qIV guard research evidence: $output"

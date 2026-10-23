param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-qiv-research.ps1')
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1');& (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Qiv}
$hostExe=Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output=Join-Path $projectRoot ('artifacts\qiv-window-research-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output|Out-Null
$runs=@()
foreach($case in @(
    @{name='Bounded snapshot replacement with transient and persistent sharing conflicts';operation='--test-snapshot-publishing';folder='publishing'},
    @{name='Ordinary modeless windows and native menu state';operation='--research-qiv-windows';folder='direct'},
    @{name='GPU-startup viewer, modeless choice, native menu and scaling';operation='--test-qiv-window-viewer';folder='gpu-viewer'},
    @{name='Native-startup viewer, modeless choice, native menu and scaling';operation='--test-qiv-native-window-viewer';folder='native-viewer'},
    @{name='Two independent layers with simultaneously tracked native menus';operation='--test-concurrent-qiv-windows';folder='concurrent'}
)){
    Write-Output $case.name
    & $hostExe $case.operation (Join-Path $output $case.folder)
    $code=$LASTEXITCODE
    $runs+=[pscustomobject]@{name=$case.name;folder=$case.folder;exitCode=$code}
    $runs|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if($code -ne 0){throw "qIV window research failed; inspect $output"}
}
Write-Output "qIV window research evidence: $output"

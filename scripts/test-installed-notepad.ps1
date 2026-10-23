param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1')}
$hostExe=Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output=Join-Path $projectRoot ('artifacts\installed-notepad-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output|Out-Null
$runs=@()
foreach($case in @(
    @{operation='--test-notepad-workflow';folder='ordinary-document-workflow'},
    @{operation='--research-installed-notepad';folder='default-renderer'},
    @{operation='--research-installed-notepad-gdi';folder='gdi-renderer'},
    @{operation='--test-installed-notepad-viewer';folder='viewer'},
    @{operation='--test-concurrent-installed-notepad';folder='concurrent'}
)){
    Write-Output $case.operation
    & $hostExe $case.operation (Join-Path $output $case.folder)
    $code=$LASTEXITCODE
    $runs+=[pscustomobject]@{operation=$case.operation;folder=$case.folder;exitCode=$code}
    $runs|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if($code -ne 0){throw "Installed Notepad++ verification failed: $output"}
}
Write-Output "Installed Notepad++ evidence: $output"

param([switch]$SkipBuild, [string]$ProfileSetup = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (-not $ProfileSetup) { $ProfileSetup = Join-Path $projectRoot 'artifacts\vs-m23-software-options\launch.json' }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\visual-studio-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$runs = @()
foreach ($case in @(
    @{ name='Direct Ctrl+Tab release-state workflow'; folder='release-state'; mode='workflow-release' },
    @{ name='Direct IDE build of edited code'; folder='ide-build'; mode='workflow-build' }
)) {
    Write-Output $case.name
    & $hostExe --research-vs-private (Join-Path $output $case.folder) $ProfileSetup $case.mode
    $code = $LASTEXITCODE
    $runs += [pscustomobject]@{name=$case.name;folder=$case.folder;exitCode=$code}
    $runs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
    if ($code -ne 0) { throw "Visual Studio verification failed: $output" }
}
$viewerProject = Join-Path $output 'viewer-project'
& $hostExe --prepare-vs-private-demo $viewerProject $ProfileSetup
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the fresh viewer solution.' }
Write-Output 'Actual viewer: mouse menus/submenus, text selection, wheel, context menu, two files, IDE build, ownership handoff'
& $hostExe --test-vs-mouse-viewer (Join-Path $output 'viewer') (Join-Path $viewerProject 'launch.json')
$code = $LASTEXITCODE
$runs += [pscustomobject]@{name='Actual viewer mouse/menu and ordinary IDE workflow';folder='viewer';exitCode=$code}
$runs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'runs.json') -Encoding utf8
if ($code -ne 0) { throw "Visual Studio viewer verification failed: $output" }
Write-Output "Visual Studio evidence: $output"

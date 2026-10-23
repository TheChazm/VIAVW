param([switch]$SkipBuild, [string]$ProfileSetup = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (-not $ProfileSetup) { $ProfileSetup = Join-Path $projectRoot 'artifacts\vs-m23-software-options\launch.json' }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = Join-Path $projectRoot ('artifacts\vs-new-project-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $hostExe --prepare-vs-private-demo (Join-Path $output 'project') $ProfileSetup
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare a disposable solution.' }
& $hostExe --test-vs-new-project (Join-Path $output 'viewer') (Join-Path $output 'project\launch.json') $hostExe
if ($LASTEXITCODE -ne 0) { throw "New Project verification failed: $output" }
Write-Output "New Project evidence: $output"

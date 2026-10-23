param([switch]$SkipBuild, [string]$Python = 'C:\Python314\python.exe', [string]$Chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$probeExe = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe'
$artifactDir = Join-Path $projectRoot ('artifacts\milestone-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $hostExe --self-test $probeExe (Join-Path $artifactDir 'native')
if ($LASTEXITCODE -ne 0) { throw 'Native checks failed' }
& $hostExe --test-charmap (Join-Path $artifactDir 'charmap')
if ($LASTEXITCODE -ne 0) { throw 'Character Map checks failed' }
& $hostExe --test-viewer $probeExe (Join-Path $artifactDir 'viewer')
if ($LASTEXITCODE -ne 0) { throw 'Viewer checks failed' }
if ((Test-Path -LiteralPath $Python) -and (Test-Path -LiteralPath (Join-Path $projectRoot '.deps\python\PySide6')))
{
    & $hostExe --test-qt $Python (Join-Path $projectRoot 'experiments\qt_probe.py') (Join-Path $artifactDir 'qt')
    if ($LASTEXITCODE -ne 0) { throw 'Qt checks failed (code 2 records an input compatibility failure)' }
}
else { Write-Output 'Qt skipped: install the optional project-local runtime with scripts\setup-qt.ps1 and supply -Python if needed.' }
if (Test-Path -LiteralPath $Chrome)
{
    & $hostExe --test-browser $Chrome (Join-Path $projectRoot 'experiments\browser-probe.html') (Join-Path $artifactDir 'chromium')
    if ($LASTEXITCODE -ne 0) { throw 'Chromium checks failed' }
}
else { Write-Output 'Chromium skipped: supply -Chrome with the executable path.' }
Write-Output "Milestone results: $artifactDir"

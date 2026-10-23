param([switch]$SkipBuild, [string]$StatusFile, [switch]$VisibilityResearch, [switch]$ExperimentalHiddenInput, [ValidateRange(0,500)][int]$InspectionSettleMilliseconds=150, [ValidateSet('chrome','edge')][string]$Browser='chrome')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$profilePath = Join-Path $projectRoot ('artifacts\private-browser-demo\' + [guid]::NewGuid().ToString('N'))
$pageFile = if ($VisibilityResearch) { 'experiments\gpu-visibility.html' } else { 'experiments\gpu-browser.html' }
$pageUrl = ([Uri](Join-Path $projectRoot $pageFile)).AbsoluteUri
$browserExe = if ($Browser -eq 'edge') { 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' } else { 'C:\Program Files\Google\Chrome\Application\chrome.exe' }
& (Join-Path $PSScriptRoot 'demo.ps1') -SkipBuild:$SkipBuild -StatusFile $StatusFile -FullContent -QueuePointer -RootInput -ExperimentalHiddenInput:$ExperimentalHiddenInput -InspectionSettleMilliseconds $InspectionSettleMilliseconds -Executable $browserExe -ApplicationArguments @(('--user-data-dir=' + $profilePath), ('--app=' + $pageUrl), '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', '--disable-extensions', '--disable-component-update', '--disable-backgrounding-occluded-windows', '--window-size=800,600')

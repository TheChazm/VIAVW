param([switch]$StartupOnly)
$ErrorActionPreference='Stop'
& python (Join-Path $PSScriptRoot 'check-agent-ready.py')
if($LASTEXITCODE -ne 0){throw 'VIAVW startup checks failed. Resolve the reported prerequisite before launching applications.'}
if($StartupOnly){return}
& python -m unittest discover -s $PSScriptRoot -p 'test_viavw_*.py' -v
if($LASTEXITCODE -ne 0){throw 'VIAVW collaboration policy/transport checks failed.'}
& python (Join-Path $PSScriptRoot 'test-workspace.py')
if($LASTEXITCODE -ne 0){throw 'Installed application collaboration checks failed.'}
& (Join-Path $PSScriptRoot 'test-agent.ps1')
& (Join-Path $PSScriptRoot 'demo-collaboration.ps1') -Smoke
Write-Output 'Automated VIAVW acceptance passed. CharlieAI hookup and physical human acceptance remain separate gates.'

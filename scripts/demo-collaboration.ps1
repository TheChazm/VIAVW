param([switch]$Smoke)
$ErrorActionPreference='Stop'
& python (Join-Path $PSScriptRoot 'check-agent-ready.py')
if($LASTEXITCODE -ne 0){throw 'VIAVW startup checks failed.'}
$demoArguments=@((Join-Path $PSScriptRoot 'demo-collaboration.py'))
if($Smoke){$demoArguments+='--smoke'}
& python @demoArguments
if($LASTEXITCODE -ne 0){throw 'Collaboration demonstration did not complete; inspect its printed evidence.'}

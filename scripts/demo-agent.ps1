param([switch]$Smoke)
$ErrorActionPreference='Stop'
$arguments=@((Join-Path $PSScriptRoot 'demo-agent.py'))
if($Smoke){$arguments+='--smoke'}
& python @arguments
if($LASTEXITCODE -ne 0){throw 'VIAVW agent demonstration failed; inspect the printed artifact directory and error.'}

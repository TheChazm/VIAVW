param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'layers.py'), 'demo')
if ($Smoke) { $arguments += '--smoke' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Layer demonstration failed; inspect the printed descriptor. Running applications are retained.' }

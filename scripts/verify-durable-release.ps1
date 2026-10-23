param()
$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'verify-durable-release.py')
if ($LASTEXITCODE -ne 0) { throw 'Durable release verification did not pass. Existing selections remain unchanged; inspect the printed evidence.' }

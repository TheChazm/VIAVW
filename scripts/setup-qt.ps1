$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$wheelDir = Join-Path $projectRoot '.deps\wheels'
$pythonDir = Join-Path $projectRoot '.deps\python'
python -m pip download --no-cache-dir --index-url https://pypi.org/simple --dest $wheelDir --only-binary=:all: PySide6-Essentials==6.10.3 shiboken6==6.10.3
if ($LASTEXITCODE -ne 0) { throw 'Qt wheel download failed' }
python -m pip install --no-cache-dir --no-index --find-links $wheelDir --target $pythonDir PySide6-Essentials==6.10.3 shiboken6==6.10.3
if ($LASTEXITCODE -ne 0) { throw 'Project-local Qt install failed' }

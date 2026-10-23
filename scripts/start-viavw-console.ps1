param([string]$Output)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$Output){$Output=Join-Path $root ('artifacts/codex-control-'+[guid]::NewGuid().ToString('N'))}
$manifest=Get-Content (Join-Path $root 'artifacts/installed-apps-current.json') -Raw|ConvertFrom-Json
$hostExe=[IO.Path]::GetFullPath($manifest.executable)
$binRoot=[IO.Path]::GetFullPath((Join-Path $root 'src/LayerHost/bin'))+[IO.Path]::DirectorySeparatorChar
if(!$hostExe.StartsWith($binRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($hostExe,'.dll'))).Hash -ne $manifest.assemblySha256){throw 'Research host path/hash mismatch.'}
& python -u (Join-Path $PSScriptRoot 'viavw-console.py') $Output --compact --host $hostExe
if($LASTEXITCODE -ne 0){throw 'VIAVW direct-use console failed; inspect its output directory.'}

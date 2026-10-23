$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content (Join-Path $projectRoot 'artifacts/agent-api-current.json') -Raw|ConvertFrom-Json
$hostExe=[IO.Path]::GetFullPath($manifest.executable)
$binRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'src/LayerHost/bin'))+[IO.Path]::DirectorySeparatorChar
if(!$hostExe.StartsWith($binRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($hostExe,'.dll'))).Hash -ne $manifest.assemblySha256){throw 'Preserved agent host path/hash mismatch.'}
$output=Join-Path $projectRoot ('artifacts/agent-verification-'+[guid]::NewGuid().ToString('N'))
& $hostExe --test-agent-host $output
if($LASTEXITCODE -ne 0){throw "Agent interface verification failed: $output"}
& (Join-Path $PSScriptRoot 'demo-agent.ps1') -Smoke
Write-Output "Agent protocol evidence: $output"

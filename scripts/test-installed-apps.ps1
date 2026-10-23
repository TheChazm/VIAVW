param([switch]$SkipBaselineRegression)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content (Join-Path $root 'artifacts/installed-apps-current.json') -Raw|ConvertFrom-Json
$hostExe=[IO.Path]::GetFullPath($manifest.executable)
$binRoot=[IO.Path]::GetFullPath((Join-Path $root 'src/LayerHost/bin'))+[IO.Path]::DirectorySeparatorChar
if(!$hostExe.StartsWith($binRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($hostExe,'.dll'))).Hash -ne $manifest.assemblySha256){throw 'Research host path/hash mismatch.'}
& (Join-Path $PSScriptRoot 'test-surface-rebind.ps1') -HostAssembly ([IO.Path]::ChangeExtension($hostExe,'.dll'))
& $hostExe --test-passive-inspection (Join-Path $root ('artifacts/passive-inspection-'+[guid]::NewGuid().ToString('N')))
if($LASTEXITCODE -ne 0){throw 'Passive inspection lifecycle/fault checks failed.'}
& python (Join-Path $PSScriptRoot 'test-fast-inspection.py') $hostExe
if($LASTEXITCODE -ne 0){throw 'Direct asynchronous inspection checks failed.'}
& python (Join-Path $PSScriptRoot 'test-viavw-console.py')
if($LASTEXITCODE -ne 0){throw 'Direct-use console workflow checks failed.'}
foreach($application in @('Word','VLC','7Zip')){& (Join-Path $PSScriptRoot 'demo-installed-app.ps1') -Application $application -Smoke}
if(!$SkipBaselineRegression){
    $output=Join-Path $root ('artifacts/installed-apps-regression-'+[guid]::NewGuid().ToString('N'))
    & $hostExe --test-agent-host $output
    if($LASTEXITCODE -ne 0){throw "Installed Notepad++/Visual Studio regression failed: $output"}
    Write-Output "Baseline regression evidence: $output"
}
Write-Output 'Installed-app console checks and launcher smoke tests passed. Direct Word/VLC workflow evidence and remaining limitations are documented separately.'

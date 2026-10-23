param([switch]$SkipBuild, [switch]$AllowDuplicate, [string]$DemoDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-mpv-research.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') -Dynamic
}
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output = if ($DemoDirectory) { [IO.Path]::GetFullPath($DemoDirectory) } else { Join-Path $projectRoot ('artifacts\mpv-demo-' + [guid]::NewGuid().ToString('N')) }
& $hostExe --prepare-mpv-demo $output
if ($LASTEXITCODE -ne 0) { throw 'Disposable mpv demo preparation failed.' }
$appArguments = @(Get-Content -LiteralPath (Join-Path $output 'launch-arguments.json') -Raw | ConvertFrom-Json)
& (Join-Path $PSScriptRoot 'demo.ps1') -SkipBuild -AllowDuplicate:$AllowDuplicate -Executable (Join-Path $projectRoot '.deps\mpv-7c4f662c0\mpv.exe') -ApplicationArguments $appArguments -ExperimentalGpuBridge mpv -RootInput -QueuePointer -StatusFile (Join-Path $output 'session.json')
Write-Output "Demo data: $output"

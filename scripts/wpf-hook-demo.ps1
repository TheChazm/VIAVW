param([switch]$SkipBuild, [string]$StatusFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceExe = Join-Path $projectRoot 'artifacts\baselines\accepted-private-gpu-20261014\src\WpfProbe\bin\Release\net10.0-windows\WpfProbe.exe'
Write-Output 'Research demonstration: unchanged accepted WPF executable, with an opt-in process-local .NET startup adapter selecting software rendering.'
& (Join-Path $PSScriptRoot 'demo.ps1') -Executable $sourceExe -WpfSoftwareHook -QueuePointer -SkipBuild:$SkipBuild -StatusFile $StatusFile

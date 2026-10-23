param([switch]$SkipBuild, [switch]$AllowDuplicate, [string]$StatusFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceExe = Join-Path $projectRoot '.deps\ILSpy-10.1.0.8386-x64\app\ILSpy.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'The pinned portable ILSpy research package is missing; see the ordinary WPF research report.' }
$dataDirectory = Join-Path $projectRoot ('artifacts\ilspy-demo\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDirectory | Out-Null
$configFile = Join-Path $dataDirectory 'ILSpy.xml'
'<ILSpy><UpdateSettings><AutomaticUpdateCheckEnabled>false</AutomaticUpdateCheckEnabled></UpdateSettings><MiscSettings LoadPreviousAssemblies="false" AllowMultipleInstances="true"/><SessionSettings><WindowBounds>10,10,880,440</WindowBounds><Theme>Light</Theme><CurrentCulture>en-US</CurrentCulture></SessionSettings></ILSpy>' | Set-Content -LiteralPath $configFile
$assembly = Join-Path $projectRoot 'src\WpfProbe\bin\Release\net10.0-windows\WpfProbe.dll'
Write-Output 'Ordinary ILSpy WPF application: unchanged portable binaries, isolated config and process-local software rendering adapter. Drag gestures are delivered when released.'
& (Join-Path $PSScriptRoot 'demo.ps1') -Executable $sourceExe -ApplicationArguments @('--newinstance', '--config', $configFile, '--navigateto', 'T:WpfProbe.Program', $assembly) -WpfSoftwareHook -QueuePointer -RootInput -SkipBuild:$SkipBuild -AllowDuplicate:$AllowDuplicate -StatusFile $StatusFile

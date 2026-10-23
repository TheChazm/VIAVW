param([switch]$SkipBuild, [switch]$QueuePointer, [switch]$Software, [switch]$Layered)
$projectRoot = Split-Path -Parent $PSScriptRoot
$applicationOptions = @('--confirm-close')
if ($Software) { $applicationOptions += '--software' }
if ($Layered) { $applicationOptions += '--layered' }
if (-not ($Software -or $Layered)) { $applicationOptions += '--enable-grab-adapter' }
& (Join-Path $PSScriptRoot 'demo.ps1') -Executable (Join-Path $projectRoot 'src\WpfProbe\bin\Release\net10.0-windows\WpfProbe.exe') -ApplicationArguments $applicationOptions -CooperativeFixture:(-not ($Software -or $Layered)) -FullContent:$Layered -QueuePointer:($QueuePointer -or $Software -or $Layered) -SkipBuild:$SkipBuild

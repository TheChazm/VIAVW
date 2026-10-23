param([string]$Executable, [string[]]$ApplicationArguments = @(), [switch]$SkipBuild, [switch]$Hidden, [switch]$AllowDuplicate, [switch]$CooperativeFixture, [switch]$QtSoftware, [switch]$FullContent, [switch]$QueuePointer, [switch]$RootInput, [switch]$PostedShortcuts, [switch]$FitPreview, [switch]$WpfSoftwareHook, [switch]$ExperimentalHiddenInput, [ValidateRange(0,500)][int]$InspectionSettleMilliseconds=150, [ValidateSet('mpv','dynamic','qiv')][string]$ExperimentalGpuBridge, [switch]$QivNativeStartup, [string]$StatusFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
if (-not $Executable) { $Executable = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe' }
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw "LayerHost executable missing. Run scripts\build.ps1 first: $hostExe" }
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Application executable missing: $Executable" }
function ConvertTo-WindowsArgument([string]$Value)
{
    $argBuilder = [System.Text.StringBuilder]::new('"')
    $slashCount = 0
    foreach ($character in $Value.ToCharArray())
    {
        if ($character -eq '\') { $slashCount++; continue }
        if ($character -eq '"')
        {
            [void]$argBuilder.Append('\', 2 * $slashCount + 1)
            [void]$argBuilder.Append($character)
        }
        else
        {
            [void]$argBuilder.Append('\', $slashCount)
            [void]$argBuilder.Append($character)
        }
        $slashCount = 0
    }
    [void]$argBuilder.Append('\', 2 * $slashCount)
    [void]$argBuilder.Append('"')
    return $argBuilder.ToString()
}
$sessionFile = if ($StatusFile) { [IO.Path]::GetFullPath($StatusFile) } else { Join-Path $projectRoot ('artifacts\sessions\' + [guid]::NewGuid().ToString('N') + '.json') }
if (-not $sessionFile.StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The demo status file must stay within project artifacts.' }
if (Test-Path -LiteralPath $sessionFile) { throw 'Use a fresh status-file path; existing session evidence will not be overwritten.' }
$hostArguments = @('--demo', '--ready-file', $sessionFile)
$hostArguments += @('--inspection-settle', [string]$InspectionSettleMilliseconds)
if ($Hidden) { $hostArguments += '--hidden' }
if ($AllowDuplicate) { $hostArguments += '--allow-duplicate' }
if ($CooperativeFixture) { $hostArguments += '--cooperative-fixture' }
if ($FullContent) { $hostArguments += '--full-content' }
if ($QueuePointer) { $hostArguments += '--queue-pointer' }
if ($RootInput) { $hostArguments += '--root-input' }
if ($PostedShortcuts) { $hostArguments += '--posted-shortcuts' }
if ($FitPreview) { $hostArguments += '--fit-preview' }
if ($ExperimentalHiddenInput) { $hostArguments += '--experimental-hidden-input' }
if ($ExperimentalGpuBridge) { $hostArguments += @('--experimental-gpu-bridge', $ExperimentalGpuBridge) }
if ($QivNativeStartup) { $hostArguments += '--qiv-native-startup' }
if ($WpfSoftwareHook) { $hostArguments += '--wpf-software-hook' }
if ($QtSoftware) { $hostArguments += '--qt-software' }
$launchArguments = ($hostArguments + $Executable + $ApplicationArguments | ForEach-Object { ConvertTo-WindowsArgument $_ }) -join ' '
$windowStyle = if ($Hidden) { 'Hidden' } else { 'Normal' }
$demoProcess = Start-Process -FilePath $hostExe -ArgumentList $launchArguments -WorkingDirectory $projectRoot -WindowStyle $windowStyle -PassThru
$startupWatch = [Diagnostics.Stopwatch]::StartNew()
while ($startupWatch.Elapsed.TotalSeconds -lt 25)
{
    if (Test-Path -LiteralPath $sessionFile)
    {
        $sessionState = Get-Content -Raw -LiteralPath $sessionFile | ConvertFrom-Json
        if ($sessionState.state -eq 'error') { throw $sessionState.error }
        if ($sessionState.state -eq 'ready')
        {
            Write-Output "VIAVW demo ready. PID=$($demoProcess.Id); hidden=$Hidden; status=$sessionFile"
            return
        }
    }
    if ($demoProcess.HasExited) { throw "VIAVW exited before its viewer was ready (exit $($demoProcess.ExitCode)). Status: $sessionFile" }
    Start-Sleep -Milliseconds 100
}
throw "VIAVW startup deadline exceeded. This script launched PID $($demoProcess.Id); inspect $sessionFile. No unrelated process was terminated."

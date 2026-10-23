param([switch]$SkipBuild, [switch]$PrepareOnly, [switch]$Diagnostics, [switch]$ExperimentalCapture, [switch]$PrintCapture, [switch]$PreviousInput,
    [string]$ProfileSetup = '')
$ErrorActionPreference = 'Stop'
if(@($Diagnostics,$ExperimentalCapture,$PrintCapture,$PreviousInput|Where-Object {$_}).Count -gt 1){throw 'Choose one capture override: Diagnostics, ExperimentalCapture, PrintCapture or PreviousInput.'}
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestFile = Join-Path $projectRoot 'artifacts\visual-studio-current.json'
$manifest = if (Test-Path -LiteralPath $manifestFile) { Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json } else { $null }
if ($SkipBuild) {
    if (-not $manifest) { throw 'No verified runnable VS build is recorded. Run this script without -SkipBuild to build a separate viewer.' }
    if($ExperimentalCapture -and !$manifest.experimentalExecutable){throw 'No preserved experimental capture viewer is recorded.'}
    if($PreviousInput -and !$manifest.previousInputExecutable){throw 'No preserved previous-input viewer is recorded.'}
    $hostExe = [IO.Path]::GetFullPath($(if($PrintCapture -and $manifest.printCaptureExecutable){$manifest.printCaptureExecutable}elseif($PreviousInput){$manifest.previousInputExecutable}elseif($ExperimentalCapture){$manifest.experimentalExecutable}else{$manifest.executable}))
    $binaryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\LayerHost\bin')) + [IO.Path]::DirectorySeparatorChar
    if (-not $hostExe.StartsWith($binaryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe') { throw 'The verified viewer must be inside the VIAVW build directory.' }
    $assembly = [IO.Path]::ChangeExtension($hostExe, '.dll')
    $expectedHash=if($PrintCapture -and $manifest.printCaptureExecutable){$manifest.printCaptureAssemblySha256}elseif($PreviousInput){$manifest.previousInputAssemblySha256}elseif($ExperimentalCapture){$manifest.experimentalAssemblySha256}else{$manifest.assemblySha256}
    if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The recorded viewer changed. Rebuild without -SkipBuild.' }
} else {
    # A fresh output avoids overwriting the executable of an open viewer.
    $buildOutput = Join-Path $projectRoot ('src\LayerHost\bin\VS-local-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '\net10.0-windows')
    & dotnet build (Join-Path $projectRoot 'src\LayerHost\LayerHost.csproj') -c Release -o $buildOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Viewer build failed.' }
    $hostExe = Join-Path $buildOutput 'LayerHost.exe'
}
if (-not $ProfileSetup) {
    $ProfileSetup = if ($manifest) { $manifest.profileSetup } else { Join-Path $projectRoot 'artifacts\vs-m30-runnable\launch.json' }
}
if (-not (Test-Path -LiteralPath $ProfileSetup)) { throw 'The configured disposable VS profile is missing. See docs/VISUAL-STUDIO-END-TO-END-20261017.md.' }
$output = Join-Path $projectRoot ('artifacts\visual-studio-demo-' + [guid]::NewGuid().ToString('N'))
& $hostExe --prepare-vs-runnable-with-profile $output $ProfileSetup
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the runnable disposable solution. An existing viewer using this experimental profile must finish first.' }
$setupFile = Join-Path $output 'launch.json'
$setup = Get-Content -LiteralPath $setupFile -Raw | ConvertFrom-Json
$viewerMode = if ($Diagnostics) { '--run-vs-diagnostic-viewer' } elseif ($PrintCapture) { '--run-vs-private-viewer' } elseif ($PreviousInput) { if($manifest.previousInputViewerMode){$manifest.previousInputViewerMode}else{'--run-vs-dc-viewer'} } elseif ($ExperimentalCapture) { if($manifest.experimentalViewerMode){$manifest.experimentalViewerMode}else{'--run-vs-dc-pixel-viewer'} } elseif ($manifest.viewerMode) { $manifest.viewerMode } else { '--run-vs-private-viewer' }
if($viewerMode -notin @('--run-vs-private-viewer','--run-vs-dc-viewer','--run-vs-precise-viewer','--run-vs-damage-viewer','--run-vs-fast-viewer','--run-vs-prepared-viewer','--run-vs-dc-pixel-viewer','--run-vs-diagnostic-viewer')){throw 'Unknown preserved viewer mode.'}
Write-Output "Installed Visual Studio 2022, private desktop, experimental profile: $($setup.Suffix)"
Write-Output "Viewer: $hostExe"
if($viewerMode -eq '--run-vs-prepared-viewer'){Write-Output 'Private-window capture with input-driven preview and editor pointer lookup prepared during startup. Every click still validates the current editor. -PreviousInput selects the prior verified viewer.'}
elseif($viewerMode -eq '--run-vs-fast-viewer'){Write-Output 'Private-window DC capture with input-driven preview deadlines, split-region painting and faster verified shortcuts. Application transitions retain protective pacing. -PreviousInput selects the preserved earlier viewer.'}
elseif($viewerMode -eq '--run-vs-damage-viewer'){Write-Output 'Private-window DC capture with incremental preview painting and verified mouse routing. Keyboard pacing is preserved.'}
elseif($viewerMode -eq '--run-vs-precise-viewer'){Write-Output 'Private-window DC capture with precise mouse waits and faster owned-popup targeting. Keyboard pacing is preserved. -PreviousInput selects the prior DC input path.'}
elseif($viewerMode -eq '--run-vs-dc-viewer'){Write-Output 'Private-window DC capture with reusable destination resources. -PrintCapture selects the preserved printing path for comparison.'}
elseif($ExperimentalCapture){Write-Output 'Experimental capture: fresh editor pixels and capture lifecycle verified. The complete run/debug/designer/menu regression remains pending for this backend.'}
Write-Output "Runnable disposable solution: $($setup.Solution)"
Write-Output "Code: $($setup.First), $($setup.Second), $(Join-Path (Split-Path $setup.First) 'Program.cs')"
Write-Output 'Choose Human control, then click the editor or a menu in the picture.'
Write-Output 'Ctrl+Shift+B builds; Ctrl+F5 runs without debugging; F5 starts or continues debugging; F9 toggles a breakpoint; F10 steps; Shift+F5 stops debugging.'
Write-Output 'Use Window or Next window in the viewer toolbar to see and control the running application, then return to the IDE.'
Write-Output 'Ctrl+O opens files, Ctrl+S saves and Ctrl+Tab switches files. Return to automation rejects picture input.'
Write-Output 'Software rendering is used. Native GPU capture, arbitrary docking, extensions and all installed workloads are not fully verified.'
Write-Output 'Refresh updates pixels. Inspect details collects slower accessibility evidence. -Diagnostics enables the older continuous diagnostic mode.'
Write-Output 'Close the viewer for graceful shutdown; inspect any save prompt. Force close discards unsaved state only in this disposable process tree.'
if ($PrepareOnly) { Write-Output "Prepared only: $setupFile"; return }
$viewerOutput = Join-Path $output 'viewer'
New-Item -ItemType Directory -Path $viewerOutput | Out-Null
& $hostExe $viewerMode $setupFile $viewerOutput
if ($LASTEXITCODE -ne 0) { throw "Visual Studio demonstration failed. Evidence: $output" }

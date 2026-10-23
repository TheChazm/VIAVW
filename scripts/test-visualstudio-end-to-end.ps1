param([switch]$SkipBuild, [switch]$CoreOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'artifacts\visual-studio-current.json'
$selectedDc=$false;$selectedPrecise=$false;$selectedDamage=$false;$selectedFast=$false;$selectedPrepared=$false
if ($SkipBuild) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $hostExe = [IO.Path]::GetFullPath($manifest.executable)
    $binaryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\LayerHost\bin')) + [IO.Path]::DirectorySeparatorChar
    if (-not $hostExe.StartsWith($binaryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($hostExe, '.dll'))).Hash -ne $manifest.assemblySha256) { throw 'Verified viewer path or hash mismatch.' }
    $profileSetup = $manifest.profileSetup
    $selectedPrepared=$manifest.viewerMode -eq '--run-vs-prepared-viewer';$selectedFast=$manifest.viewerMode -in @('--run-vs-fast-viewer','--run-vs-prepared-viewer')
    $selectedDamage=$manifest.viewerMode -eq '--run-vs-damage-viewer'
    $selectedPrecise=$manifest.viewerMode -eq '--run-vs-precise-viewer'
    $selectedDc=$manifest.viewerMode -in @('--run-vs-dc-viewer','--run-vs-precise-viewer','--run-vs-damage-viewer','--run-vs-fast-viewer','--run-vs-prepared-viewer')
    if($CoreOnly -and $selectedDc){
        $hostExe=[IO.Path]::GetFullPath($manifest.printCaptureExecutable)
        if(!$hostExe.StartsWith($binaryRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($hostExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($hostExe,'.dll'))).Hash -ne $manifest.printCaptureAssemblySha256){throw 'Printing control viewer path or hash mismatch.'}
        Write-Output 'CoreOnly uses the preserved printing control. Omit CoreOnly to verify the selected DC viewer.'
        $selectedDc=$false;$selectedPrecise=$false;$selectedDamage=$false;$selectedFast=$false;$selectedPrepared=$false
    }
} else {
    $buildOutput = Join-Path $projectRoot ('src\LayerHost\bin\VS-test-' + [guid]::NewGuid().ToString('N').Substring(0,8) + '\net10.0-windows')
    & dotnet build (Join-Path $projectRoot 'src\LayerHost\LayerHost.csproj') -c Release -o $buildOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $hostExe = Join-Path $buildOutput 'LayerHost.exe'
    $profileSetup = Join-Path $projectRoot 'artifacts\vs-m30-runnable\launch.json'
    if(Test-Path -LiteralPath $manifestPath){$mode=(Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json).viewerMode;$selectedDc=$mode -in @('--run-vs-dc-viewer','--run-vs-precise-viewer','--run-vs-damage-viewer','--run-vs-fast-viewer','--run-vs-prepared-viewer');$selectedPrecise=$mode -eq '--run-vs-precise-viewer';$selectedDamage=$mode -eq '--run-vs-damage-viewer';$selectedPrepared=$mode -eq '--run-vs-prepared-viewer';$selectedFast=$mode -in @('--run-vs-fast-viewer','--run-vs-prepared-viewer')}
}
$run = Join-Path $projectRoot ('artifacts\vs-end-to-end-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$evidence = Join-Path $run 'viewer'
& $hostExe --prepare-vs-runnable-with-profile $fixture $profileSetup
if ($LASTEXITCODE -ne 0) { throw 'Disposable profile is in use or could not be prepared. Existing applications were left alone.' }
$testExe=$hostExe
if (-not $CoreOnly) {
    if ($SkipBuild) {
        $testExe = [IO.Path]::GetFullPath($manifest.verifierExecutable)
        if (-not $testExe.StartsWith($binaryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($testExe) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($testExe, '.dll'))).Hash -ne $manifest.verifierAssemblySha256) { throw 'Verified mouse test harness path or hash mismatch.' }
    }
    if($selectedDc){
        if($testExe -ne $hostExe){throw 'The DC workflow requires the matching preserved viewer and verifier.'}
        $testMode=if($selectedPrepared){'--test-vs-prepared-complete'}elseif($selectedFast){'--test-vs-fast-complete'}elseif($selectedDamage){'--test-vs-damage-complete'}elseif($selectedPrecise){'--test-vs-precise-complete'}else{'--test-vs-dc-complete'}
        & $testExe $testMode $evidence (Join-Path $fixture 'launch.json')
    }else{& $testExe --test-vs-complete-with-viewer $evidence (Join-Path $fixture 'launch.json') $hostExe}
} else {
    if($selectedDc){Write-Output 'CoreOnly runs the printing control path. The full suite verifies the selected DC backend.'}
    & $hostExe --test-vs-end-to-end $evidence (Join-Path $fixture 'launch.json')
}
if ($LASTEXITCODE -ne 0) { throw "One or more checks failed. Full evidence: $evidence\end-to-end.json" }
if (-not $CoreOnly) {
    $mouseFixture = Join-Path $run 'mouse-fixture'
    $mouseEvidence = Join-Path $run 'mouse-viewer'
    & $hostExe --prepare-vs-runnable-with-profile $mouseFixture $profileSetup
    if ($LASTEXITCODE -ne 0) { throw 'Mouse regression fixture preparation failed.' }
    if($selectedDc){$mouseMode=if($selectedPrepared){'--test-vs-prepared-mouse'}elseif($selectedFast){'--test-vs-fast-mouse'}elseif($selectedDamage){'--test-vs-damage-mouse'}elseif($selectedPrecise){'--test-vs-precise-mouse'}else{'--test-vs-dc-mouse'};& $testExe $mouseMode $mouseEvidence (Join-Path $mouseFixture 'launch.json')}
    else{& $testExe --test-vs-mouse-with-viewer $mouseEvidence (Join-Path $mouseFixture 'launch.json') $hostExe}
    if ($LASTEXITCODE -ne 0) { throw "Mouse regression failed. Evidence: $mouseEvidence\vs-workflow.json" }
    Write-Output "Verified mouse selection/deletion/undo, wheel, menus and focus. Evidence: $mouseEvidence\vs-workflow.json"
}
Write-Output "Verified workflows and cleanup. Evidence: $evidence\end-to-end.json"

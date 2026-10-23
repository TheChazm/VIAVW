param([switch]$SkipBuild, [string]$ProfileSetup = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\visual-studio-current.json') -Raw|ConvertFrom-Json
$selectedPrepared=$manifest.viewerMode -eq '--run-vs-prepared-viewer';$selectedFast=$manifest.viewerMode -in @('--run-vs-fast-viewer','--run-vs-prepared-viewer')
$selectedDamage=$manifest.viewerMode -eq '--run-vs-damage-viewer'
$selectedPrecise=$manifest.viewerMode -eq '--run-vs-precise-viewer'
$selectedDc=$manifest.viewerMode -in @('--run-vs-dc-viewer','--run-vs-precise-viewer','--run-vs-damage-viewer','--run-vs-fast-viewer','--run-vs-prepared-viewer')
$binaryRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'src\LayerHost\bin'))+[IO.Path]::DirectorySeparatorChar
function ValidatedBinary([string]$path,[string]$hash){
    $full=[IO.Path]::GetFullPath($path)
    if(!$full.StartsWith($binaryRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -ne 'LayerHost.exe' -or (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($full,'.dll'))).Hash -ne $hash){throw 'Preserved benchmark viewer/verifier path or hash mismatch.'}
    return $full
}
if($SkipBuild){
    $hostExe=ValidatedBinary $manifest.executable $manifest.assemblySha256
    if($manifest.performanceVerifierExecutable){$driver=ValidatedBinary $manifest.performanceVerifierExecutable $manifest.performanceVerifierAssemblySha256}
    else{$driver=ValidatedBinary $manifest.verifierExecutable $manifest.verifierAssemblySha256}
}else{
    $buildOutput=Join-Path $projectRoot ('src\LayerHost\bin\VS-responsive-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'\net10.0-windows')
    & dotnet build (Join-Path $projectRoot 'src\LayerHost\LayerHost.csproj') -c Release -o $buildOutput --nologo
    if($LASTEXITCODE -ne 0){throw 'Benchmark build failed.'}
    $hostExe=Join-Path $buildOutput 'LayerHost.exe';$driver=$hostExe
}
if (-not $ProfileSetup) { $ProfileSetup = $manifest.profileSetup }
$output = Join-Path $projectRoot ('artifacts\vs-responsiveness-' + [guid]::NewGuid().ToString('N'))
$setupDirectory = Join-Path $output 'project'
& $driver --prepare-vs-runnable-with-profile $setupDirectory $ProfileSetup
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare a disposable solution.' }
$testMode=if($selectedPrepared){'--test-vs-prepared-responsive'}elseif($selectedFast){'--test-vs-fast-responsive'}elseif($selectedDamage){'--test-vs-damage-responsive'}elseif($selectedPrecise){'--test-vs-precise-responsive'}elseif($selectedDc){'--test-vs-responsive-dc'}else{'--test-vs-responsiveness'}
if($selectedFast -or $selectedDamage -or $selectedPrecise){& $driver $testMode (Join-Path $output 'viewer') (Join-Path $setupDirectory 'launch.json') $hostExe}
else{& $driver $testMode (Join-Path $output 'viewer') (Join-Path $setupDirectory 'launch.json') $hostExe true}
if ($LASTEXITCODE -ne 0) { throw "Responsiveness/deletion verification failed: $output" }
Write-Output "Responsiveness evidence: $output"

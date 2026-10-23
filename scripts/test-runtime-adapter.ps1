param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-gpu-research.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
foreach ($required in @('artifacts\gpu-research-build\D3DProbe.exe','artifacts\baselines\accepted-capture-timing-20261014\native-build\D3DProbe.exe','artifacts\gpu-research-build\PresentBridge.dll','artifacts\gpu-research-build\GpuResearchHost.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $required))) { throw "Missing $required; the instrumentation guard requires the preserved, unchanged accepted probe." }
}
$outputDir = Join-Path $projectRoot ('artifacts\runtime-adapter-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDir | Out-Null
$runs = @()
foreach ($experiment in @(
    @{ name='GPU bridge, input, resources and recovery'; operation='--test-gpu-bridge'; folder='bridge' },
    @{ name='Two independent instrumented private layers'; operation='--test-concurrent-gpu-bridge'; folder='concurrent' },
    @{ name='Ordinary native semantic accessibility'; operation='--research-accessibility'; folder='accessibility' }
)) {
    Write-Output $experiment.name
    & $hostExe $experiment.operation (Join-Path $outputDir $experiment.folder)
    $runs += [pscustomobject]@{ name=$experiment.name; exitCode=$LASTEXITCODE; folder=$experiment.folder }
    if ($LASTEXITCODE -ne 0) { $runs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDir 'runs.json'); throw "Research failed; inspect $outputDir" }
}
& (Join-Path $PSScriptRoot 'research-session-capabilities.ps1') -OutputDirectory (Join-Path $outputDir 'sessions')
$runs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDir 'runs.json') -Encoding utf8
Write-Output "Runtime adapter evidence: $outputDir"

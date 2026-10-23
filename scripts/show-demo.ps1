param([Parameter(Mandatory)][string]$SessionFile)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$statusPath = [IO.Path]::GetFullPath($SessionFile)
if (-not $statusPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use the explicit status file for a demo in this project.' }
$state = Get-Content -Raw -LiteralPath $statusPath | ConvertFrom-Json
if ($state.state -ne 'ready') { throw "Demo is not ready: $($state.state)" }
$expectedBuild = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows'))
if ([IO.Path]::GetFullPath($state.buildDirectory).TrimEnd('\') -ne $expectedBuild.TrimEnd('\')) { throw 'Status file is from another build directory.' }
[IO.File]::WriteAllText($statusPath + '.show', 'show')
Write-Output 'Requested that this explicitly selected demo reveal its existing viewer.'

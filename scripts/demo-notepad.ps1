param([switch]$SkipBuild, [switch]$DefaultRendering, [switch]$Hidden)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1')}
$hostExe=Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$output=Join-Path $projectRoot ('artifacts\installed-notepad-demo-'+[guid]::NewGuid().ToString('N'))
$operation=if($DefaultRendering){'--prepare-installed-notepad-demo'}else{'--prepare-installed-notepad-gdi-demo'}
& $hostExe $operation $output
if($LASTEXITCODE -ne 0){throw 'Could not prepare disposable installed Notepad++ demonstration.'}
$setup=Get-Content -LiteralPath (Join-Path $output 'launch.json') -Raw|ConvertFrom-Json
Write-Output "Installed Notepad++; plugins disabled; disposable file and settings: $output"
if(-not $DefaultRendering){Write-Output 'Dedicated configuration uses the built-in GDI renderer for visible editor pixels.'}
& (Join-Path $PSScriptRoot 'demo.ps1') -SkipBuild -Hidden:$Hidden -FullContent -QueuePointer -PostedShortcuts -FitPreview -Executable $setup.Executable -ApplicationArguments @($setup.Arguments)

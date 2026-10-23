param([string]$Python = 'C:\Python314\python.exe', [switch]$SkipBuild, [switch]$Software, [switch]$NativeGpu)
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($NativeGpu) { & (Join-Path $PSScriptRoot 'demo.ps1') -Executable $Python -ApplicationArguments @((Join-Path $projectRoot 'experiments\quick_probe.py')) -FullContent -SkipBuild:$SkipBuild }
elseif ($Software) { & (Join-Path $PSScriptRoot 'demo.ps1') -Executable $Python -ApplicationArguments @((Join-Path $projectRoot 'experiments\quick_probe.py')) -QtSoftware -SkipBuild:$SkipBuild }
else { & (Join-Path $PSScriptRoot 'demo.ps1') -Executable $Python -ApplicationArguments @((Join-Path $projectRoot 'experiments\quick_probe.py'), '--enable-grab-adapter') -CooperativeFixture -SkipBuild:$SkipBuild }

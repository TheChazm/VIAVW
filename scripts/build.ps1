$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $projectRoot 'src\LayerProbe\LayerProbe.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'LayerProbe build failed' }
dotnet build (Join-Path $projectRoot 'src\LayerHost\LayerHost.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'LayerHost build failed' }
dotnet build (Join-Path $projectRoot 'src\WpfProbe\WpfProbe.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'WpfProbe build failed' }
dotnet build (Join-Path $projectRoot 'src\WpfRenderHook\WpfRenderHook.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'WpfRenderHook build failed' }

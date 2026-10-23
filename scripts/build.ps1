param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$restoreOptions = @()
if ($Offline) {
    $restoreOptions += "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.offline.config')"
    $restoreOptions += '-p:NuGetAudit=false'
}
foreach ($project in @('LayerProbe','LayerHost','WpfProbe','WpfRenderHook','ViavwTool','ViavwDesktop')) {
    dotnet build (Join-Path $projectRoot "src\$project\$project.csproj") -c Release --nologo @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw "$project build failed" }
}

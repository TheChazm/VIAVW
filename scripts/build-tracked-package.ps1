param(
    [Parameter(Mandatory)][string]$Release,
    [string]$Version = '0.1.2',
    [string]$OutputRoot,
    [switch]$Offline,
    [string]$PackageCache
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if ($Release -notmatch '^tool-[a-z0-9-]+$') { throw 'Use a simple tool- release name.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch product version.' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $projectRoot 'artifacts\packages' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$sourceRoot = (Join-Path $projectRoot 'src') + [IO.Path]::DirectorySeparatorChar
$installedRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\VIAVW')
foreach ($protectedRoot in @($sourceRoot.TrimEnd('\'), $installedRoot)) {
    if ($OutputRoot.Equals($protectedRoot,[StringComparison]::OrdinalIgnoreCase) -or
        $OutputRoot.StartsWith($protectedRoot+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputRoot must not be inside source or the installed VIAVW application.'
    }
}
$destination = Join-Path $OutputRoot $Release
$archive = Join-Path $OutputRoot "VIAVW-Tool-$Version-$Release-win-x64.zip"
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $archive)) {
    throw 'Package destination/archive already exists; choose a fresh release name.'
}
$cacheBefore = $env:NUGET_PACKAGES
$advertisingBefore = $env:DOTNET_CLI_WORKLOAD_UPDATE_ADVERTISING_DISABLE
$notifyBefore = $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE
try {
    if ($PackageCache) {
        $PackageCache = [IO.Path]::GetFullPath($PackageCache)
        if (-not (Test-Path -LiteralPath $PackageCache -PathType Container)) { throw 'PackageCache must already exist; no packages are installed by this script.' }
        $env:NUGET_PACKAGES = $PackageCache
    }
    $env:DOTNET_CLI_WORKLOAD_UPDATE_ADVERTISING_DISABLE = 'true'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    $options = @('-p:PublishReadyToRun=false','-p:UseSharedCompilation=false',"-p:PathMap=$projectRoot=/src/viavw")
    if ($Offline) {
        $options += "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.offline.config')"
        $options += '-p:NuGetAudit=false'
    }
    $inputs = @((Get-Item -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')))
    foreach ($project in @('LayerHost','ViavwTool','ViavwDesktop')) {
        $inputs += Get-ChildItem -LiteralPath (Join-Path $projectRoot "src\$project") -File |
            Where-Object { $_.Extension -in '.cs','.csproj' }
    }
    $sourceHashes = [ordered]@{}
    foreach ($inputFile in $inputs | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($projectRoot,$inputFile.FullName).Replace('\','/')
        $sourceHashes[$relative] = (Get-FileHash -LiteralPath $inputFile.FullName -Algorithm SHA256).Hash
    }
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    $buildFolder = Join-Path $projectRoot "artifacts\tracked-build-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $buildFolder | Out-Null
    $hostOutput = Join-Path $destination 'src\LayerHost\bin\Runtime\net10.0-windows'
    dotnet publish (Join-Path $projectRoot 'src\LayerHost\LayerHost.csproj') -c Standalone -r win-x64 --self-contained true -o $hostOutput --nologo -v minimal @options
    if ($LASTEXITCODE -ne 0) { throw 'Tracked host publish failed; no selection changed.' }
    dotnet publish (Join-Path $projectRoot 'src\ViavwTool\ViavwTool.csproj') -c $Release -r win-x64 --self-contained true -o $destination --nologo -v minimal @options
    if ($LASTEXITCODE -ne 0) { throw 'Tracked tool publish failed; no selection changed.' }
    $desktopOutput = Join-Path $buildFolder 'desktop'
    dotnet publish (Join-Path $projectRoot 'src\ViavwDesktop\ViavwDesktop.csproj') -c $Release -r win-x64 --self-contained true -o $desktopOutput --nologo -v minimal @options
    if ($LASTEXITCODE -ne 0) { throw 'Tracked manager publish failed; no selection changed.' }
    foreach ($file in Get-ChildItem -LiteralPath $desktopOutput -File) {
        $existing = Join-Path $hostOutput $file.Name
        if ($file.Name.StartsWith('VIAVW.Manager')) { Copy-Item -LiteralPath $file.FullName -Destination $existing }
        elseif (-not (Test-Path -LiteralPath $existing) -or
            (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $existing).Hash) {
            throw "Manager runtime differs from host: $($file.Name). No selection changed."
        }
    }
    foreach ($inputFile in $inputs) {
        $relative = [IO.Path]::GetRelativePath($projectRoot,$inputFile.FullName).Replace('\','/')
        if ((Get-FileHash -LiteralPath $inputFile.FullName).Hash -ne $sourceHashes[$relative]) {
            throw "Source changed while building: $relative. Package is not selected."
        }
    }
    $configuration = @{mcpServers=@{viavw=@{command=(Join-Path $destination 'viavw.exe');args=@('mcp')}}}
    $configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'mcp-config.example.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\STANDALONE-TOOL.md') -Destination (Join-Path $destination 'README.md')
    $runtime = (Get-Content -LiteralPath (Join-Path $destination 'viavw.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks |
        Where-Object name -eq 'Microsoft.NETCore.App' | Select-Object -ExpandProperty version
    $desktopRuntime = (Get-Content -LiteralPath (Join-Path $hostOutput 'LayerHost.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks |
        Where-Object name -eq 'Microsoft.WindowsDesktop.App' | Select-Object -ExpandProperty version
    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
    $notices = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Path $notices | Out-Null
    $noticeInputs = @{
        'dotnet-LICENSE.TXT'="microsoft.netcore.app.runtime.win-x64\$runtime\LICENSE.TXT"
        'dotnet-THIRD-PARTY-NOTICES.TXT'="microsoft.netcore.app.runtime.win-x64\$runtime\THIRD-PARTY-NOTICES.TXT"
        'windowsdesktop-LICENSE.TXT'="microsoft.windowsdesktop.app.runtime.win-x64\$desktopRuntime\LICENSE"
    }
    foreach ($name in $noticeInputs.Keys) {
        $file = Join-Path $cache $noticeInputs[$name]
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required runtime license/notice missing: $($noticeInputs[$name]). Populate the package cache before offline packaging." }
        Copy-Item -LiteralPath $file -Destination (Join-Path $notices $name)
    }
    $toolHashes = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\ViavwTool') -File | Where-Object Extension -in '.cs','.csproj') {
        $toolHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    # Include the project notice before manifest hashing; retain separate third-party notices.
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $destination 'LICENSE')
    $hashes = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($destination,$file.FullName).Replace('\','/')
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    $package = [ordered]@{
        schema=1;version=$Version;release=$Release;platform='win-x64';selfContained=$true
        sourceMode='tracked';validationStatus='Build only; application/Human acceptance not performed by packaging'
        hostBaseline=$null;hostAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $hostOutput 'LayerHost.dll')).Hash
        sourceSnapshot='tracked source';toolSourceSnapshot='tracked source';sourceHashes=$sourceHashes
        toolSourceFiles=$toolHashes;files=$hashes;createdUtc=[DateTime]::UtcNow.ToString('o')
    }
    $package | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'package.json') -Encoding utf8
    [IO.Compression.ZipFile]::CreateFromDirectory($destination,$archive)
    $receipt = [ordered]@{sourceMode='tracked';package=$destination;archive=$archive;archiveSha256=(Get-FileHash -LiteralPath $archive).Hash;sourceFileCount=$sourceHashes.Count;files=$hashes.Count;selectionChanged=$false;applicationAcceptance='not run'}
    $receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $buildFolder 'build-result.json') -Encoding utf8
    Write-Output "VIAVW_PACKAGE=$destination"
    Write-Output "VIAVW_ARCHIVE=$archive"
    Write-Output "VIAVW_BUILD_RESULT=$(Join-Path $buildFolder 'build-result.json')"
}
finally {
    $env:NUGET_PACKAGES = $cacheBefore
    $env:DOTNET_CLI_WORKLOAD_UPDATE_ADVERTISING_DISABLE = $advertisingBefore
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = $notifyBefore
}

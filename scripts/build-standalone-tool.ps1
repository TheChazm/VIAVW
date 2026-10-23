param(
    [string]$Release = 'tool-20261023a', [string]$Version = '0.1.2',
    [ValidateSet('Pinned','Tracked')][string]$SourceMode = 'Pinned',
    [string]$OutputRoot, [switch]$Offline, [string]$PackageCache
)
$ErrorActionPreference = 'Stop'
if ($SourceMode -eq 'Tracked') {
    & (Join-Path $PSScriptRoot 'build-tracked-package.ps1') -Release $Release -Version $Version -OutputRoot $OutputRoot -Offline:$Offline -PackageCache $PackageCache
    return
}
if ($OutputRoot -or $Offline -or $PackageCache) { throw 'OutputRoot, Offline and PackageCache apply only to SourceMode Tracked. Pinned mode remains unchanged.' }
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Release -notmatch '^tool-[a-z0-9-]+$') { throw 'Use a simple preserved tool release name.' }
$destination = Join-Path $projectRoot "artifacts\packages\$Release"
if (Test-Path -LiteralPath $destination) { throw 'Release already exists. Choose a fresh release name.' }
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\durable-layers-current.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($manifest.executable,'.dll')) -Algorithm SHA256).Hash -ne $manifest.assemblySha256) { throw 'Preserved host integrity failed.' }
$buildFolder = Join-Path $projectRoot "artifacts\tool-build-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $buildFolder | Out-Null
Copy-Item -Path (Join-Path $manifest.snapshot 'source\*') -Destination $buildFolder
Copy-Item -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Destination $buildFolder
$hostOutput = Join-Path $destination 'src\LayerHost\bin\Runtime\net10.0-windows'
dotnet publish (Join-Path $buildFolder 'LayerHost.csproj') -c Standalone -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $hostOutput --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Host publish failed.' }
dotnet publish (Join-Path $projectRoot 'src\ViavwTool\ViavwTool.csproj') -c $Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $destination --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Tool publish failed.' }
# Tool and owner share the same official runtime files through hardlinks where
# possible. The ZIP still contains complete independent runtime directories.
$hostFiles = Get-ChildItem -LiteralPath $hostOutput -File
foreach ($hostFile in $hostFiles) {
    $toolFile = Join-Path $destination $hostFile.Name
    if ((Test-Path -LiteralPath $toolFile) -and $hostFile.Name -notlike 'LayerHost*') {
        if ((Get-FileHash -LiteralPath $toolFile).Hash -eq (Get-FileHash -LiteralPath $hostFile.FullName).Hash) {
            Remove-Item -LiteralPath $hostFile.FullName
            New-Item -ItemType HardLink -Path $hostFile.FullName -Target $toolFile | Out-Null
        }
    }
}
$configuration = @{ mcpServers = @{ viavw = @{ command = (Join-Path $destination 'viavw.exe'); args = @('mcp') } } }
$configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'mcp-config.example.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\STANDALONE-TOOL.md') -Destination (Join-Path $destination 'README.md')
$runtimeVersion = (Get-Content -LiteralPath (Join-Path $destination 'viavw.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' } | Select-Object -ExpandProperty version
$desktopVersion = (Get-Content -LiteralPath (Join-Path $hostOutput 'LayerHost.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' } | Select-Object -ExpandProperty version
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$notices = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $packageCache "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\LICENSE.TXT") -Destination (Join-Path $notices 'dotnet-LICENSE.TXT')
Copy-Item -LiteralPath (Join-Path $packageCache "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $notices 'dotnet-THIRD-PARTY-NOTICES.TXT')
Copy-Item -LiteralPath (Join-Path $packageCache "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion\LICENSE") -Destination (Join-Path $notices 'windowsdesktop-LICENSE.TXT')
$sourceHashes = [ordered]@{}
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\ViavwTool') -File | Where-Object { $_.Extension -in '.cs','.csproj' } | ForEach-Object {
    $sourceHashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName).Hash
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $buildFolder ('tool-'+$_.Name))
}
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $destination -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($destination,$_.FullName).Replace('\','/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$package = [ordered]@{
    schema=1; version=$Version; release=$Release; platform='win-x64'; selfContained=$true
    hostBaseline=$manifest.assemblySha256; hostAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $hostOutput 'LayerHost.dll')).Hash
    sourceSnapshot=$manifest.snapshot; toolSourceFiles=$sourceHashes; toolSourceSnapshot=$buildFolder; files=$hashes; createdUtc=[DateTime]::UtcNow.ToString('o')
}
$package | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'package.json') -Encoding utf8
Write-Output "VIAVW_PACKAGE=$destination"

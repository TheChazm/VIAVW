param([switch]$SkipBuild, [string]$GroupFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$groupPath = if ($GroupFile) { [IO.Path]::GetFullPath($GroupFile) } else { Join-Path $projectRoot ('artifacts\ilspy-pairs\' + [guid]::NewGuid().ToString('N') + '\group.json') }
if (-not $groupPath.StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The pair manifest must stay inside project artifacts.' }
if (Test-Path -LiteralPath $groupPath) { throw 'Use a fresh pair manifest path.' }
$directory = Split-Path -Parent $groupPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$sessionFiles = @((Join-Path $directory 'a-session.json'), (Join-Path $directory 'b-session.json'))
if (@($sessionFiles | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0) { throw 'Use fresh session paths for both viewers.' }
function Write-PairState([string]$State, [string]$Failure) {
    @{state=$State;sessions=$sessionFiles;error=$Failure;note='Two independent viewers, private desktops, source processes and disposable configurations.'} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath ($groupPath+'.tmp')
    Move-Item -LiteralPath ($groupPath+'.tmp') -Destination $groupPath -Force
}
try {
    Write-PairState 'starting' ''
    & (Join-Path $PSScriptRoot 'ilspy-demo.ps1') -SkipBuild -StatusFile $sessionFiles[0]
    & (Join-Path $PSScriptRoot 'ilspy-demo.ps1') -SkipBuild -AllowDuplicate -StatusFile $sessionFiles[1]
    Write-PairState 'ready' ''
    Write-Output "VIAVW pair ready. Each viewer has independent control ownership. Manifest: $groupPath"
} catch {
    Write-PairState 'error' $_.Exception.Message
    throw
}

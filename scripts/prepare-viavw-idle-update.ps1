# Stop only idle stdio adapters belonging to the installed VIAVW release.
# Application layers, viewers and managers must already be closed.
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$installRoot=Join-Path $env:LOCALAPPDATA 'Programs\VIAVW'
$selection=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\artifacts\windows-installation-current.json') -Raw | ConvertFrom-Json
$tool=$selection.tool
if (-not ([IO.Path]::GetFullPath($tool).StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase))) { throw 'Installed tool is outside VIAVW.' }
$raw=& $tool call viavw_list --json '{}'
if ($LASTEXITCODE -ne 0) { throw 'Could not verify installed layers; no adapter stopped.' }
$inventory=$raw | ConvertFrom-Json
if (-not $inventory.ok -or @($inventory.result.layers | Where-Object { $_.ownerAlive -or $_.state -ne 'closed' }).Count -gt 0) { throw 'A layer needs attention; no adapter stopped.' }
$processes=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) })
foreach ($entry in $processes) {
    if ($entry.Name -ne 'viavw.exe' -or $entry.CommandLine -notmatch '\s+"?mcp"?\s*$') { throw 'A VIAVW manager or layer is still running; no adapter stopped.' }
}
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $evidence.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Use an evidence folder inside VIAVW.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$processes | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'idle-adapters.json')
foreach ($entry in $processes) {
    $current=Get-CimInstance Win32_Process -Filter ('ProcessId='+$entry.ProcessId)
    if ($current -and $current.CreationDate -eq $entry.CreationDate -and $current.ExecutablePath -eq $entry.ExecutablePath -and $current.CommandLine -eq $entry.CommandLine) { Stop-Process -Id $entry.ProcessId }
}
Write-Output ('Stopped '+$processes.Count+' idle VIAVW stdio adapters. No application layers were running.')

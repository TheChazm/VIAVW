param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot ('artifacts\session-capabilities-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Research output must stay under project artifacts.' }
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
# Query-only declarations: no enable, connect, logon, disconnect or session-switch API.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ViavwSessionQueries {
  [StructLayout(LayoutKind.Sequential)] public struct Session { public int Id; public IntPtr Station; public int State; }
  [DllImport("wtsapi32.dll", SetLastError=true)] public static extern bool WTSIsChildSessionsEnabled(out bool enabled);
  [DllImport("wtsapi32.dll", SetLastError=true)] public static extern bool WTSGetChildSessionId(out uint id);
  [DllImport("wtsapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool WTSEnumerateSessionsW(IntPtr server,int reserved,int version,out IntPtr sessions,out int count);
  [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr pointer);
  [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
}
'@
[bool]$enabled = $false
$enabledQuery = [ViavwSessionQueries]::WTSIsChildSessionsEnabled([ref]$enabled)
$enabledError = if ($enabledQuery) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
[uint32]$childId = 0
$childQuery = [ViavwSessionQueries]::WTSGetChildSessionId([ref]$childId)
$childError = if ($childQuery) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
[IntPtr]$buffer = [IntPtr]::Zero
[int]$count = 0
$sessions = @()
$enumerated = [ViavwSessionQueries]::WTSEnumerateSessionsW([IntPtr]::Zero,0,1,[ref]$buffer,[ref]$count)
$enumerateError = if ($enumerated) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
try {
  if ($enumerated) {
    $size = [Runtime.InteropServices.Marshal]::SizeOf([type][ViavwSessionQueries+Session])
    for ($index = 0; $index -lt $count; $index++) {
      $entry = [Runtime.InteropServices.Marshal]::PtrToStructure([IntPtr]::Add($buffer,$index*$size),[type][ViavwSessionQueries+Session])
      $sessions += [pscustomobject]@{ id=$entry.Id; state=$entry.State }
    }
  }
} finally { if ($buffer -ne [IntPtr]::Zero) { [ViavwSessionQueries]::WTSFreeMemory($buffer) } }
$os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$report = [ordered]@{
  queryOnly=$true; edition=$os.EditionID; build=$os.CurrentBuild; revision=$os.UBR
  currentSession=[Diagnostics.Process]::GetCurrentProcess().SessionId; activeConsoleSession=[ViavwSessionQueries]::WTSGetActiveConsoleSessionId()
  childEnabledQuerySucceeded=$enabledQuery; childEnabled=$enabled; childEnabledError=$enabledError
  childIdQuerySucceeded=$childQuery; childId=$childId; childIdError=$childError
  sessionEnumerationSucceeded=$enumerated; enumerationError=$enumerateError; sessions=$sessions
  note='Read-only capabilities inventory. No child session was enabled or created, no RDP settings changed, no account/session switched, and no compatibility was demonstrated in a second session.'
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'sessions.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5

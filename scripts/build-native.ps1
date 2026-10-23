$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vswherePath = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'An installed Visual C++ toolchain is required; none will be installed automatically.' }
$vcRoot = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
$sdkRoot = 'C:\Program Files (x86)\Windows Kits\10'
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
$compiler = Join-Path $vcRoot 'bin\Hostx64\x64\cl.exe'
$nativeOutput = Join-Path $projectRoot 'artifacts\native-build'
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$includeArguments = @('/I' + (Join-Path $vcRoot 'include'))
foreach ($component in @('ucrt','shared','um','winrt','cppwinrt')) { $includeArguments += '/I' + (Join-Path $sdkRoot "Include\$sdkVersion\$component") }
$libArguments = @(
    ('/LIBPATH:' + (Join-Path $vcRoot 'lib\x64'))
    ('/LIBPATH:' + (Join-Path $sdkRoot "Lib\$sdkVersion\um\x64"))
    ('/LIBPATH:' + (Join-Path $sdkRoot "Lib\$sdkVersion\ucrt\x64"))
)
foreach ($programName in @('D3DProbe','WgcCapture','WgcWorker'))
{
    $source = Join-Path $projectRoot "experiments\native\$programName.cpp"
    $subsystem = if ($programName -eq 'D3DProbe') { '/SUBSYSTEM:WINDOWS' } else { '/SUBSYSTEM:CONSOLE' }
    & $compiler /nologo /EHsc /std:c++20 /MD /O2 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX @includeArguments $source ('/Fo' + (Join-Path $nativeOutput "$programName.obj")) ('/Fe' + (Join-Path $nativeOutput "$programName.exe")) /link @libArguments $subsystem d3d11.lib dxgi.lib d3dcompiler.lib windowsapp.lib windowscodecs.lib ole32.lib user32.lib gdi32.lib
    if ($LASTEXITCODE -ne 0) { throw "$programName build failed" }
}

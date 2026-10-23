param([switch]$Dynamic, [switch]$Qiv)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$vswherePath='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot=& $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vsRoot){throw 'Installed Visual C++ toolchain required; nothing will be installed.'}
$vcRoot=Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory|Sort-Object Name -Descending|Select-Object -First 1 -ExpandProperty FullName
$sdkRoot='C:\Program Files (x86)\Windows Kits\10'
$sdkVersion=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory|Sort-Object Name -Descending|Select-Object -First 1 -ExpandProperty Name
$compiler=Join-Path $vcRoot 'bin\Hostx64\x64\cl.exe'
$output=Join-Path $projectRoot $(if($Qiv){'artifacts\gpu-qiv-build'}elseif($Dynamic){'artifacts\gpu-dynamic-build'}else{'artifacts\gpu-research-build'})
New-Item -ItemType Directory -Path $output -Force|Out-Null
$includes=@('/I'+(Join-Path $vcRoot 'include'))
foreach($component in @('ucrt','shared','um','winrt')){$includes+='/I'+(Join-Path $sdkRoot "Include\$sdkVersion\$component")}
$libraries=@(('/LIBPATH:'+(Join-Path $vcRoot 'lib\x64')),('/LIBPATH:'+(Join-Path $sdkRoot "Lib\$sdkVersion\um\x64")),('/LIBPATH:'+(Join-Path $sdkRoot "Lib\$sdkVersion\ucrt\x64")))
$names=if($Qiv){@('QivPresentBridge','GpuResearchHost','ModuleSnapshotTests','QivCursorGuard','QivGuardHost')}elseif($Dynamic){@('DynamicPresentBridge','GpuResearchHost','DynamicD3DProbe')}else{@('PresentBridge','GpuResearchHost')}
foreach($name in $names){
    $isLibrary=$name -like '*PresentBridge' -or $name -eq 'QivCursorGuard'
    $extra=@();if($isLibrary){$extra+='/LD'};if($Dynamic -or $Qiv){$extra+='/DVIAVW_DYNAMIC_RESEARCH'};if($Qiv -and $name -eq 'GpuResearchHost'){$extra+='/DVIAVW_QIV_RESEARCH'}
    $extension=if($isLibrary){'dll'}else{'exe'}
    $linkLibraries=@(if($name -eq 'QivGuardHost'){'bcrypt.lib';'user32.lib'}elseif($name -eq 'QivCursorGuard'){'user32.lib'}else{'d3d11.lib';'dxgi.lib';'d3dcompiler.lib';'windowscodecs.lib';'ole32.lib';'user32.lib';'gdi32.lib'})
    & $compiler /nologo /EHsc /std:c++20 /MD /O2 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX @extra @includes (Join-Path $projectRoot "experiments\native\$name.cpp") ('/Fo'+(Join-Path $output "$name.obj")) ('/Fe'+(Join-Path $output "$name.$extension")) /link @libraries ('/IMPLIB:'+(Join-Path $output "$name.lib")) @linkLibraries
    if($LASTEXITCODE -ne 0){throw "$name build failed"}
}
# A disposable byte-identical work copy keeps normal native rebuilds independent.
# The accepted snapshot itself is never written or rebuilt.
$acceptedProbe=Join-Path $projectRoot 'artifacts\baselines\accepted-capture-timing-20261014\native-build\D3DProbe.exe'
if(-not(Test-Path -LiteralPath $acceptedProbe)){throw 'The preserved accepted D3DProbe is required.'}
if($Qiv){Write-Output 'Separate pinned qIV capture build complete.'}
elseif(-not $Dynamic){Copy-Item -LiteralPath $acceptedProbe -Destination (Join-Path $output 'D3DProbe.exe')}
else{(Get-FileHash -LiteralPath (Join-Path $output 'DynamicD3DProbe.exe') -Algorithm SHA256).Hash|Set-Content -LiteralPath (Join-Path $output 'DynamicD3DProbe.sha256') -Encoding ascii}

"""Build the stable entry point with explicitly configurable installed MSVC/SDK tools."""
import os
from pathlib import Path
import re
import subprocess
import sys
ROOT=Path(__file__).resolve().parents[1]


def build(destination, *, vswhere=None, msvc_root=None, sdk_root=None, sdk_version=None):
    destination=Path(destination).resolve()
    if destination.exists():raise ValueError('Launcher output must be a fresh directory')
    if msvc_root:
        msvc=Path(msvc_root).resolve()
    else:
        finder=Path(vswhere or r'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe')
        if not finder.is_file():raise FileNotFoundError('Existing vswhere is required; supply --vswhere or --msvc-root. No tools are installed.')
        location=subprocess.check_output([str(finder),'-latest','-products','*','-requires','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','-property','installationPath'],text=True).strip()
        if not location:raise FileNotFoundError('No installed MSVC x64 toolchain found; supply --msvc-root.')
        versions=sorted((Path(location)/'VC/Tools/MSVC').glob('*'))
        if not versions:raise FileNotFoundError('Installed MSVC version directory is missing')
        msvc=versions[-1]
    sdk=Path(sdk_root or r'C:\Program Files (x86)\Windows Kits\10').resolve()
    if sdk_version and not re.fullmatch(r'\d+(?:\.\d+){3}',sdk_version):raise ValueError('Invalid Windows SDK version')
    versions=sorted((sdk/'Include').glob('*'))
    if not sdk_version:
        if not versions:raise FileNotFoundError('Windows SDK Include directory is missing; supply --windows-sdk-root/version.')
        sdk_version=versions[-1].name
    compiler=msvc/'bin/Hostx64/x64/cl.exe'
    includes=[msvc/'include',sdk/'Include'/sdk_version/'ucrt',sdk/'Include'/sdk_version/'shared',sdk/'Include'/sdk_version/'um']
    libraries=[msvc/'lib/x64',sdk/'Lib'/sdk_version/'ucrt/x64',sdk/'Lib'/sdk_version/'um/x64']
    if not compiler.is_file() or not all(p.is_dir() for p in includes+libraries):
        raise FileNotFoundError('Existing x64 compiler/SDK headers/libraries are incomplete; no installation is attempted.')
    destination.mkdir(parents=True)
    env=dict(os.environ,INCLUDE=';'.join(map(str,includes)),LIB=';'.join(map(str,libraries)))
    subprocess.run([str(compiler),'/nologo','/O2','/MT','/EHsc','/std:c++17','/W4','/DUNICODE','/D_UNICODE',str(ROOT/'src/ViavwLauncher/main.cpp'),'/Fe:'+str(destination/'viavw.exe'),'/Fo:'+str(destination/'main.obj'),'/link','shell32.lib','/DYNAMICBASE','/NXCOMPAT','/Brepro'],env=env,check=True,cwd=destination)
    return destination/'viavw.exe'


if __name__=='__main__':print(build(sys.argv[1]))

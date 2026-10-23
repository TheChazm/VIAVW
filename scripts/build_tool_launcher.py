"""Build the small stable Windows entry point using the installed MSVC compiler."""
import os, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def build(destination):
    destination=Path(destination).resolve();destination.mkdir()
    vs=Path(subprocess.check_output([r'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe','-latest','-products','*','-requires','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','-property','installationPath'],text=True).strip())
    msvc=sorted((vs/'VC/Tools/MSVC').iterdir())[-1]
    sdk=Path(r'C:\Program Files (x86)\Windows Kits\10');version=sorted((sdk/'Include').iterdir())[-1].name
    env=dict(os.environ,INCLUDE=';'.join(str(p) for p in [msvc/'include',sdk/'Include'/version/'ucrt',sdk/'Include'/version/'shared',sdk/'Include'/version/'um']),LIB=';'.join(str(p) for p in [msvc/'lib/x64',sdk/'Lib'/version/'ucrt/x64',sdk/'Lib'/version/'um/x64']))
    subprocess.run([str(msvc/'bin/Hostx64/x64/cl.exe'),'/nologo','/O2','/MT','/EHsc','/std:c++17','/W4','/DUNICODE','/D_UNICODE',str(ROOT/'src/ViavwLauncher/main.cpp'),'/Fe:'+str(destination/'viavw.exe'),'/Fo:'+str(destination/'main.obj'),'/link','shell32.lib','/DYNAMICBASE','/NXCOMPAT','/Brepro'],env=env,check=True,cwd=destination)
    return destination/'viavw.exe'
if __name__=='__main__':print(build(sys.argv[1]))

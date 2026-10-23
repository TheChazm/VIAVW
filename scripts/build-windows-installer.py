"""Build a standard per-user Windows MSI around the verified portable tool."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile
from build_tool_launcher import build as build_launcher

ROOT=Path(__file__).resolve().parents[1]
name=sys.argv[1] if len(sys.argv)>1 else 'installer-20261023a'
testing='--test' in sys.argv[2:]
version='0.1.2'
registry_key=r'Software\VIAVW'+('-InstallerTest' if testing else '')
product_name='VIAVW'+(' Installer Test' if testing else '')
if not name.replace('-','').isalnum():raise ValueError('Use a simple new build name')
build=ROOT/'artifacts'/name
build.mkdir()
selection=json.loads((ROOT/'artifacts/standalone-tool-current.json').read_text())
archive=Path(selection['archive'])
if hashlib.sha256(archive.read_bytes()).hexdigest().upper()!=selection['archiveSha256']:raise ValueError('Selected archive integrity failed')
release=json.loads(Path(selection['manifest']).read_text(encoding='utf-8-sig'))['release']
payload=build/'payload';payload.mkdir()
with zipfile.ZipFile(archive) as package:package.extractall(payload)
launcher=build_launcher(build/'launcher')
(build/'viavw-current.txt').write_text('releases\\'+release+'\\viavw.exe\n',encoding='ascii')
desktop=build/'desktop'
if '--desktop-from' in sys.argv:
    source_desktop=Path(sys.argv[sys.argv.index('--desktop-from')+1]).resolve()
    if not source_desktop.is_relative_to(ROOT/'artifacts'):raise ValueError('Reuse only an owned preserved manager build')
    shutil.copytree(source_desktop,desktop)
else:
    subprocess.run(['dotnet','publish',str(ROOT/'src/ViavwDesktop/ViavwDesktop.csproj'),'-c',name,'-r','win-x64','--self-contained','true','-o',str(desktop),'--nologo','-v','minimal'],check=True)
host=payload/'src/LayerHost/bin/Runtime/net10.0-windows'
for source in desktop.rglob('*'):
    if not source.is_file():continue
    relative=source.relative_to(desktop);existing=host/relative
    if source.name.startswith('VIAVW.Manager'):
        shutil.copy2(source,existing)
    elif not existing.exists() or hashlib.sha256(source.read_bytes()).digest()!=hashlib.sha256(existing.read_bytes()).digest():
        raise ValueError('Manager requires a different runtime: '+str(relative))
# Bundled runtime files are reused unchanged. The accepted tool manifest stays intact.
(build/'notice.rtf').write_text(r'{\rtf1\ansi\deff0 {\fonttbl {\f0 Segoe UI;}}\f0\fs22 VIAVW application layers\par\par This research application is installed for your Windows account. Application compatibility varies.\par\par Close VIAVW layers before uninstalling. User-created documents and layer state are retained. No driver or Windows service is installed.\par\par Bundled Microsoft runtime licenses and notices are included in the installation.}',encoding='ascii')
NS='http://wixtoolset.org/schemas/v4/wxs';UI=NS+'/ui'
ET.register_namespace('',NS);ET.register_namespace('ui',UI)
def add(parent,tag,**attrs):return ET.SubElement(parent,'{'+NS+'}'+tag,{key:str(value) for key,value in attrs.items()})
wix=ET.Element('{'+NS+'}Wix')
product='{'+str(uuid.uuid5(uuid.NAMESPACE_URL,'viavw.local/windows-installer/'+version+('/test' if testing else ''))).upper()+'}'
upgrade='{'+str(uuid.uuid5(uuid.NAMESPACE_URL,'viavw.local/windows-installer/test-upgrade')).upper()+'}' if testing else '{98E0B628-818D-4C97-9120-D881CF03E74B}'
package=add(wix,'Package',Name=product_name,Manufacturer='VIAVW',Version=version,Language='1033',Scope='perUser',UpgradeCode=upgrade,ProductCode=product,InstallerVersion='500',Compressed='yes')
add(package,'MajorUpgrade',DowngradeErrorMessage='A newer VIAVW version is already installed.')
add(package,'MediaTemplate',EmbedCab='yes',CompressionLevel='high')
add(package,'Property',Id='MSIRESTARTMANAGERCONTROL',Value='DisableShutdown')
location=add(package,'Property',Id='INSTALLFOLDER',Secure='yes')
add(location,'RegistrySearch',Id='PreviousInstallFolder',Root='HKCU',Key=registry_key,Name='InstallPath',Type='raw',Bitness='always64')
local=add(package,'StandardDirectory',Id='LocalAppDataFolder');programs=add(local,'Directory',Id='UserPrograms',Name='Programs');install=add(programs,'Directory',Id='INSTALLFOLDER',Name='VIAVW');releases=add(install,'Directory',Id='Releases',Name='releases');release_dir=add(releases,'Directory',Id='RELEASEFOLDER',Name=release)
menu=add(add(package,'StandardDirectory',Id='ProgramMenuFolder'),'Directory',Id='VIAVWMenu',Name=product_name)
feature=add(package,'Feature',Id='Main',Title='VIAVW',Level='1')
root_files=[]
for filename,source in [('viavw.exe',launcher),('viavw-current.txt',build/'viavw-current.txt')]:
    identifier='Stable'+('Launcher' if filename.endswith('.exe') else 'Selection')
    component=add(install,'Component',Id=identifier,Guid='{'+str(uuid.uuid5(uuid.NAMESPACE_URL,'viavw.local/root/'+filename+('/test' if testing else ''))).upper()+'}',Bitness='always64')
    add(component,'File',Id=identifier+'File',Source=str(source),Name=filename)
    add(component,'RegistryValue',Root='HKCU',Key=registry_key,Name=identifier,Type='integer',Value='1',KeyPath='yes')
    add(feature,'ComponentRef',Id=identifier)
    root_files.append(dict(path=filename,sha256=hashlib.sha256(source.read_bytes()).hexdigest().upper()))
directories={'':release_dir};directory_ids={'':'RELEASEFOLDER'};owned=[]
for file in sorted(payload.rglob('*')):
    if not file.is_file():continue
    relative=file.relative_to(payload);parent=''
    for part in relative.parts[:-1]:
        current=(parent+'/'+part).strip('/')
        if current not in directories:
            identifier='D'+hashlib.sha256(current.encode()).hexdigest()[:24]
            directories[current]=add(directories[parent],'Directory',Id=identifier,Name=part);directory_ids[current]=identifier
        parent=current
    identifier=hashlib.sha256(str(relative).encode()).hexdigest()[:24]
    component_guid='{'+str(uuid.uuid5(uuid.NAMESPACE_URL,'viavw.local/component/'+release+'/'+relative.as_posix()+('/test' if testing else ''))).upper()+'}'
    component=add(directories[parent],'Component',Id='C'+identifier,Guid=component_guid,Bitness='always64')
    is_manager=relative.as_posix()=='src/LayerHost/bin/Runtime/net10.0-windows/VIAVW.Manager.exe'
    file_id='ManagerExe' if is_manager else 'F'+identifier
    add(component,'File',Id=file_id,Source=str(file),Name=file.name)
    add(component,'RegistryValue',Root='HKCU',Key=registry_key+r'\InstallerFiles',Name=identifier,Type='integer',Value='1',KeyPath='yes')
    add(feature,'ComponentRef',Id='C'+identifier)
    owned.append(dict(path=relative.as_posix(),sha256=hashlib.sha256(file.read_bytes()).hexdigest().upper()))
shortcut=add(install,'Component',Id='Shortcuts',Guid='*',Bitness='always64')
add(shortcut,'Shortcut',Id='StartMenuShortcut',Directory='VIAVWMenu',Name='VIAVW',Target='[#ManagerExe]',WorkingDirectory='RELEASEFOLDER')
add(shortcut,'Shortcut',Id='FolderShortcut',Directory='INSTALLFOLDER',Name='VIAVW',Target='[#ManagerExe]',WorkingDirectory='RELEASEFOLDER')
add(shortcut,'RegistryValue',Root='HKCU',Key=registry_key,Name='Installed',Type='integer',Value='1',KeyPath='yes')
add(shortcut,'RegistryValue',Root='HKCU',Key=registry_key,Name='InstallPath',Type='string',Value='[INSTALLFOLDER]')
add(shortcut,'RemoveFolder',Id='RemoveMenu',Directory='VIAVWMenu',On='uninstall')
for path,identifier in {'INSTALLFOLDER':'INSTALLFOLDER','Releases':'Releases',**{p:directory_ids[p] for p in directory_ids}}.items():
    add(shortcut,'RemoveFolder',Id='Remove'+identifier,Directory=identifier,On='uninstall')
add(shortcut,'RemoveFile',Id='RemoveConnection',Directory='INSTALLFOLDER',Name='mcp-config.json',On='uninstall')
add(shortcut,'RemoveFile',Id='RemoveInstalledInfo',Directory='INSTALLFOLDER',Name='installed.json',On='uninstall')
add(feature,'ComponentRef',Id='Shortcuts')
add(package,'CustomAction',Id='CheckRunningVIAVW',FileRef='ManagerExe',ExeCommand='--check-idle --quiet',Execute='immediate',Return='check',Impersonate='yes')
add(package,'CustomAction',Id='WriteConnection',FileRef='ManagerExe',ExeCommand='--write-connection',Execute='immediate',Return='check',Impersonate='yes')
sequence=add(package,'InstallExecuteSequence')
add(sequence,'Custom',Action='CheckRunningVIAVW',Before='InstallValidate',Condition='Installed AND (REMOVE~="ALL" OR REINSTALL)')
add(sequence,'Custom',Action='WriteConnection',After='InstallFinalize',Condition='NOT REMOVE~="ALL"')
ET.SubElement(package,'{'+UI+'}WixUI',{'Id':'WixUI_InstallDir','InstallDirectory':'INSTALLFOLDER'})
add(package,'WixVariable',Id='WixUILicenseRtf',Value=str(build/'notice.rtf'))
wxs=build/'VIAVW.wxs';ET.ElementTree(wix).write(wxs,encoding='utf-8',xml_declaration=True)
installer=build/('VIAVW-Setup-'+version+('-test' if testing else '')+'-x64.msi')
result=subprocess.run([str(ROOT/'artifacts/installer-tools/wix.exe'),'build',str(wxs),'-arch','x64','-ext','WixToolset.UI.wixext','-intermediatefolder',str(build/'wix-temp'),'-out',str(installer)],capture_output=True,text=True,cwd=ROOT)
(build/'wix-build.log').write_text(result.stdout+result.stderr,encoding='utf-8')
if result.returncode:
    print('\n'.join(dict.fromkeys((result.stdout+result.stderr).splitlines()))[-8000:])
    raise RuntimeError('MSI build failed; see '+str(build/'wix-build.log'))
report=dict(installer=str(installer),sha256=hashlib.sha256(installer.read_bytes()).hexdigest().upper(),bytes=installer.stat().st_size,productCode=product,version=version,release=release,sourceArchive=str(archive),sourceArchiveSha256=selection['archiveSha256'],files=owned,rootFiles=root_files,testing=testing,menuName=product_name)
(build/'installer.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('VIAVW_INSTALLER='+str(installer),flush=True)

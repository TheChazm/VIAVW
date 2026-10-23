"""Exercise actual per-user Windows Installer install, repair and removal."""
import hashlib
import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid
import winreg

ROOT=Path(__file__).resolve().parents[1]
build=Path(sys.argv[1]).resolve();metadata=json.loads((build/'installer.json').read_text());installer=Path(metadata['installer'])
if not metadata.get('testing'):raise ValueError('Use an isolated --test installer; never uninstall the real installation in a test')
out=ROOT/'artifacts'/('installer-verification-'+uuid.uuid4().hex);out.mkdir()
destination=out/'Installed VIAVW';release=destination/'releases'/metadata['release'];tool=destination/'viavw.exe';runtime=release/'src/LayerHost/bin/Runtime/net10.0-windows';manager=runtime/'VIAVW.Manager.exe'
checks={};failure=None;layer=None;steps=[]

def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)

def msi(operation,label,extra=None):
    args=[str(Path(os.environ['SystemRoot'])/'System32/msiexec.exe'),operation,str(installer) if operation!='/x' else metadata['productCode'],'/qn','/norestart','/L*v',str(out/(label+'.log'))]
    command=subprocess.list2cmdline(args)
    # Windows Installer parses PROPERTY="value with spaces" itself rather than
    # accepting the whole "PROPERTY=value with spaces" token produced by the
    # standard Windows argv encoder. No shell is involved.
    if extra:
        for field in extra:
            key,value=field.split('=',1)
            if not key.isupper() or '"' in value:raise ValueError('Invalid MSI property')
            command+=' '+key+'="'+value+'"'
    result=subprocess.run(command,capture_output=True,timeout=120);steps.append(dict(action=label,exit=result.returncode));print(label+' exit '+str(result.returncode),flush=True);return result.returncode

def call(name,data):
    request=out/('request-'+uuid.uuid4().hex+'.json');request.write_text(json.dumps(data))
    result=subprocess.run([str(tool),'call',name,'--file',str(request)],capture_output=True,text=True,encoding='utf-8',timeout=100)
    response=json.loads(result.stdout)
    if not response['ok']:raise RuntimeError(response)
    return response['result']

def registry_exists():
    for hive in (winreg.HKEY_CURRENT_USER,winreg.HKEY_LOCAL_MACHINE):
        try:
            with winreg.OpenKey(hive,'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\'+metadata['productCode']):return True
        except FileNotFoundError:pass
    return False

def install_context(context):
    query=ctypes.WinDLL('msi').MsiGetProductInfoExW
    query.argtypes=[ctypes.c_wchar_p,ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_wchar_p,ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_uint)]
    text=ctypes.create_unicode_buffer(1024);length=ctypes.c_uint(1024)
    return query(metadata['productCode'],None,context,'InstallDate',text,ctypes.byref(length))==0

try:
    check('installer matches frozen build hash',hashlib.sha256(installer.read_bytes()).hexdigest().upper()==metadata['sha256'])
    check('standard per-user MSI installs without a reboot',msi('/i','install',['INSTALLFOLDER='+str(destination)])==0)
    check('all installed release files match shipped hashes',all(hashlib.sha256((release/file['path']).read_bytes()).hexdigest().upper()==file['sha256'] for file in metadata['files']))
    check('stable launcher and release selection match shipped hashes',all(hashlib.sha256((destination/file['path']).read_bytes()).hexdigest().upper()==file['sha256'] for file in metadata['rootFiles']))
    check('installed tool integrity check',json.loads(subprocess.check_output([str(tool),'doctor'],text=True,encoding='utf-8'))['passed'])
    connection=json.loads((destination/'mcp-config.json').read_text())
    check('installer writes ready local tool connection',connection['mcpServers']['viavw']==dict(command=str(tool),args=['mcp']))
    ui=json.loads(subprocess.check_output([str(manager),'--self-test'],text=True,encoding='utf-8',timeout=30))
    check('installed Windows manager constructs all eight actions and connects',ui['passed'] and ui['buttons']==8 and ui['connectionFile']==str(destination/'mcp-config.json'))
    shortcut=Path(os.environ['APPDATA'])/'Microsoft/Windows/Start Menu/Programs'/metadata['menuName']/'VIAVW.lnk'
    check('Start menu and installation folder shortcuts created',shortcut.is_file() and (destination/'VIAVW.lnk').is_file())
    check('Windows Installed apps registration uses per-user installation context',registry_exists() and install_context(2) and not install_context(4))
    fixture=release/'artifacts'/'installer-test-document'
    subprocess.run([str(runtime/'LayerHost.exe'),'--prepare-installed-notepad-gdi-demo',str(fixture)],check=True,capture_output=True,timeout=20)
    setup=json.loads((fixture/'launch.json').read_text(encoding='utf-8-sig'))
    layer=call('viavw_create',dict(executable=setup['Executable'],arguments=setup['Arguments']))['layer']
    pid=call('viavw_status',dict(layer=layer))['data']['session']['applicationPid']
    check('installed tool starts actual Notepad++',pid>0)
    check('uninstall is blocked while installed layer infrastructure is live',msi('/x','blocked-uninstall')==1603)
    check('blocked uninstall preserves same running application',call('viavw_status',dict(layer=layer))['data']['session']['applicationPid']==pid)
    check('blocked uninstall leaves every shipped file intact',all(hashlib.sha256((release/file['path']).read_bytes()).hexdigest().upper()==file['sha256'] for file in metadata['files']))
    check('installed app closes normally',call('viavw_close',dict(layer=layer))['data']['result']['closed']);layer=None
    check('standard Windows Installer repair succeeds',msi('/fa','repair')==0)
    check('repaired manager still ready',json.loads(subprocess.check_output([str(manager),'--self-test'],text=True,encoding='utf-8',timeout=30))['passed'])
    document=release/'artifacts'/'preserve-user-document.txt';document.write_text('Preserve user-created content on removal.\n')
    key=release/'artifacts/tool-layers/observation-key.bin';key_bytes=key.read_bytes()
    check('standard per-user uninstall succeeds without a reboot',msi('/x','uninstall')==0)
    check('uninstall removes installed executables',not tool.exists() and not manager.exists())
    check('uninstall preserves documents and layer data',document.read_text()=='Preserve user-created content on removal.\n' and key.read_bytes()==key_bytes and Path(setup['Document']).is_file())
    check('uninstall removes Windows registration and owned shortcuts',not registry_exists() and not shortcut.exists() and not (destination/'VIAVW.lnk').exists())
except Exception:
    failure=traceback.format_exc();print(failure,flush=True)
finally:
    if layer and tool.exists():
        try:call('viavw_close',dict(layer=layer,force=True))
        except Exception as error:print('Disposable cleanup needs attention:',error,flush=True)
    if failure and registry_exists():
        try:msi('/x','failed-test-cleanup')
        except Exception:pass
    passed=failure is None and all(checks.values())
    (out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,steps=steps,failure=failure,installer=str(installer),destination=str(destination),physicalHumanAcceptance='pending'),indent=2))
    print('VIAVW_EVIDENCE='+str(out/'verification.json'),flush=True)
    if not passed:raise SystemExit(1)

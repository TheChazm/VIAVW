"""Install a verified MSI and register only VIAVW's Codex tool entry."""
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tomllib
import uuid

ROOT=Path(__file__).resolve().parents[1]
build=Path(sys.argv[1]).resolve();evidence=Path(sys.argv[2]).resolve()
metadata=json.loads((build/'installer.json').read_text());test=json.loads(evidence.read_text())
if not test['passed']:raise ValueError('Installer acceptance did not pass')
if test['installer']!=metadata['installer']:
    tested=json.loads((Path(test['installer']).parent/'installer.json').read_text())
    if not tested.get('testing') or metadata.get('testing') or any(tested[k]!=metadata[k] for k in ('sourceArchiveSha256','files','rootFiles','release','version')):raise ValueError('Isolated installer payload differs from production')
source=Path(metadata['installer']);digest=hashlib.sha256(source.read_bytes()).hexdigest().upper()
if digest!=metadata['sha256']:raise ValueError('Installer changed after verification')
distribution=ROOT/'dist';distribution.mkdir(exist_ok=True);installer=distribution/source.name
if installer.exists() and hashlib.sha256(installer.read_bytes()).hexdigest().upper()!=digest:raise ValueError('Preserve the existing distributed installer')
if not installer.exists():shutil.copy2(source,installer)
destination=Path(os.environ['LOCALAPPDATA'])/'Programs/VIAVW';release=destination/'releases'/metadata['release'];tool=destination/'viavw.exe';manager=release/'src/LayerHost/bin/Runtime/net10.0-windows/VIAVW.Manager.exe'
out=ROOT/'artifacts'/('local-install-'+uuid.uuid4().hex);out.mkdir()
if '"' in str(destination):raise ValueError('Invalid installation directory')
args=[str(Path(os.environ['SystemRoot'])/'System32/msiexec.exe'),'/i',str(installer),'/qn','/norestart','/L*v',str(out/'install.log')]
command=subprocess.list2cmdline(args)+' INSTALLFOLDER="'+str(destination)+'"'
result=subprocess.run(command,timeout=120)
if result.returncode!=0:raise RuntimeError('Windows installation did not finish cleanly: '+str(result.returncode))
print('Installed VIAVW at '+str(destination),flush=True)
if not all(hashlib.sha256((release/file['path']).read_bytes()).hexdigest().upper()==file['sha256'] for file in metadata['files']):raise ValueError('Installed file integrity failed')
if not all(hashlib.sha256((destination/file['path']).read_bytes()).hexdigest().upper()==file['sha256'] for file in metadata['rootFiles']):raise ValueError('Stable entry point integrity failed')
doctor=json.loads(subprocess.check_output([str(tool),'doctor'],text=True,encoding='utf-8',timeout=20))
if not doctor['passed']:raise ValueError('Installed runtime verification failed')
ui=json.loads(subprocess.check_output([str(manager),'--self-test'],text=True,encoding='utf-8',timeout=20))
if not ui['passed']:raise ValueError('Installed manager verification failed')
connection=json.loads((destination/'mcp-config.json').read_text())
if connection['mcpServers']['viavw']!=dict(command=str(tool),args=['mcp']):raise ValueError('Installed connection path mismatch')
codex=shutil.which('codex')
if not codex:raise RuntimeError('VIAVW installed, but Codex CLI is unavailable for registration')
config=Path(os.environ.get('CODEX_HOME',str(Path.home()/'.codex')))/'config.toml'
before=tomllib.loads(config.read_text(encoding='utf-8')) if config.exists() else {}
existing=before.get('mcp_servers',{}).get('viavw')
if existing and existing.get('command')!=str(tool):
    previous=Path(existing.get('command','')).resolve()
    if not previous.is_relative_to(destination/'releases') or previous.name.lower()!='viavw.exe':raise RuntimeError('An existing VIAVW tool entry points outside the installation; preserved it for review')
backup=config.with_name('config.toml.viavw-before-'+uuid.uuid4().hex)
if config.exists():shutil.copy2(config,backup)
subprocess.run([codex,'mcp','add','viavw','--',str(tool),'mcp'],check=True,capture_output=True,text=True)
after=tomllib.loads(config.read_text(encoding='utf-8'))
# Codex's writer omits an existing empty command-argument array. Restore that
# representation so registering VIAVW preserves other server entries exactly
# as parsed, not only their equivalent default behavior.
text=config.read_text(encoding='utf-8');restored=False
for name,server in before.get('mcp_servers',{}).items():
    current=after.get('mcp_servers',{}).get(name,{})
    if server.get('args')==[] and 'args' not in current:
        if not re.fullmatch(r'[A-Za-z0-9_-]+',name):raise ValueError('Review normalized MCP entry: '+name)
        header='[mcp_servers.'+name+']'
        if text.count(header+'\n')!=1:raise ValueError('Cannot preserve MCP entry formatting: '+name)
        text=text.replace(header+'\n',header+'\nargs = []\n',1);restored=True
if restored:
    temporary=config.with_name('config.toml.viavw-registration-'+uuid.uuid4().hex+'.tmp');temporary.write_text(text,encoding='utf-8');os.replace(temporary,config)
    after=tomllib.loads(config.read_text(encoding='utf-8'))
def unrelated(data):
    data=json.loads(json.dumps(data,default=str));servers=data.get('mcp_servers',{});servers.pop('viavw',None)
    if not servers:data.pop('mcp_servers',None)
    return data
if unrelated(before)!=unrelated(after):raise ValueError('Unexpected configuration change; inspect private backup '+str(backup))
registered=json.loads(subprocess.check_output([codex,'mcp','get','viavw','--json'],text=True,encoding='utf-8'))
transport=registered['transport']
if transport['command']!=str(tool) or transport['args']!=['mcp'] or not registered['enabled']:raise ValueError('Registered tool path does not match installed VIAVW')
report=dict(passed=True,installationFolder=str(destination),releaseRoot=str(release),tool=str(tool),manager=str(manager),installer=str(installer),installerSha256=digest,installerVersion=metadata['version'],productCode=metadata['productCode'],installedFilesVerified=len(metadata['files']),managerSelfTest=ui['passed'],runtimeFilesVerified=len(doctor['files']),codexRegistered=True,codexConfiguration=str(config),privateConfigBackup=str(backup),unrelatedCodexSettingsPreserved=True,connectionFile=str(destination/'mcp-config.json'),CharlieAI='unchanged',activeCodexReload='May require app restart or a new session; current conversation was not interrupted',installerEvidence=str(evidence))
(out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
for filename in ('windows-installation-current.json','windows-installer-current.json'):
    previous=ROOT/'artifacts'/filename
    if previous.exists():(out/('previous-'+filename)).write_bytes(previous.read_bytes())
(ROOT/'artifacts/windows-installation-current.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
(ROOT/'artifacts/windows-installer-current.json').write_text(json.dumps(dict(schema=1,version=metadata['version'],installer=str(installer),sha256=digest,evidence=str(evidence),build=str(build),sourceArchive=metadata['sourceArchive']),indent=2),encoding='utf-8')
print('Registered VIAVW in Codex; all other settings preserved.',flush=True)
print('VIAVW_EVIDENCE='+str(out/'verification.json'),flush=True)

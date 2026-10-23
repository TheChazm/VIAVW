"""Select only an exact verified standalone package, archive and check relocation."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid
import zipfile

ROOT=Path(__file__).resolve().parents[1]
package=Path(sys.argv[1]).resolve();raw=Path(sys.argv[2]).resolve();sdk=Path(sys.argv[3]).resolve()
if package.parent!=ROOT/'artifacts/packages':raise ValueError('Use an owned preserved package')
manifest=json.loads((package/'package.json').read_text(encoding='utf-8-sig'))
direct=json.loads(raw.read_text());official=json.loads(sdk.read_text())
if not direct['passed'] or not official['passed'] or Path(direct['sourcePackage'])!=package or Path(direct['package'])!=Path(official['package']):raise ValueError('Exact package verification did not pass')
if (package/'package.json').read_bytes()!=(Path(official['package'])/'package.json').read_bytes():raise ValueError('Tested package manifest differs')
for name,digest in manifest['files'].items():
    for directory in (package,Path(official['package'])):
        target=(directory/name).resolve()
        if not target.is_relative_to(directory) or hashlib.sha256(target.read_bytes()).hexdigest().upper()!=digest:raise ValueError('Packaged/tested file differs: '+name)
for name,digest in manifest['toolSourceFiles'].items():
    if hashlib.sha256((ROOT/'src/ViavwTool'/name).read_bytes()).hexdigest().upper()!=digest:raise ValueError('Source changed after verified build: '+name)
archive=package.parent/('VIAVW-Tool-'+manifest['version']+'-'+manifest['release']+'-win-x64.zip')
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as zipped:
    # Only release files. No test apps, secrets, live layer data or SDK included.
    for name in [*manifest['files'],'package.json']:zipped.write(package/name,name)
verification=ROOT/'artifacts'/('standalone-archive-'+uuid.uuid4().hex);verification.mkdir()
extracted=verification/'VIAVW Tool'
with zipfile.ZipFile(archive) as zipped:zipped.extractall(extracted)
env=dict(os.environ,PATH=str(Path(os.environ['SystemRoot'])/'System32'),DOTNET_ROOT=str(verification/'no-runtime'),DOTNET_ROOT_X64=str(verification/'no-runtime'))
doctor=subprocess.run([str(extracted/'viavw.exe'),'doctor'],capture_output=True,text=True,encoding='utf-8',env=env,timeout=30)
doctor_data=json.loads(doctor.stdout)
if doctor.returncode or not doctor_data['passed']:raise ValueError('Extracted archive failed integrity/runtime check')
tools=subprocess.run([str(extracted/'viavw.exe'),'tools'],capture_output=True,text=True,encoding='utf-8',env=env,timeout=15)
if tools.returncode or len(json.loads(tools.stdout)['tools'])!=13:raise ValueError('Extracted archive tool discovery failed')
report=dict(passed=True,archive=str(archive),archiveSha256=hashlib.sha256(archive.read_bytes()).hexdigest().upper(),archiveBytes=archive.stat().st_size,extractedReleaseBytes=sum((package/name).stat().st_size for name in [*manifest['files'],'package.json']),fileCount=len(manifest['files'])+1,doctor=doctor_data,toolCount=13,evidence=[str(raw),str(sdk)])
(verification/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
selected=dict(schema=1,status='verified standalone MCP and CLI package; physical Human acceptance pending',version=manifest['version'],release=manifest['release'],executable=str(package/'viavw.exe'),archive=str(archive),archiveSha256=report['archiveSha256'],manifest=str(package/'package.json'),toolAssemblySha256=manifest['files']['viavw.dll'],hostAssemblySha256=manifest['hostAssemblySha256'],evidence=[str(raw),str(sdk),str(verification/'verification.json')],documentation=str(ROOT/'docs/STANDALONE-TOOL.md'),CharlieAI='unchanged')
selection=ROOT/'artifacts/standalone-tool-current.json'
if selection.exists():(verification/'previous-selection.json').write_bytes(selection.read_bytes())
temporary=selection.with_suffix('.tmp');temporary.write_text(json.dumps(selected,indent=2),encoding='utf-8');os.replace(temporary,selection)
print(json.dumps({k:v for k,v in report.items() if k!='doctor'},indent=2))
print('VIAVW_EVIDENCE='+str(verification/'verification.json'))

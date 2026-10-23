"""Two independent installed apps: extract an archive while editing a document."""
import concurrent.futures
import json
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid
import zipfile
from viavw_layers import ROOT,Layers,checked_host
host=checked_host(sys.argv[1]);out=ROOT/'artifacts'/('layer-workflows-'+uuid.uuid4().hex);out.mkdir();checks={};layers=[];clients=[];failure=None;owned=set()
def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)
def connect(layer):
    c=layer.connect();clients.append(c);s=c.request('status',layer.session)['session'];owned.update(s['ownedProcessIds']);return c
def observe(c,layer,title=None,capture=False):
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        obs=c.observe(layer.session,capture=capture)
        if title is None or any(w['Handle']==obs.data['session']['root'] and w['Text']==title for w in obs.data['result']['windows']):return obs
        time.sleep(.03)
    raise TimeoutError('Expected application surface: '+str(title))
def send(c,layer,kind,**fields):return c.act(observe(c,layer),kind,**fields)
try:
    expected={'greeting.txt':'Independent archive extraction verified.\n','nested/details.txt':'Second disposable member.\n'};archive=out/'sample.zip'
    with zipfile.ZipFile(archive,'x') as z:
        for name,text in expected.items():z.writestr(name,text)
    subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(out/'editor')],check=True,capture_output=True);setup=json.loads((out/'editor/launch.json').read_text(encoding='utf-8-sig'))
    editor_layer=Layers(host).create(setup['Executable'],setup['Arguments']);layers.append(editor_layer);editor_client=connect(editor_layer)
    zip_layer=Layers(host).create(r'C:\Program Files\7-Zip\7zFM.exe',[str(archive)]);layers.append(zip_layer);zip_client=connect(zip_layer)
    first=observe(zip_client,zip_layer,capture=True);listview=next(w['Handle'] for w in first.data['result']['windows'] if w['ClassName']=='SysListView32');editor=next(w['Handle'] for w in observe(editor_client,editor_layer).data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
    check('applications have independent desktops and PIDs',first.data['session']['layerId']!=observe(editor_client,editor_layer).data['session']['layerId'] and first.data['session']['applicationPid']!=observe(editor_client,editor_layer).data['session']['applicationPid'])
    def edit_document():
        for i in range(8):
            send(editor_client,editor_layer,'chord',window=editor,keys=[17,65]);send(editor_client,editor_layer,'text',window=editor,text=f'Independent editor iteration {i}\nSecond line');send(editor_client,editor_layer,'chord',window=editor,keys=[17,83]);time.sleep(.08)
        return Path(setup['Document']).read_text(encoding='utf-8-sig')=='Independent editor iteration 7\nSecond line'
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending=pool.submit(edit_document)
        send(zip_client,zip_layer,'click',window=listview,x=70,y=54,deliveryMode='posted');send(zip_client,zip_layer,'chord',keys=[17,65]);send(zip_client,zip_layer,'key',key=116)
        dialog=observe(zip_client,zip_layer,title='Copy',capture=True);check('ordinary archive extraction dialog is captured',dialog.data['session']['root']!=first.data['session']['root'] and Path(dialog.data['result']['image']['path']).stat().st_size>1000)
        send(zip_client,zip_layer,'chord',keys=[17,65]);send(zip_client,zip_layer,'text',text=str(out/'extracted')+'\\');send(zip_client,zip_layer,'key',key=13)
        deadline=time.monotonic()+10
        while time.monotonic()<deadline and not all((out/'extracted'/name).is_file() for name in expected):time.sleep(.03)
        check('actual extracted files match archive members',all((out/'extracted'/name).is_file() and (out/'extracted'/name).read_text(encoding='utf-8')==text for name,text in expected.items()))
        check('independent editor saves exact text during extraction',pending.result(timeout=10))
    check('same application PIDs retained throughout workflows',all(c.request('status',l.session)['session']['applicationPid']==json.loads((l.descriptor.parent/'layer-record.json').read_text(encoding='utf-8'))['applicationPid'] for c,l in [(editor_client,editor_layer),(zip_client,zip_layer)]))
    for c in clients:c.close()
    for layer in layers:check('graceful close '+layer.session,layer.close_application()['result']['closed'])
except Exception:failure=traceback.format_exc();print(failure,flush=True)
finally:
    for c in clients:c.close()
    if failure:
        for layer in layers:
            try:
                with layer.connect() as c:c.request('shutdown',force=True)
            except Exception:pass
    time.sleep(1.1)
from importlib.util import spec_from_file_location,module_from_spec
spec=spec_from_file_location('durable_cleanup',ROOT/'scripts/test-durable-layers.py');helpers=module_from_spec(spec);spec.loader.exec_module(helpers)
survivors=[p for p in owned if helpers.alive(p)];passed=failure is None and not survivors and all(checks.values())
(out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,failure=failure,survivors=survivors,physicalHumanAcceptance='pending'),indent=2),encoding='utf-8');print(json.dumps(dict(passed=passed,output=str(out),survivors=survivors)),flush=True)
print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
if not passed:raise SystemExit(1)

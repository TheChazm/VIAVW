"""Installed Visual Studio editing and live-owner replacement through protocol 2."""
import json
from pathlib import Path
import subprocess
import time
import traceback
import uuid
from viavw_layers import ROOT,Layers,read_metadata
out=ROOT/'artifacts'/('durable-vs-'+uuid.uuid4().hex);out.mkdir();checks={};layer=None;client=None;failure=None;images=[]
def check(name,value):
 checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
 if not value:raise AssertionError(name)
def observe(**kw):
 o=client.observe(layer.session,**kw).data
 if 'image' in o['result'] and o['result']['image']:images.append(o['result']['image']['path'])
 return o
def nodes(o):return read_metadata(o['result']['accessibilityPath'])['nodes']
def send(kind,**fields):return client.act(client.observe(layer.session),kind,**fields)
def wait(condition,seconds=20):
 deadline=time.monotonic()+seconds
 while time.monotonic()<deadline:
  if condition():return
  time.sleep(.05)
 raise TimeoutError('Visual Studio did not reach expected state')
def title_contains(text):
 o=observe();return any(w['Handle']==o['session']['root'] and text in w['Text'] for w in o['result']['windows'])
try:
 from viavw_layers import checked_host
 host=checked_host();subprocess.run([str(host),'--prepare-vs-runnable-with-profile',str(out/'fixture'),str(ROOT/'artifacts/vs-m30-runnable/launch.json')],capture_output=True,check=True)
 setup=read_metadata(out/'fixture/launch.json');first=Path(setup['First']);second=Path(setup['Second'])
 layer=Layers().create(profile='visual-studio',setupFile=str(out/'fixture/launch.json'));client=layer.connect();pid=client.request('status',layer.session)['session']['applicationPid'];wait(lambda:title_contains('Greeting.cs'),60)
 def editor_ready():
  global ready,editor_node
  ready=observe(accessibility=True,capture=True);editor_node=next((n for n in nodes(ready) if n.get('id')=='WpfTextView' and 'public static class Greeting' in n.get('text','') and not n.get('offscreen')),None);return editor_node is not None
 wait(editor_ready,30);origin=ready['result']['sourceClientOrigin'];send('click',x=int(editor_node['x'])+80-origin['X'],y=int(editor_node['y'])+8-origin['Y']);send('chord',keys=[17,65])
 wait(lambda:any(n.get('id')=='WpfTextView' and any(n.get('selectionCoversDocument',[])) for n in nodes(observe(accessibility=True))))
 check('installed VS editor selection is observed before editing',True)
 send('text',text='// Durable Visual Studio proof');send('key',key=13);send('text',text='// second linex');send('key',key=8);send('chord',keys=[17,83]);expected='// Durable Visual Studio proof\n// second line';wait(lambda:first.read_text(encoding='utf-8-sig')==expected)
 check('durable API edits Enter Backspace and saves actual VS file',True)
 send('text',text='_unsaved');check('VS has unsaved application memory',first.read_text(encoding='utf-8-sig')==expected)
 client.close();client=None;layer=layer.replace_owner();layer.reconcile(lambda d:d['session']['applicationPid']==pid and any(w['Handle']==d['session']['root'] and 'Greeting.cs' in w['Text'] for w in d['result']['windows']) and Path(d['result']['image']['path']).stat().st_size>1000);client=layer.connect();send('chord',keys=[17,83]);wait(lambda:first.read_text(encoding='utf-8-sig')==expected+'_unsaved')
 check('VS same process and actual unsaved buffer survive controller replacement',client.request('status',layer.session)['session']['applicationPid']==pid)
 send('chord',keys=[17,79]);wait(lambda:title_contains('Open File'));send('chord',keys=[17,65]);send('text',text=str(second));send('key',key=13);wait(lambda:title_contains('Details.cs'));wait(lambda:any(n.get('id')=='WpfTextView' and 'public static class Details' in n.get('text','') and not n.get('offscreen') for n in nodes(observe(accessibility=True))));time.sleep(.05);observe(capture=True);check('durable VS ordinary Open File dialog and actual editor navigation work',True)
 client.close();client=None;closed=layer.close_application()
 if not closed['result']['closed']:
  # IDE helpers may outlive its saved main process; do not force a live editor.
  with layer.connect() as c:
   wait(lambda:c.request('status',layer.session)['session']['state'] in ('application_root_exited','closed'),10)
  layer.close_application(force=True)
 check('saved VS and owned helpers are closed',True)
except Exception:failure=traceback.format_exc();print(failure,flush=True)
finally:
 if client:client.close()
 if failure and layer:
  try:
   with layer.connect() as c:c.request('shutdown',force=True)
  except Exception:pass
passed=failure is None and all(checks.values());(out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,failure=failure,images=images,descriptor=str(layer.descriptor) if layer else None,scope='Installed VS 2022 with existing disposable experimental software-rendering profile; protocol-2 edit/save, actual unsaved memory across owner replacement and Open File. Physical human acceptance pending.'),indent=2),encoding='utf-8');print(json.dumps(dict(passed=passed,output=str(out))),flush=True)
if not passed:raise SystemExit(1)

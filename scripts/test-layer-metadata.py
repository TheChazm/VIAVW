"""Actual layer recovery while metadata readers overlap atomic publications."""
import concurrent.futures
import json
from pathlib import Path
import subprocess
import sys
import threading
import time
import traceback
import uuid
from viavw_layers import ROOT,Layers,read_metadata,write_metadata,resolve_keeper
host=Path(sys.argv[1]).resolve();out=ROOT/'artifacts'/('layer-metadata-'+uuid.uuid4().hex);out.mkdir();layer=None;failure=None;checks={};reads=0;errors=[];stop=threading.Event();thread=None

def check(name,value):
 checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
 if not value:raise AssertionError(name)
try:
 subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(out/'fixture')],check=True,capture_output=True);setup=read_metadata(out/'fixture/launch.json');document=Path(setup['Document'])
 layer=Layers(host).create(setup['Executable'],setup['Arguments']);record=layer.descriptor.parent/'layer-record.json'
 with layer.connect() as c:
  obs=c.observe(layer.session,capture=True);pid=obs.data['session']['applicationPid'];editor=next(w['Handle'] for w in obs.data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
  c.act(obs,'chord',window=editor,keys=[17,65]);c.act(c.observe(layer.session),'text',window=editor,text='Unsaved metadata contention proof')
 # An ordinary non-delete-sharing reader must delay, rather than kill, adoption.
 with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
  with layer.descriptor.open('rb') as held:
   pending=pool.submit(layer.replace_owner);time.sleep(.25);check('ordinary held reader delays owner publication',not pending.done())
  layer=pending.result(timeout=12)
 check('owner publishes successfully after reader releases',read_metadata(layer.descriptor)['pid']>0)
 keeper_file,_=resolve_keeper(layer.keeper)
 with layer.connect() as c,concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
  state=c.request('status',layer.session)['session']
  with keeper_file.open('rb') as held:
   pending=pool.submit(c.request,'ownership',layer.session,expectedRevision=state['revision'],owner='Human');time.sleep(.2);check('ordinary keeper reader postpones ownership persistence',not pending.done())
  pending.result(timeout=5);_,metadata=resolve_keeper(layer.keeper);check('Human ownership is persisted after contention',metadata['snapshot']['owner']=='Human')
  state=c.request('status',layer.session)['session'];c.request('ownership',layer.session,expectedRevision=state['revision'],owner='Automation')
 with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
  data=read_metadata(record);data['contentionProof']=True
  with record.open('rb') as held:
   pending=pool.submit(write_metadata,record,data);time.sleep(.2);check('registry publisher tolerates ordinary reader',not pending.done())
  pending.result(timeout=3)
 check('registry update stays a complete snapshot',read_metadata(record)['contentionProof'] is True)
 def read_continuously():
  global reads
  while not stop.is_set():
   try:
    read_metadata(layer.descriptor);read_metadata(record);resolve_keeper(layer.keeper);reads+=1
   except Exception as error:errors.append(repr(error));break
   time.sleep(.001)
 thread=threading.Thread(target=read_continuously);thread.start()
 for _ in range(20):layer=layer.replace_owner()
 stop.set();thread.join(timeout=3)
 check('twenty replacements survive concurrent shared metadata readers',not errors and reads>100)
 layer.reconcile(lambda data:data['session']['applicationPid']==pid and any(w['Handle']==editor for w in data['result']['windows']) and Path(data['result']['image']['path']).stat().st_size>1000)
 with layer.connect() as c:c.act(c.observe(layer.session),'chord',window=editor,keys=[17,83])
 time.sleep(.1);check('actual unsaved app memory survives all metadata contention',document.read_text(encoding='utf-8-sig')=='Unsaved metadata contention proof')
 check('contended layer closes gracefully',layer.close_application()['result']['closed'])
except Exception:failure=traceback.format_exc();print(failure,flush=True)
finally:
 stop.set()
 if thread:thread.join(timeout=3)
 if failure and layer:
  try:
   with layer.connect() as c:c.request('shutdown',force=True)
  except Exception:
   try:subprocess.run([str(host),'--discard-owned-layer',str(layer.keeper),'--force'],capture_output=True,timeout=10)
   except Exception:pass
passed=failure is None and all(checks.values())
(out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,failure=failure,metadataReads=reads,readerErrors=errors,physicalHumanAcceptance='pending'),indent=2),encoding='utf-8');print(json.dumps(dict(passed=passed,output=str(out))),flush=True)
print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
if not passed:raise SystemExit(1)

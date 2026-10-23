"""A pending native save prompt must retain Human ownership after owner death."""
import json
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid
from viavw_layers import ROOT,LayerClient,LayerError,start_owner,resolve_keeper
host=Path(sys.argv[1]).resolve();out=ROOT/'artifacts'/('layer-save-prompt-'+uuid.uuid4().hex);out.mkdir();checks={};owners=[];client=None;keeper=None;owned=set();failure=None

def check(name,value):
 checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
 if not value:raise AssertionError(name)
def start(label,adopt=None):
 global client
 descriptor,process=start_owner(out/label,host,adopt=adopt);owners.append((descriptor,process));owned.add(process.pid);client=LayerClient(descriptor,host);return client
def state():
 s=client.request('status',sid)['session'];owned.update(s['ownedProcessIds']);return s
def send(kind,window=None,**fields):
 s=state();return client.request('input',sid,window=window or s['root'],expectedRevision=s['revision'],expectedInputSequence=s['inputSequence'],kind=kind,**fields)
try:
 subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(out/'fixture')],check=True,capture_output=True);setup=json.loads((out/'fixture/launch.json').read_text(encoding='utf-8-sig'));document=Path(setup['Document'])
 client=start('initial');sid=client.request('create',executable=setup['Executable'],arguments=setup['Arguments'])['id'];first=client.observe(sid,capture=True).data;pid=first['session']['applicationPid'];keeper=first['session']['keeperDescriptor'];editor=next(w['Handle'] for w in first['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible']);state()
 send('chord',editor,keys=[17,65]);send('text',editor,text='Unsaved native prompt recovery')
 result=client.request('close',sid,expectedRevision=state()['revision']);check('graceful close preserves unsaved app and requests Human',not result['result']['closed'] and result['result']['needsHuman'] and result['session']['owner']=='Human')
 check('pending save prompt opens its Human viewer',result['session']['viewerVisible'])
 prompt=client.observe(sid,capture=True).data;check('native save prompt is actually captured',prompt['session']['root']!=first['session']['root'] and Path(prompt['result']['image']['path']).stat().st_size>1000)
 _,metadata=resolve_keeper(keeper);check('save-prompt Human ownership is persisted before owner loss',metadata['snapshot']['owner']=='Human')
 client.close();owners[-1][1].kill();owners[-1][1].wait();client=start('replacement',keeper);s=state();check('save prompt and same app survive owner replacement',s['applicationPid']==pid and s['owner']=='Human' and s['recoveryPending'])
 observed=client.observe(sid,capture=True).data;rejected=False
 try:client.request('reconcile',sid,expectedRevision=observed['session']['revision'],observationToken=observed['result']['recoveryToken'],**{'continue':True})
 except LayerError as error:rejected=error.code=='human_owns_input'
 check('automation cannot reclaim a pending Human save prompt',rejected)
 # Explicit programmatic return; this does not claim a physical human action.
 client.request('ownership',sid,expectedRevision=state()['revision'],owner='Automation');observed=client.observe(sid,capture=True).data
 client.request('reconcile',sid,expectedRevision=observed['session']['revision'],observationToken=observed['result']['recoveryToken'],**{'continue':True});send('key',key=27)
 deadline=time.monotonic()+3
 while time.monotonic()<deadline:
  actual=client.observe(sid,capture=True).data
  if actual['session']['root']==first['session']['root']:break
  time.sleep(.02)
 check('native Cancel restores editor without losing buffer',actual['session']['root']==first['session']['root'])
 send('chord',editor,keys=[17,83]);time.sleep(.1);check('actual previously unsaved document is preserved',document.read_text(encoding='utf-8-sig')=='Unsaved native prompt recovery')
 check('resolved application closes gracefully',client.request('close',sid,expectedRevision=state()['revision'])['result']['closed']);client.request('shutdown');client.close();owners[-1][1].wait(timeout=10)
except Exception:failure=traceback.format_exc();print(failure,flush=True)
finally:
 if client:client.close()
 for descriptor,process in owners:
  if process.poll() is None:
   try:
    with LayerClient(descriptor,host) as cleanup:cleanup.request('shutdown',force=True)
    process.wait(timeout=10)
   except Exception:process.kill();process.wait()
 if failure and keeper:
  try:subprocess.run([str(host),'--discard-owned-layer',str(keeper),'--force'],capture_output=True,timeout=10)
  except Exception:pass
 time.sleep(1.1)
from importlib.util import spec_from_file_location,module_from_spec
spec=spec_from_file_location('cleanup',ROOT/'scripts/test-durable-layers.py');helpers=module_from_spec(spec);spec.loader.exec_module(helpers)
survivors=[p for p in owned if helpers.alive(p)];passed=failure is None and not survivors and all(checks.values())
(out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,failure=failure,survivors=survivors,physicalHumanAcceptance='pending'),indent=2),encoding='utf-8');print(json.dumps(dict(passed=passed,output=str(out),survivors=survivors)),flush=True)
print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
if not passed:raise SystemExit(1)

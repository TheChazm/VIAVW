"""Exercise the public factory, durable owner slot and real Notepad++ saved state."""
import json
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid
from viavw_layers import ROOT,Layers,LayerClient,LayerError,checked_host

host=checked_host(sys.argv[1] if len(sys.argv)>1 else None)
out=ROOT/'artifacts'/('layer-client-'+uuid.uuid4().hex);out.mkdir()
checks={};layer=None;failure=None;measurements={}
def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)
try:
    subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(out/'fixture')],check=True,capture_output=True)
    setup=json.loads((out/'fixture/launch.json').read_text(encoding='utf-8-sig'));document=Path(setup['Document'])
    layer=Layers(host).create(setup['Executable'],setup['Arguments'])
    with layer.connect() as c:
        obs=c.observe(layer.session,capture=True);pid=obs.data['session']['applicationPid'];editor=next(w['Handle'] for w in obs.data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
        check('factory registers actual application',any(r.get('applicationPid')==pid and r['state']=='owned' for r in Layers(host).list()))
        c.act(obs,'chord',window=editor,keys=[17,65])
        rejected=False
        try:c.act(obs,'text',window=editor,text='STALE INPUT')
        except LayerError as error:rejected=error.outcome=='not_started'
        check('same-owner stale observation cannot send another input',rejected)
        c.act(c.observe(layer.session),'text',window=editor,text='alpha\rbravo\ncharlie\r\ndelta\t\U0001f600')
        c.act(c.observe(layer.session),'chord',window=editor,keys=[17,83]);time.sleep(.1)
        check('actual text preserves CR LF CRLF tab and Unicode',document.read_text(encoding='utf-8-sig')=='alpha\nbravo\ncharlie\ndelta\t\U0001f600')
        c.act(c.observe(layer.session),'chord',window=editor,keys=[17,65])
        c.act(c.observe(layer.session),'text',window=editor,text='Public factory saved')
        c.act(c.observe(layer.session),'chord',window=editor,keys=[17,83]);time.sleep(.1)
        c.act(c.observe(layer.session),'text',window=editor,text='_unsaved')
        check('test contains unsaved app memory',document.read_text(encoding='utf-8-sig')=='Public factory saved')
    check('client context exits without ending application',layer.connect().close() is None)
    started=time.monotonic();previous=layer;layer=layer.replace_owner();measurements['replaceOwnerMilliseconds']=(time.monotonic()-started)*1000
    check('default owner replacement reuses original management address',layer.descriptor==previous.descriptor)
    with layer.connect() as c:
        state=c.request('status',layer.session)['session'];check('replacement retains same actual application',state['applicationPid']==pid and state['recoveryPending'])
        rejected=False
        try:c.act(obs,'text',window=editor,text='OLD OWNER')
        except ValueError:rejected=True
        check('old-owner observation rejected locally',rejected)
    # Verify actual app surface identity before accepting continuation. Subsequent
    # saved bytes independently prove that its unsaved buffer was retained.
    layer.reconcile(lambda data:data['session']['applicationPid']==pid and any(w['Handle']==editor and w['ClassName']=='Scintilla' for w in data['result']['windows']) and Path(data['result']['image']['path']).stat().st_size>1000)
    with layer.connect() as c:
        c.act(c.observe(layer.session),'chord',window=editor,keys=[17,83]);time.sleep(.1)
        check('public replacement preserves exact unsaved memory',document.read_text(encoding='utf-8-sig')=='Public factory saved_unsaved')
    layer.show(human=True,reason='Automated handoff check; physical acceptance remains pending.')
    with layer.connect() as c:
        state=c.request('status',layer.session)['session'];check('public handoff opens viewer with Human ownership',state['owner']=='Human' and state['viewerVisible'])
        rejected=False
        try:c.act(c.observe(layer.session),'text',window=editor,text='HUMAN DENIED')
        except LayerError as e:rejected=e.code=='human_owns_input'
        check('automation cannot type during Human ownership',rejected)
        c.request('viewer',layer.session,visible=False)
        c.request('ownership',layer.session,expectedRevision=state['revision'],owner='Automation')
    result=layer.close_application();check('public graceful close succeeds',result['result']['closed'])
    time.sleep(.2);check('registry reports completed layer',next(r for r in Layers(host).list() if r.get('descriptor')==str(layer.descriptor))['state']=='closed_or_unavailable')
except Exception:
    failure=traceback.format_exc();print(failure,flush=True)
finally:
    if layer:
        try:
            with layer.connect() as c:c.request('shutdown',force=True)
        except Exception:pass
passed=failure is None and all(checks.values())
(out/'verification.json').write_text(json.dumps(dict(passed=passed,checks=checks,measurements=measurements,failure=failure,descriptor=str(layer.descriptor) if layer else None,physicalHumanAcceptance='pending'),indent=2),encoding='utf-8')
print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
print(json.dumps(dict(passed=passed,output=str(out))),flush=True)
if not passed:raise SystemExit(1)

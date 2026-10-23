"""Actual protocol-2 transport/parallel-client verification without starting applications."""
import concurrent.futures
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid
from viavw_layers import ROOT,LayerClient,checked_host
host=checked_host(sys.argv[1]);out=ROOT/'artifacts'/('layer-transport-'+uuid.uuid4().hex);out.mkdir();descriptor=out/'owner/layer-owner.json';checks={};failure=None;process=None
try:
    # This starts only a control endpoint, never an application/desktop/worker.
    # Application admission is independently enforced by the host's create path.
    with (out/'owner.log').open('xb') as log:process=subprocess.Popen([str(host),'--layer-service',str(descriptor.parent)],stdin=subprocess.DEVNULL,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    deadline=time.monotonic()+10
    while not descriptor.exists():
        if process.poll() is not None or time.monotonic()>deadline:raise RuntimeError('Control-only owner failed to start')
        time.sleep(.01)
    with LayerClient(descriptor,host) as c:
        checks['empty owner has no application']=c.request('list')==[]
        started=time.monotonic()
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            replies=list(pool.map(lambda _:c.request('list'),range(4200)))
        milliseconds=(time.monotonic()-started)*1000
        checks['concurrent callers serialize 4200 actual requests']=all(r==[] for r in replies) and c.next_sequence==4202
        expired=c.raw(dict(protocol=2,id='1',epoch=c.epoch,op='list'))
        checks['expired response never reexecutes']=not expired['ok'] and expired['error']['code']=='result_expired'
        checks['owner remains usable after expired response']=c.request('list')==[]
    with LayerClient(descriptor,host) as c:
        checks['reconnect resumes owner sequence']=c.next_sequence==4203
        c.request('shutdown')
    process.wait(timeout=10);checks['control owner shuts down']=process.returncode==0
except Exception as error:failure=repr(error)
finally:
    if process and process.poll() is None:
        try:
            with LayerClient(descriptor,host) as c:c.request('shutdown')
            process.wait(timeout=10)
        except Exception:process.kill();process.wait()
report=dict(passed=failure is None and all(checks.values()),checks=checks,failure=failure,milliseconds=locals().get('milliseconds'),scope='Control endpoint/client only; no application admission bypass or application compatibility claim.')
(out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(dict(report=report,output=str(out)),indent=2))
print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
if not report['passed']:raise SystemExit(1)

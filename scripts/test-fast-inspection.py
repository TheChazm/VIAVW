"""Exercise nonblocking inspection through Codex's persistent console on installed 7-Zip."""
import json
from pathlib import Path
import queue
import runpy
import subprocess
import sys
import threading
import time
import uuid
from viavw_client import ROOT

output = ROOT / 'artifacts' / ('fast-inspection-console-' + uuid.uuid4().hex)
output.mkdir()
runpy.run_path(str(ROOT / 'scripts/prepare-installed-app-fixtures.py'))['prepare'](output / 'fixtures', media=False)
exe = Path(sys.argv[1]).resolve()
process = subprocess.Popen([sys.executable, '-u', str(ROOT / 'scripts/viavw-console.py'), str(output / 'host'), '--host', str(exe)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
replies = queue.Queue()
threading.Thread(target=lambda: [replies.put(json.loads(line)) for line in process.stdout], daemon=True).start()
stderr = []
threading.Thread(target=lambda: [stderr.append(line) for line in process.stderr], daemon=True).start()
checks, timings, transcript, owned = {}, [], [], {process.pid}
sequence = 0
failure = None

def check(name, value):
    checks[name] = bool(value)
    if not value:
        raise AssertionError(name)
    print('PASS', name, flush=True)

def call(op, **fields):
    global sequence
    sequence += 1
    command = dict(id='fast-' + str(sequence), op=op, **fields)
    started = time.perf_counter()
    process.stdin.write(json.dumps(command) + '\n')
    process.stdin.flush()
    response = replies.get(timeout=30)
    elapsed = (time.perf_counter()-started)*1000
    transcript.append(response)
    timings.append(dict(op=op, milliseconds=elapsed))
    if not response['ok']:
        raise RuntimeError(response)
    state = response.get('data', {}).get('session', {})
    owned.update(state.get('ownedProcessIds', []))
    return response['data'], elapsed

try:
    check('persistent console ready', replies.get(timeout=20)['ready'])
    hello, _ = call('hello')
    check('asynchronous inspection advertised', all(op in hello['operations'] for op in ('inspect-begin','inspect-poll')))
    created, _ = call('create', launch=dict(executable=r'C:\Program Files\7-Zip\7zFM.exe', arguments=[str(output/'fixtures/sample.zip')]))
    session = created['id']
    observation, _ = call('observe', session=session)
    for i in range(6):
        begun, begin_ms = call('inspect-begin', session=session)
        ticket = begun['result']['ticket']
        if i == 0:
            editor = next(w for w in observation['result']['windows'] if w['ClassName']=='SysListView32')
            call('input', observationId=observation['observationId'], kind='click', fields=dict(window=editor['Handle'],x=70,y=54,deliveryMode='posted'))
        deadline = time.monotonic()+5
        while True:
            polled, poll_ms = call('inspect-poll', session=session, ticket=ticket)
            inspection = polled['result']['inspection']
            if inspection['workerPid']:
                owned.add(inspection['workerPid'])
            if inspection['status'] not in ('pending','partial'):
                break
            if time.monotonic()>deadline:
                raise TimeoutError('Inspection not bounded')
            time.sleep(.025)
        check('actual UIA nodes '+str(i), inspection['status']=='complete' and len(inspection['result']['nodes'])>0)
        check('console begin/poll below 100 ms '+str(i), begin_ms<100 and poll_ms<100)
        if i == 0:
            check('input during inspection makes result explicitly stale', polled['result']['stale'])
        else:
            check('unchanged input context remains current '+str(i), not polled['result']['stale'])
    begun, _ = call('inspect-begin', session=session)
    call('human', observationId=observation['observationId'], reason='Programmatic inspection ownership check')
    polled, _ = call('inspect-poll', session=session, ticket=begun['result']['ticket'])
    check('ownership transition makes pending evidence stale', polled['result']['stale'])
    call('shutdown')
    check('graceful console shutdown', process.wait(timeout=20)==0)
except Exception as error:
    failure = repr(error)
finally:
    if process.poll() is None:
        try:
            call('shutdown', force=True)
            process.wait(timeout=20)
        except Exception:
            process.kill()
            process.wait(timeout=10)
    alive = runpy.run_path(str(ROOT/'scripts/test-workspace.py'))['alive']
    until = time.monotonic()+5
    while any(alive(pid) for pid in owned) and time.monotonic()<until:
        time.sleep(.025)
    survivors = [pid for pid in owned if alive(pid)]
    result = dict(passed=failure is None and not survivors, failure=failure, checks=checks, timings=timings, ownedProcessIds=sorted(owned), survivors=survivors, stderr=''.join(stderr), humanAcceptance='pending; programmatic ownership only')
    (output/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    (output/'transcript.json').write_text(json.dumps(transcript,indent=2),encoding='utf-8')
    print(json.dumps(dict(passed=result['passed'],failure=failure,survivors=survivors,output=str(output))),flush=True)
    if not result['passed']:
        raise SystemExit(1)

"""Timed actual Notepad++ edit/save endurance, controller reconnects and owner replacement."""
import argparse
import ctypes
import json
import math
from pathlib import Path
import subprocess
import time
import traceback
import uuid
from viavw_layers import ROOT,Layers,LayerError,checked_host,write_metadata

def resources(pids):
    kernel=ctypes.WinDLL('kernel32',use_last_error=True);psapi=ctypes.WinDLL('psapi',use_last_error=True)
    kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    kernel.GetProcessTimes.argtypes=[ctypes.c_void_p]+[ctypes.POINTER(ctypes.c_uint64)]*4
    kernel.GetProcessHandleCount.argtypes=[ctypes.c_void_p,ctypes.POINTER(ctypes.c_uint32)]
    class Counters(ctypes.Structure):
        _fields_=[('cb',ctypes.c_uint32),('faults',ctypes.c_uint32)]+[(n,ctypes.c_size_t) for n in ['peakWorking','working','peakPaged','paged','peakNonpaged','nonpaged','pagefile','peakPagefile','private']]
    psapi.GetProcessMemoryInfo.argtypes=[ctypes.c_void_p,ctypes.POINTER(Counters),ctypes.c_uint32]
    result={}
    for pid in pids:
        handle=kernel.OpenProcess(0x1000|0x10,False,pid)
        if not handle:continue
        try:
            values=[ctypes.c_uint64() for _ in range(4)];handles=ctypes.c_uint32();memory=Counters();memory.cb=ctypes.sizeof(memory)
            if not kernel.GetProcessTimes(handle,*[ctypes.byref(v) for v in values]):continue
            if not psapi.GetProcessMemoryInfo(handle,ctypes.byref(memory),memory.cb):continue
            kernel.GetProcessHandleCount(handle,ctypes.byref(handles))
            result[str(pid)]=dict(created=values[0].value,cpuSeconds=(values[2].value+values[3].value)/1e7,privateBytes=memory.private,workingSet=memory.working,handles=handles.value)
        finally:kernel.CloseHandle(handle)
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--host');parser.add_argument('--minutes',type=float,default=20);args=parser.parse_args()
    if not math.isfinite(args.minutes) or args.minutes<=0:parser.error('Use a positive duration')
    host=checked_host(args.host);out=ROOT/'artifacts'/('layer-endurance-'+uuid.uuid4().hex);out.mkdir();layer=None;failure=None;samples=[];iterations=0;replacements=0;client=None;owned=set();identities={};timings=[];started=time.monotonic()
    try:
        subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(out/'fixture')],check=True,capture_output=True)
        setup=json.loads((out/'fixture/launch.json').read_text(encoding='utf-8-sig'));document=Path(setup['Document'])
        layer=Layers(host).create(setup['Executable'],setup['Arguments']);client=layer.connect();obs=client.observe(layer.session,capture=True);pid=obs.data['session']['applicationPid'];editor=next(w['Handle'] for w in obs.data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible']);owned.update(obs.data['session']['ownedProcessIds'])
        deadline=time.monotonic()+args.minutes*60
        while time.monotonic()<deadline:
            iteration_start=time.monotonic();text=f'Durable ordinary editing iteration {iterations}\r\nSecond line: abc'
            for kind,fields in [('chord',dict(keys=[17,65])),('text',dict(text=text)),('key',dict(key=8)),('key',dict(key=13)),('text',dict(text='Continued')),('chord',dict(keys=[17,83]))]:
                client.act(client.observe(layer.session),kind,window=editor,**fields)
            expected=text[:-1]+'\r\nContinued';save_deadline=time.monotonic()+3
            while document.read_text(encoding='utf-8-sig').replace('\r\n','\n')!=expected.replace('\r\n','\n'):
                if time.monotonic()>save_deadline:raise AssertionError('Actual saved document mismatch at iteration '+str(iterations))
                time.sleep(.01)
            timings.append((time.monotonic()-iteration_start)*1000);iterations+=1
            state=client.request('status',layer.session)['session'];owned.update(state['ownedProcessIds'])
            if state['applicationPid']!=pid:raise AssertionError('Application was restarted')
            if iterations%10==0:
                image=client.observe(layer.session,capture=True).data['result']['image']
                if Path(image['path']).stat().st_size<1000:raise AssertionError('Missing visual evidence')
                current_resources=resources(state['ownedProcessIds']);identities.update({p:r['created'] for p,r in current_resources.items()})
                samples.append(dict(elapsedSeconds=time.monotonic()-started,iteration=iterations,processes=current_resources))
                write_metadata(out/'progress.json',dict(iterations=iterations,samples=samples))
                print('Verified edit/save iterations:',iterations,flush=True)
            if iterations%15==0:client.close();client=layer.connect()
            if iterations%60==0:
                client.close();layer=layer.replace_owner();replacements+=1
                layer.reconcile(lambda d:d['session']['applicationPid']==pid and any(w['Handle']==editor for w in d['result']['windows']) and Path(d['result']['image']['path']).stat().st_size>1000)
                client=layer.connect()
            # Deliberate workflow pacing, not a saturated typing benchmark.
            time.sleep(min(1,max(0,deadline-time.monotonic())))
        client.close();client=None
        if not layer.close_application()['result']['closed']:raise AssertionError('Graceful close failed')
    except Exception:failure=traceback.format_exc();print(failure,flush=True)
    finally:
        if client:client.close()
        if failure and layer:
            try:
                with layer.connect() as c:c.request('shutdown',force=True)
            except Exception:
                subprocess.run([str(host),'--discard-owned-layer',str(layer.keeper),'--force'],capture_output=True,timeout=10)
    time.sleep(1.1)
    survivors=[p for p,r in resources(owned).items() if identities.get(p)==r['created']]
    ordered=sorted(timings)
    report=dict(passed=failure is None and not survivors,failure=failure,survivors=survivors,elapsedSeconds=time.monotonic()-started,requestedMinutes=args.minutes,verifiedEditSaveCycles=iterations,ownerReplacements=replacements,workflowMilliseconds=dict(median=ordered[len(ordered)//2],maximum=max(ordered)) if ordered else {},samples=samples,scope='Installed Notepad++ real editing/backspace/Enter/saving, on-demand capture, reconnect and owner replacement. No physical-human acceptance or application-crash/reboot survival claim.')
    (out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(dict(passed=report['passed'],output=str(out))),flush=True)
    print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
    if not report['passed']:raise SystemExit(1)
if __name__=='__main__':main()

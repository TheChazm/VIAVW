"""Official MCP Python SDK interoperability; SDK is test-only, never shipped."""
import asyncio
import base64
import ctypes
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import statistics
import site
import subprocess
import sys
import time
import traceback
import uuid
import zipfile

ROOT=Path(__file__).resolve().parents[1]
from app_test_paths import application, add_sdk_path
add_sdk_path(ROOT)
from mcp import ClientSession,StdioServerParameters
from mcp.client.stdio import stdio_client
from jsonschema import Draft202012Validator

package=Path(sys.argv[1]).resolve();exe=package/'viavw.exe';host=package/'src/LayerHost/bin/Runtime/net10.0-windows/LayerHost.exe'
if len(sys.argv)>2:exe=Path(sys.argv[2]).resolve()
out=ROOT/'artifacts'/('standalone-sdk-'+uuid.uuid4().hex);out.mkdir()
checks={};layers=[];owned=set();images=[];failure=None;measurements={}

def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)

def stop_exact_owner(info):
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.restype=ctypes.c_void_p;kernel.GetProcessTimes.argtypes=[ctypes.c_void_p]+[ctypes.POINTER(ctypes.c_uint64)]*4
    kernel.TerminateProcess.argtypes=[ctypes.c_void_p,ctypes.c_uint32];kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
    handle=kernel.OpenProcess(0x1000|0x100000|1,False,info['pid'])
    if not handle:raise ctypes.WinError(ctypes.get_last_error())
    try:
        times=[ctypes.c_uint64() for _ in range(4)]
        if not kernel.GetProcessTimes(handle,*[ctypes.byref(t) for t in times]) or times[0].value!=info['createdFileTime']:raise RuntimeError('Owner identity mismatch: no process terminated')
        if not kernel.TerminateProcess(handle,3):raise ctypes.WinError(ctypes.get_last_error())
        if kernel.WaitForSingleObject(handle,5000)!=0:raise RuntimeError('Owner termination unconfirmed')
    finally:kernel.CloseHandle(handle)

async def main():
    env=dict(os.environ,PATH=str(Path(os.environ['SystemRoot'])/'System32'),DOTNET_ROOT=str(out/'no-dotnet'),DOTNET_ROOT_X64=str(out/'no-dotnet'))
    parameters=StdioServerParameters(command=str(exe),args=['mcp'],cwd=str(out),env=env)
    async with stdio_client(parameters) as (read,write):
        async with ClientSession(read,write) as client:
            initialized=await client.initialize();check('official SDK lifecycle negotiation',initialized.serverInfo.name=='viavw')
            listing=await client.list_tools();check('official SDK parses all 13 tool schemas',len(listing.tools)==13)
            for tool in listing.tools:Draft202012Validator.check_schema(tool.inputSchema)
            check('published JSON schemas validate',True)
            async def call(name,**arguments):
                result=await client.call_tool(name,arguments=arguments)
                if result.isError:raise RuntimeError(result.structuredContent)
                return result.structuredContent
            store=Path((await call('viavw_info'))['store'])
            async def status(layer):
                s=(await call('viavw_status',layer=layer))['data']['session'];owned.update(s['ownedProcessIds']);return s
            async def observation(layer,capture=False):
                for attempt in range(80):
                    result=await client.call_tool('viavw_observe',arguments=dict(layer=layer,capture=capture))
                    if not result.isError:return result
                    if result.structuredContent['error']['code'] not in ('no_surface','observation_changed','capture_unavailable'):raise RuntimeError(result.structuredContent)
                    await asyncio.sleep(.025)
                raise TimeoutError('Observation did not settle')
            latencies=[]
            async def act(layer,kind,window=None,**fields):
                proof=(await observation(layer)).structuredContent['data']['observation']
                request=dict(layer=layer,observation=proof,kind=kind,**fields)
                if window:request['window']=window
                start=time.perf_counter();result=await call('viavw_input',**request);latencies.append((time.perf_counter()-start)*1000);return result
            fixture=package/'artifacts'/('sdk-fixture-'+uuid.uuid4().hex)
            subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(fixture)],check=True,capture_output=True,env=env)
            setup=json.loads((fixture/'launch.json').read_text(encoding='utf-8-sig'));document=Path(setup['Document'])
            nl=(await call('viavw_create',executable=setup['Executable'],arguments=setup['Arguments']))['layer'];layers.append(nl)
            first=await status(nl);pid=first['applicationPid'];o=await observation(nl,True);data=o.structuredContent['data'];editor=next(w['Handle'] for w in data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
            image=next(c for c in o.content if c.type=='image');png=base64.b64decode(image.data);images.append(data['result']['image']['path'])
            check('official SDK parses actual application PNG and structured results',hashlib.sha256(png).hexdigest().upper()==data['result']['image']['sha256'])
            archive=package/'artifacts'/('sdk-'+uuid.uuid4().hex+'.zip')
            with zipfile.ZipFile(archive,'x') as z:z.writestr('test.txt','Independent layer SDK test.\n')
            zl=(await call('viavw_create',executable=application('VIAVW_7ZIP_EXE','7-Zip/7zFM.exe'),arguments=[str(archive)]))['layer'];layers.append(zl)
            zs=await status(zl)
            check('two ordinary installed apps run in independent layers',zs['applicationPid']!=pid and zs['layerId']!=first['layerId'])
            async with stdio_client(parameters) as (peer_read,peer_write):
                async with ClientSession(peer_read,peer_write) as peer:
                    await peer.initialize()
                    async def probe_peer():
                        values=[]
                        for _ in range(75):
                            started=time.perf_counter();result=await peer.call_tool('viavw_status',arguments=dict(layer=zl));values.append((time.perf_counter()-started)*1000)
                            if result.isError:raise RuntimeError(result.structuredContent)
                            await asyncio.sleep(.01)
                        return values
                    async def edit():
                        for i in range(25):
                            await act(nl,'chord',window=editor,keys=[17,65]);await act(nl,'text',window=editor,text=f'SDK workflow {i}\nSecond linex');await act(nl,'key',window=editor,key=8);await act(nl,'key',window=editor,key=13);await act(nl,'text',window=editor,text='Finished');await act(nl,'chord',window=editor,keys=[17,83])
                            expected=f'SDK workflow {i}\nSecond line\nFinished';deadline=time.monotonic()+2
                            while document.read_text(encoding='utf-8-sig')!=expected and time.monotonic()<deadline:await asyncio.sleep(.01)
                            if document.read_text(encoding='utf-8-sig')!=expected:raise RuntimeError('Actual saved content mismatch at cycle '+str(i))
                        return True
                    started=time.perf_counter();edited,peers=await asyncio.gather(edit(),probe_peer());measurements['25WorkflowSeconds']=time.perf_counter()-started
                    check('25 actual edit save cycles while another AI queries a separate app',edited and len(peers)==75)
                    measurements['peerStatusMedianMilliseconds']=statistics.median(peers);measurements['peerStatusMaximumMilliseconds']=max(peers)
            await act(nl,'text',window=editor,text='_unsaved_after_fault')
            folder=store/nl;recordpath=folder/'tool-record.json';intent=json.loads(recordpath.read_text());intent['uncertainInput']=True;intent['inputFence']=uuid.uuid4().hex;recordpath.write_text(json.dumps(intent),encoding='utf-8')
            async with stdio_client(parameters) as (fresh_read,fresh_write):
                async with ClientSession(fresh_read,fresh_write) as fresh_client:
                    await fresh_client.initialize()
                    fresh_observation=(await fresh_client.call_tool('viavw_observe',arguments=dict(layer=nl,capture=False))).structuredContent['data']
                    denied=await fresh_client.call_tool('viavw_input',arguments=dict(layer=nl,observation=fresh_observation['observation'],kind='text',window=editor,text='MUST_NOT_REPLAY'))
                    check('persisted unknown intent blocks a newly connected AI client',denied.isError and denied.structuredContent['error']['code']=='input_uncertain' and fresh_observation['session']['inputUncertain'])
                    reconciled=await fresh_client.call_tool('viavw_reconcile',arguments=dict(layer=nl,observation=fresh_observation['observation'],confirmContext=True,reason='Same actual editor HWND and PID observed after deliberately modeled lost acknowledgement; no replay authorized.'))
                    check('fresh client explicitly reconciles uncertain input without replay',not reconciled.isError)
            # Recreate a lost tool catalog write, then crash only its exact held
            # controller identity. This is a disposable application owned by us.
            folder=store/nl;record=json.loads((folder/'tool-record.json').read_text());record.pop('session');record.pop('keeper');record['state']='starting';(folder/'tool-record.json').write_text(json.dumps(record),encoding='utf-8')
            descriptor=json.loads((folder/'owner/layer-owner.json').read_text());owned.add(descriptor['pid']);stop_exact_owner(descriptor)
            recovered=await call('viavw_recover',layer=nl);check('dead owner and lost catalog outcome recover without duplicate launch',recovered['data']['session']['applicationPid']==pid and recovered['data']['session']['recoveryPending'])
            fresh=(await observation(nl,True)).structuredContent['data'];images.append(fresh['result']['image']['path'])
            check('recovered actual editor and image match prior application',fresh['session']['applicationPid']==pid and any(w['Handle']==editor and w['ClassName']=='Scintilla' for w in fresh['result']['windows']))
            await call('viavw_reconcile',layer=nl,observation=fresh['observation'],confirmContext=True,reason='Verified same PID and Scintilla editor HWND plus actual capture after held-controller crash.')
            await act(nl,'chord',window=editor,keys=[17,83]);await asyncio.sleep(.1)
            check('real unsaved editor memory saved after crash and catalog recovery',document.read_text(encoding='utf-8-sig')=='SDK workflow 24\nSecond line\nFinished_unsaved_after_fault')
            for layer in layers:
                result=await call('viavw_close',layer=layer);check('official SDK graceful close '+layer,result['data']['result']['closed'])
            measurements['inputApiSamples']=len(latencies);measurements['inputApiMedianMilliseconds']=statistics.median(latencies);measurements['inputApiP95Milliseconds']=sorted(latencies)[int(len(latencies)*.95)-1];measurements['inputApiMaximumMilliseconds']=max(latencies)
            check('official SDK rejects invalid tool arguments as tool error',(await client.call_tool('viavw_input',arguments={'extra':True})).isError)

try:asyncio.run(main())
except Exception:failure=traceback.format_exc();print(failure,flush=True)
finally:
    for layer in layers:
        try:
            request=out/'close.json';request.write_text(json.dumps(dict(layer=layer,force=True)))
            subprocess.run([str(exe),'call','viavw_close','--file',str(request)],capture_output=True,timeout=20)
        except Exception:pass
    time.sleep(.3)
    kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
    survivors=[]
    for pid in owned:
        handle=kernel.OpenProcess(0x100000,False,pid)
        if handle:
            if kernel.WaitForSingleObject(handle,0)!=0:survivors.append(pid)
            kernel.CloseHandle(handle)
    passed=failure is None and all(checks.values()) and not survivors
    report=dict(passed=passed,checks=checks,measurements=measurements,failure=failure,package=str(package),sdkVersion=importlib.metadata.version('mcp'),layers=layers,images=images,survivors=survivors,physicalHumanAcceptance='pending',CharlieAI='unchanged')
    (out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print('VIAVW_EVIDENCE='+str(out/'verification.json'),flush=True)
    if not passed:raise SystemExit(1)

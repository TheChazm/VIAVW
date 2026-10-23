"""Use only the shipped EXE, public MCP and CLI; no VIAVW client imports."""
import base64
import concurrent.futures
import ctypes
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import sys
import threading
import time
import traceback
import uuid
import zipfile

ROOT=Path(__file__).resolve().parents[1]
source=Path(sys.argv[1]).resolve()
out=ROOT/'artifacts'/('standalone-verification-'+uuid.uuid4().hex);out.mkdir()
package=out/'relocated-package';shutil.copytree(source,package)
exe=package/'viavw.exe';host=package/'src/LayerHost/bin/Runtime/net10.0-windows/LayerHost.exe'
env=dict(os.environ,PATH=str(Path(os.environ['SystemRoot'])/'System32'),DOTNET_ROOT=str(out/'no-dotnet'),DOTNET_ROOT_X64=str(out/'no-dotnet'),DOTNET_MULTILEVEL_LOOKUP='0')
checks={};measurements={};layers=[];connections=[];owned=set();failure=None;images=[]

def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)

def cli(name,args):
    request=out/('request-'+uuid.uuid4().hex+'.json');request.write_text(json.dumps(args),encoding='utf-8')
    result=subprocess.run([str(exe),'call',name,'--file',str(request)],capture_output=True,text=True,encoding='utf-8',env=env,timeout=100)
    data=json.loads(result.stdout)
    if not data['ok']:raise RuntimeError(data)
    return data['result']

class MCP:
    def __init__(self,version='2025-11-25'):
        self.p=subprocess.Popen([str(exe),'mcp'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env)
        self.sequence=0;self.q=queue.Queue();self.last=None;connections.append(self)
        def reader():
            try:
                for line in self.p.stdout:self.q.put(json.loads(line))
            except Exception as error:self.q.put(error)
            finally:self.q.put(EOFError('MCP disconnected'))
        threading.Thread(target=reader,daemon=True).start()
        hello=self.rpc('initialize',dict(protocolVersion=version,capabilities={},clientInfo=dict(name='VIAVW external verification',version='1')))
        check('MCP initialize '+version,hello['result']['protocolVersion']==version)
        self.write(dict(jsonrpc='2.0',method='notifications/initialized'))
    def write(self,data):self.p.stdin.write(json.dumps(data,separators=(',',':'))+'\n');self.p.stdin.flush()
    def receive(self):
        result=self.q.get(timeout=100)
        if isinstance(result,Exception):raise result
        return result
    def rpc(self,method,params=None):
        self.sequence+=1;self.last=dict(jsonrpc='2.0',id=self.sequence,method=method,params=params or {});self.write(self.last);return self.receive()
    def tool(self,name,args,allow_error=False):
        reply=self.rpc('tools/call',dict(name=name,arguments=args))
        if 'error' in reply:raise RuntimeError(reply)
        result=reply['result']
        if result['isError'] and not allow_error:raise RuntimeError(result)
        return result
    def call(self,name,**args):return self.tool(name,args)['structuredContent']
    def close(self):
        if self.p.poll() is None:self.p.stdin.close();self.p.wait(timeout=5)
        self.p.stdout.close();self.p.stderr.close()

def state(c,layer):
    s=c.call('viavw_status',layer=layer)['data']['session'];owned.update(s['ownedProcessIds']);return s

def observe(c,layer,capture=False):
    deadline=time.monotonic()+4
    while True:
        result=c.tool('viavw_observe',dict(layer=layer,capture=capture),allow_error=True)
        if not result['isError']:return result
        if time.monotonic()>deadline:raise RuntimeError(result)
        time.sleep(.025)

def action(c,layer,kind,window=None,**fields):
    data=observe(c,layer)['structuredContent']['data']
    args=dict(layer=layer,observation=data['observation'],kind=kind,**fields)
    if window:args['window']=window
    return c.call('viavw_input',**args)

try:
    doctor=subprocess.run([str(exe),'doctor'],env=env,capture_output=True,text=True,encoding='utf-8',timeout=30)
    check('relocated package verifies every file without Python SDK or installed dotnet PATH',doctor.returncode==0 and json.loads(doctor.stdout)['passed'])
    c=MCP();check('MCP ping',c.rpc('ping')['result']=={})
    tools=c.rpc('tools/list')['result']['tools'];check('13 discoverable tools with schemas',len(tools)==13 and all(t['inputSchema']['additionalProperties'] is False for t in tools))
    check('MCP unknown method is protocol error',c.rpc('no-such-method')['error']['code']==-32601)
    bad=c.tool('viavw_info',dict(extra='ignored?'),True);check('unknown arguments rejected',bad['isError'] and bad['structuredContent']['error']['code']=='invalid_arguments')
    check('unknown tool is error',c.tool('unknown_tool',{},True)['isError'])
    check('missing text rejected before starting action',c.tool('viavw_input',dict(layer='a'*32,observation='bad',kind='text'),True)['isError'])
    check('CLI uses same standalone tool',cli('viavw_info',{})['version']==json.loads((package/'package.json').read_text(encoding='utf-8-sig'))['version'])
    fixture=package/'artifacts'/'fixture';subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(fixture)],check=True,capture_output=True,env=env,timeout=20)
    setup=json.loads((fixture/'launch.json').read_text(encoding='utf-8-sig'));document=Path(setup['Document'])
    created=c.call('viavw_create',executable=setup['Executable'],arguments=setup['Arguments']);layer=created['layer'];layers.append(layer)
    s=state(c,layer);pid=s['applicationPid'];check('installed app starts privately with optional viewer hidden',not s['viewerVisible'] and s['owner']=='Automation')
    observed=observe(c,layer,True);data=observed['structuredContent']['data'];png=next(v for v in observed['content'] if v['type']=='image');image=data['result']['image'];raw=base64.b64decode(png['data']);images.append(image['path'])
    check('MCP returns actual verified PNG image content',raw.startswith(b'\x89PNG\r\n\x1a\n') and hashlib.sha256(raw).hexdigest().upper()==image['sha256'])
    editor=next(w['Handle'] for w in data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
    root=data['session']['root'];c.call('viavw_surface',layer=layer,observation=data['observation'],window=root)
    action(c,layer,'click',window=editor,x=80,y=28);action(c,layer,'chord',window=editor,keys=[17,65])
    stale=observe(c,layer)['structuredContent']['data']['observation']
    action(c,layer,'text',window=editor,text='Standalone tool\r\nUnicode 😀\tX')
    denied=c.tool('viavw_input',dict(layer=layer,observation=stale,kind='text',window=editor,text='BAD'),True)
    check('stale observed input rejected without replay',denied['isError'] and denied['structuredContent']['error']['code']=='stale_observation')
    action(c,layer,'key',window=editor,key=8);action(c,layer,'key',window=editor,key=13);action(c,layer,'text',window=editor,text='Saved through MCP')
    start=time.perf_counter();action(c,layer,'chord',window=editor,keys=[17,83]);measurements['observeThenSaveMilliseconds']=(time.perf_counter()-start)*1000
    expected='Standalone tool\nUnicode 😀\t\nSaved through MCP';deadline=time.monotonic()+3
    while document.read_text(encoding='utf-8-sig')!=expected and time.monotonic()<deadline:time.sleep(.02)
    check('click select type Enter Backspace chord Unicode Tab and real save',document.read_text(encoding='utf-8-sig')==expected)
    c.call('viavw_viewer',layer=layer,visible=True);check('optional viewer can open without Human transfer',state(c,layer)['viewerVisible'] and state(c,layer)['owner']=='Automation')
    c.call('viavw_viewer',layer=layer,visible=False)
    inspect=c.call('viavw_inspect',layer=layer)['data']['result'];ticket=inspect['ticket'];deadline=time.monotonic()+5
    while True:
        poll=c.call('viavw_inspect',layer=layer,ticket=ticket)['data']['result'];snapshot=poll['inspection']
        if snapshot.get('Status',snapshot.get('status'))!='pending' or time.monotonic()>deadline:break
        time.sleep(.03)
    check('passive inspection start and poll retain explicit stale context','stale' in poll and 'context' in poll)
    full=c.call('viavw_observe',layer=layer,capture=False,accessibility=True)['data'];check('accessibility evidence exposed to caller','accessibility' in full or 'accessibilityNote' in full)
    action(c,layer,'text',window=editor,text='_unsaved')
    c.close();check('tool process exits while app remains running',cli('viavw_status',dict(layer=layer))['data']['session']['applicationPid']==pid)
    inventory=cli('viavw_list',{})['layers'];check('fresh CLI discovers persistent same layer',any(i['layer']==layer and i['ownerAlive'] for i in inventory))
    c=MCP('2025-06-18');c.call('viavw_recover',layer=layer,replaceOwner=True);s=state(c,layer)
    check('external tool replaces owner retaining same app PID',s['applicationPid']==pid and s['recoveryPending'])
    paused=c.tool('viavw_input',dict(layer=layer,observation=observe(c,layer)['structuredContent']['data']['observation'],kind='text',text='BAD'),True)
    check('recovered layer rejects input until reconciliation',paused['isError'] and paused['structuredContent']['error']['code']=='layer_suspended')
    post=observe(c,layer,True)['structuredContent']['data'];check('post recovery has editor image and same context',post['session']['applicationPid']==pid and any(w['Handle']==editor for w in post['result']['windows']) and post['result']['image']['width']>100)
    c.call('viavw_reconcile',layer=layer,observation=post['observation'],confirmContext=True,reason='Verified same Notepad++ PID, editor HWND and captured document before saving retained buffer.')
    action(c,layer,'chord',window=editor,keys=[17,83]);time.sleep(.1)
    check('actual unsaved application memory survives controller replacement',document.read_text(encoding='utf-8-sig')==expected+'_unsaved')
    # Two independent MCP processes share layer ownership/sequence validation.
    peer=MCP('2024-11-05');first=observe(c,layer)['structuredContent']['data']['observation'];second=observe(peer,layer)['structuredContent']['data']['observation']
    c.call('viavw_input',layer=layer,observation=first,kind='text',window=editor,text='_one')
    cross=peer.tool('viavw_input',dict(layer=layer,observation=second,kind='text',window=editor,text='_two'),True)
    check('two AI processes cannot act on stale shared state',cross['isError'] and cross['structuredContent']['error']['code']=='stale_observation')
    action(c,layer,'chord',window=editor,keys=[17,83]);time.sleep(.08)
    # Request retransmission cannot spawn an extra action.
    token=observe(c,layer)['structuredContent']['data']['observation'];args=dict(layer=layer,observation=token,kind='text',window=editor,text='_once')
    original=c.tool('viavw_input',args);request=dict(c.last);c.write(request);repeated=c.receive()
    check('exact MCP duplicate returns historical result',repeated['result']==original)
    action(c,layer,'chord',window=editor,keys=[17,83]);time.sleep(.08)
    check('duplicate did not repeat actual text',document.read_text(encoding='utf-8-sig')==expected+'_unsaved_one_once')
    c.call('viavw_handoff',layer=layer,reason='Automated ownership check; physical Human acceptance remains pending.')
    human=state(c,layer);check('handoff opens optional viewer with Human ownership',human['owner']=='Human' and human['viewerVisible'])
    human_input=peer.tool('viavw_input',dict(layer=layer,observation=observe(peer,layer)['structuredContent']['data']['observation'],kind='text',text='BAD'),True)
    check('second AI cannot send input while Human owns app',human_input['isError'] and human_input['structuredContent']['error']['code']=='human_owns_input')
    check('AI tool exposes no ownership reclaim command',not any(t['name'] in ('viavw_return','viavw_ownership') for t in tools))
    peer.close();c.call('viavw_close',layer=layer);check('saved Human app closes normally',not cli('viavw_list',{})['layers'][0]['ownerAlive'])
    # Independent archive GUI exercises a second installed application.
    archive=package/'artifacts'/'disposable.zip'
    with zipfile.ZipFile(archive,'x') as z:z.writestr('hello.txt','Extracted through standalone VIAVW tool.\n')
    zl=c.call('viavw_create',executable=r'C:\Program Files\7-Zip\7zFM.exe',arguments=[str(archive)])['layer'];layers.append(zl);state(c,zl)
    zo=observe(c,zl,True)['structuredContent']['data'];images.append(zo['result']['image']['path']);view=next(w['Handle'] for w in zo['result']['windows'] if w['ClassName']=='SysListView32')
    action(c,zl,'click',window=view,x=70,y=54,deliveryMode='posted');action(c,zl,'chord',keys=[17,65]);action(c,zl,'key',key=116)
    deadline=time.monotonic()+5
    while True:
        zo=observe(c,zl,True)['structuredContent']['data']
        if any(w['Handle']==zo['session']['root'] and w['Text']=='Copy' for w in zo['result']['windows']):break
        if time.monotonic()>deadline:raise RuntimeError('No archive extraction dialog')
    images.append(zo['result']['image']['path']);target=package/'artifacts'/'extracted'
    action(c,zl,'chord',keys=[17,65]);action(c,zl,'text',text=str(target)+'\\');action(c,zl,'key',key=13)
    deadline=time.monotonic()+5
    while not (target/'hello.txt').exists() and time.monotonic()<deadline:time.sleep(.03)
    check('installed 7-Zip dialog extracts actual archive through MCP',(target/'hello.txt').read_text()=='Extracted through standalone VIAVW tool.\n')
    check('7-Zip closes gracefully',c.call('viavw_close',layer=zl)['data']['result']['closed'])
    # A fresh disposable editor tests save-prompt preservation and explicit discard.
    nl=c.call('viavw_create',executable=setup['Executable'],arguments=setup['Arguments'])['layer'];layers.append(nl);state(c,nl)
    no=observe(c,nl)['structuredContent']['data'];ne=next(w['Handle'] for w in no['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
    action(c,nl,'text',window=ne,text='Disposable unsaved prompt test')
    prompt=c.call('viavw_close',layer=nl)['data'];check('graceful close preserves native save prompt and Human owner',not prompt['result']['closed'] and prompt['session']['owner']=='Human' and prompt['session']['viewerVisible'])
    pimage=observe(c,nl,True)['structuredContent']['data'];images.append(pimage['result']['image']['path'])
    check('explicit force close discards only this disposable prompt app',c.call('viavw_close',layer=nl,force=True)['data']['result']['closed'])
except Exception:
    failure=traceback.format_exc();print(failure,flush=True)
finally:
    for connection in connections:
        try:connection.close()
        except Exception:pass
    for layer in layers:
        try:
            inventory=cli('viavw_list',{})['layers'];entry=next(i for i in inventory if i['layer']==layer)
            if entry['ownerAlive']:cli('viavw_close',dict(layer=layer,force=True))
        except Exception as error:print('Cleanup attention:',layer,error,flush=True)
    time.sleep(.4)
    def alive(pid):
        kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
        handle=kernel.OpenProcess(0x100000,False,pid)
        if not handle:return False
        try:return kernel.WaitForSingleObject(handle,0)!=0
        finally:kernel.CloseHandle(handle)
    survivors=[pid for pid in owned if alive(pid)]
    passed=failure is None and all(checks.values()) and not survivors
    report=dict(passed=passed,checks=checks,measurements=measurements,failure=failure,package=str(package),sourcePackage=str(source),layers=layers,owned=sorted(owned),survivors=survivors,images=images,physicalHumanAcceptance='pending',CharlieAI='unchanged')
    (out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('VIAVW_EVIDENCE='+str(out/'verification.json'),flush=True)
    if not passed:raise SystemExit(1)

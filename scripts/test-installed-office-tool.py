"""Disposable installed Office workflows through the shipped public MCP interface."""
import asyncio, base64, ctypes, json, site, sys, time, traceback, uuid, zipfile
from pathlib import Path
from xml.etree import ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
site.addsitedir(str(ROOT/'artifacts/tool-mcp-sdk-20261023'))
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
package=Path(sys.argv[1]).resolve()
executable=Path(sys.argv[2]).resolve() if len(sys.argv)>2 else package/'viavw.exe'
out=ROOT/'artifacts'/('office-tool-'+uuid.uuid4().hex);out.mkdir()
checks={};layers=[];closed=set();owned=set();images=[];timings=[];failure=None
def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)
def docx(path):
    with zipfile.ZipFile(path,'x') as z:
        z.writestr('[Content_Types].xml','<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        z.writestr('_rels/.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Disposable Word document</w:t></w:r></w:p></w:body></w:document>')
def paragraphs(path):
    with zipfile.ZipFile(path) as z:root=ET.fromstring(z.read('word/document.xml'))
    ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
    return [''.join(p.itertext()) for p in root.findall('.//w:body/w:p',ns)]
async def main():
    async with stdio_client(StdioServerParameters(command=str(executable),args=['mcp'])) as (read,write):
        async with ClientSession(read,write) as c:
            await c.initialize()
            async def call(name,**a):
                r=await c.call_tool(name,arguments=a)
                if r.isError:raise RuntimeError(r.structuredContent)
                return r.structuredContent
            store=Path((await call('viavw_info'))['store'])
            async def obs(layer,capture=False,accessibility=False):
                for i in range(80):
                    r=await c.call_tool('viavw_observe',arguments=dict(layer=layer,capture=capture,accessibility=accessibility))
                    if not r.isError:
                        d=r.structuredContent['data'];owned.update(d['session']['ownedProcessIds'])
                        if capture:images.append(d['result']['image'])
                        (out/('observe-'+uuid.uuid4().hex+'.json')).write_text(json.dumps(d,indent=2),encoding='utf-8')
                        return d
                    if r.structuredContent['error']['code'] not in ('no_surface','observation_changed','capture_unavailable'):raise RuntimeError(r.structuredContent)
                    await asyncio.sleep(.05)
                raise RuntimeError('No settled observation')
            async def act(layer,kind,window=None,**fields):
                o=await obs(layer);a=dict(layer=layer,observation=o['observation'],kind=kind,**fields)
                if window:a['window']=window
                start=time.perf_counter();r=await call('viavw_input',**a);timings.append((time.perf_counter()-start)*1000);return r
            try:
                path=out/'letter.docx';docx(path)
                r=await call('viavw_create',executable=r'C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE',arguments=['/a','/n',str(path)])
                layer=r['layer'];layers.append(layer)
                check('Word auto selects print-window',r['captureMode']=='print-window')
                for _ in range(100):
                    o=await obs(layer,True)
                    editors=[w['Handle'] for w in o['result']['windows'] if w['ClassName']=='_WwG' and w['Visible']]
                    if editors:break
                    await asyncio.sleep(.1)
                editor=editors[0]
                for index in range(3):
                    await act(layer,'chord',editor,keys=[17,65])
                    await act(layer,'text',editor,text=f'Word workflow {index}.\r\nSecond linex')
                    await act(layer,'key',editor,key=8)
                    await act(layer,'key',editor,key=13)
                    await act(layer,'text',editor,text='Saved by VIAVW.')
                    await act(layer,'chord',editor,keys=[17,83])
                    expected=[f'Word workflow {index}.','Second line','Saved by VIAVW.']
                    deadline=time.monotonic()+3
                    while time.monotonic()<deadline:
                        try:
                            if paragraphs(path)==expected:break
                        except (PermissionError,zipfile.BadZipFile):pass
                        await asyncio.sleep(.05)
                    actual=paragraphs(path)
                    (out/f'word-result-{index}.json').write_text(json.dumps(actual),encoding='utf-8')
                    check(f'Word exact saved paragraphs after multiline Enter Backspace cycle {index}',actual==expected)
                await obs(layer,True)
                # Model a lost acknowledgement at the durable public boundary.
                # Do not send a hidden app action: only mark the pending intent.
                recordpath=store/layer/'tool-record.json'
                record=json.loads(recordpath.read_text());record['uncertainInput']=True;record['inputFence']=uuid.uuid4().hex
                recordpath.write_text(json.dumps(record),encoding='utf-8')
                fresh=await obs(layer,True)
                denied=await c.call_tool('viavw_input',arguments=dict(layer=layer,observation=fresh['observation'],kind='text',text='MUST NOT TYPE'))
                check('persisted unknown intent blocks new input',denied.isError and denied.structuredContent['error']['code']=='input_uncertain')
                check('observed state reports unknown input',fresh['session']['inputUncertain'])
                await call('viavw_reconcile',layer=layer,observation=fresh['observation'],confirmContext=True,reason='Actual Word capture, same PID and editor; exact saved DOCX paragraphs verified. No unknown input replay.')
                await act(layer,'chord',editor,keys=[17,83])
                check('explicit reconciliation does not replay text',paragraphs(path)==expected)
                result=await call('viavw_close',layer=layer)
                check('saved Word closes gracefully',result['data']['result']['closed'])
                closed.add(layer)
                csv=out/'inventory.csv';csv.write_text('Item,Qty,Price,Total\nPencil,3,2,\nPaper,5,1,\n',encoding='utf-8')
                r=await call('viavw_create',executable=r'C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE',arguments=['/x','/s',str(csv)])
                layer=r['layer'];layers.append(layer)
                check('Excel auto selects print-window',r['captureMode']=='print-window')
                o=await obs(layer,True,True)
                notices=[w for w in o['result']['windows'] if w['ClassName']=='NUIDialog' and w['Visible']]
                if notices:
                    # Only acknowledge the known recovered-files informational notice.
                    content=json.dumps(o.get('accessibility',{}))
                    if 'recovered files' not in content:raise RuntimeError('Unexpected Excel dialog; retained evidence')
                    await act(layer,'key',notices[0]['Handle'],key=13)
                for cell,formula in [('D2','=B2*C2'),('D3','=B3*C3')]:
                    await act(layer,'chord',keys=[17,71]);await act(layer,'text',text=cell);await act(layer,'key',key=13)
                    await act(layer,'text',text=formula);await act(layer,'key',key=13)
                await act(layer,'chord',keys=[17,83]);await asyncio.sleep(.2)
                content=csv.read_text(encoding='utf-8-sig')
                check('Excel actual formulas compute and save CSV values', 'Pencil,3,2,6' in content and 'Paper,5,1,5' in content)
                await obs(layer,True)
                before=await obs(layer)
                saveas=await c.call_tool('viavw_input',arguments=dict(layer=layer,observation=before['observation'],kind='key',key=123))
                after=await obs(layer,True,True)
                dialogs=[w for w in after['result']['windows'] if w['Visible'] and w['ClassName']=='#32770']
                check('Excel F12 actually opens Save As',bool(dialogs))
                if saveas.isError:
                    check('real Excel unknown outcome persists input pause',saveas.structuredContent['error']['outcome']=='unknown' and after['session']['inputUncertain'])
                    denied=await c.call_tool('viavw_input',arguments=dict(layer=layer,observation=after['observation'],kind='text',text='MUST NOT TYPE'))
                    check('real Excel partial modal transition blocks further input',denied.isError and denied.structuredContent['error']['code']=='input_uncertain')
                    await call('viavw_reconcile',layer=layer,observation=after['observation'],confirmContext=True,reason='Observed actual Excel Save As modal after F12; original CSV contains verified 6 and 5. Do not repeat F12.')
                await act(layer,'key',dialogs[0]['Handle'],key=27)
                await obs(layer,True)
            finally:
                for layer in layers:
                    if layer in closed:continue
                    try:
                        s=(await call('viavw_status',layer=layer))['data']['session']
                        if s.get('applicationAlive') or s.get('ownedProcessIds'):
                            r=await call('viavw_close',layer=layer)
                            if not r['data']['result']['closed']:
                                await call('viavw_viewer',layer=layer,visible=False)
                                await call('viavw_close',layer=layer,force=True)
                    except Exception as e:print('Cleanup:',e,flush=True)
try:asyncio.run(main())
except BaseException:failure=traceback.format_exc();print(failure,flush=True)
time.sleep(.3)
kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
survivors=[]
for pid in owned:
    handle=kernel.OpenProcess(0x100000,False,pid)
    if handle:
        if kernel.WaitForSingleObject(handle,0)!=0:survivors.append(pid)
        kernel.CloseHandle(handle)
report=dict(passed=failure is None and all(checks.values()) and not survivors,checks=checks,package=str(package),executable=str(executable),images=images,inputMilliseconds=timings,failure=failure,layers=layers,survivors=survivors,physicalHumanAcceptance='pending')
(out/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('VIAVW_EVIDENCE='+str(out/'verification.json'),flush=True)
if not report['passed']:raise SystemExit(1)

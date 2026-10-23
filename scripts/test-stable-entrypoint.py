"""Exercise argument forwarding, failure propagation and selection validation."""
import json, os, shutil, subprocess, sys, uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
exe=Path(sys.argv[1]).resolve();out=ROOT/'artifacts'/('entrypoint-verification-'+uuid.uuid4().hex);out.mkdir()
checks={}
def check(name,value):
    checks[name]=bool(value);print(('PASS ' if value else 'FAIL ')+name,flush=True)
    if not value:raise AssertionError(name)
failure=None
try:
    r=subprocess.run([str(exe),'call','viavw_info','--json','{}'],capture_output=True,text=True,encoding='utf-8',timeout=20)
    check('stable entrypoint forwards valid command and clean JSON stdout',r.returncode==0 and json.loads(r.stdout)['result']['version']=='0.1.2' and not r.stderr)
    field='quoted "雪\\ trailing\\'
    r=subprocess.run([str(exe),'call','viavw_info','--json',json.dumps({field:'ignored?'},ensure_ascii=False)],capture_output=True,text=True,encoding='utf-8',timeout=20)
    error=json.loads(r.stdout)['error']
    check('quotes Unicode and backslashes survive exact Windows argument forwarding',r.returncode==1 and error['message']=='Unknown argument: '+field)
    request=out/'request 雪 with spaces.json';request.write_text('{}',encoding='utf-8')
    r=subprocess.run([str(exe),'call','viavw_info','--file',str(request)],capture_output=True,text=True,encoding='utf-8',timeout=20)
    check('Unicode request file path with spaces is forwarded',r.returncode==0 and json.loads(r.stdout)['ok'])
    invalid=out/'isolated selection';invalid.mkdir();shutil.copy2(exe,invalid/'viavw.exe')
    for value in ['..\\outside.exe','C:\\Windows\\System32\\notepad.exe','releases\\tool-..\\viavw.exe','releases\\tool-missing\\viavw.exe']:
        (invalid/'viavw-current.txt').write_text(value,encoding='ascii')
        r=subprocess.run([str(invalid/'viavw.exe'),'mcp'],capture_output=True,text=True,encoding='utf-8',timeout=5)
        check('invalid or missing selection rejected: '+value,r.returncode==2 and not r.stdout and r.stderr.startswith('VIAVW:'))
except Exception as e:failure=repr(e)
(out/'verification.json').write_text(json.dumps(dict(passed=failure is None and all(checks.values()),checks=checks,executable=str(exe),failure=failure),indent=2),encoding='utf-8')
print('VIAVW_EVIDENCE='+str(out/'verification.json'))
if failure:raise SystemExit(1)

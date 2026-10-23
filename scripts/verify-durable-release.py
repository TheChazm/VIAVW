"""Serial durable-layer acceptance; promote only after every actual-app gate passes."""
import argparse
import datetime
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import uuid
from viavw_layers import ROOT,checked_host,require_capacity,write_metadata


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--minutes',type=float,default=20);args=parser.parse_args()
    if not math.isfinite(args.minutes) or args.minutes<20:parser.error('Release acceptance requires at least 20 minutes of endurance')
    candidate_bytes=(ROOT/'artifacts/durable-layers-candidate.json').read_bytes()
    candidate=json.loads(candidate_bytes.decode('utf-8-sig'));host=checked_host(candidate['executable'])
    sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest().upper()
    if sha(host.with_suffix('.dll'))!=candidate['assemblySha256']:raise ValueError('Candidate executable changed')
    for relative,digest in candidate['files'].items():
        if sha(ROOT/relative)!=digest:raise ValueError('Candidate support file changed: '+relative)
    require_capacity()
    out=ROOT/'artifacts'/('durable-release-'+uuid.uuid4().hex);out.mkdir();reports=[];failure=None
    stages=[
        ('test-layer-transport.py',[str(host)],ROOT/'artifacts','layer-transport-*'),
        ('test-durable-layers.py',[str(host)],ROOT/'artifacts','durable-verification-*'),
        ('test-layer-owner-replacement.py',[str(host)],ROOT/'artifacts','owner-replacement-*'),
        ('test-layer-infrastructure.py',[str(host)],ROOT/'artifacts','layer-infrastructure-*'),
        ('test-layer-save-prompt.py',[str(host)],ROOT/'artifacts','layer-save-prompt-*'),
        ('test-layer-metadata.py',[str(host)],ROOT/'artifacts','layer-metadata-*'),
        ('test-layer-client.py',[str(host)],ROOT/'artifacts','layer-client-*'),
        ('test-layer-workflows.py',[str(host)],ROOT/'artifacts','layer-workflows-*'),
        ('layers.py',['demo','--host',str(host),'--smoke'],ROOT/'artifacts/layers','demo-*'),
        ('test-layer-endurance.py',['--host',str(host),'--minutes',str(args.minutes)],ROOT/'artifacts','layer-endurance-*')]
    try:
        for script,fields,directory,pattern in stages:
            print('Verifying '+script,flush=True);receipts=[]
            with (out/(script+'.log')).open('x',encoding='utf-8') as log:
                process=subprocess.Popen([sys.executable,'-u',str(ROOT/'scripts'/script),*fields],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8')
                for line in process.stdout:
                    print(line,end='',flush=True);log.write(line);log.flush()
                    if line.startswith('VIAVW_EVIDENCE='):receipts.append(Path(line.strip().split('=',1)[1]).resolve())
                returncode=process.wait()
            if len(receipts)!=1:raise RuntimeError('Expected one evidence receipt for '+script+'; see '+str(out/(script+'.log')))
            report=receipts[0]
            if not report.is_relative_to(ROOT/'artifacts'):raise ValueError('Unexpected evidence location')
            data=json.loads(report.read_text(encoding='utf-8-sig'));reports.append(str(report))
            if returncode!=0 or data.get('passed') is not True:raise RuntimeError('Acceptance failed: '+script+'; evidence '+str(report))
            if script=='test-layer-endurance.py' and (data['verifiedEditSaveCycles']<1 or data['requestedMinutes']!=args.minutes or data['elapsedSeconds']<args.minutes*60):raise RuntimeError('Endurance evidence did not cover the requested duration')
        if (ROOT/'artifacts/durable-layers-candidate.json').read_bytes()!=candidate_bytes:raise RuntimeError('Candidate changed during verification; no release selected')
        if sha(host.with_suffix('.dll'))!=candidate['assemblySha256']:raise RuntimeError('Executable changed during verification; no release selected')
        for relative,digest in candidate['files'].items():
            if sha(ROOT/relative)!=digest:raise RuntimeError('Support file changed during verification: '+relative)
        # Never overwrite an earlier selected manifest without retaining it.
        selected=ROOT/'artifacts/durable-layers-current.json'
        if selected.exists():(out/'previous-selection.json').write_bytes(selected.read_bytes())
        release=dict(candidate,status='verified_durable_core; physical_human_and_CharlieAI_integration_pending',verifiedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),verifiedEvidence=reports,pending=['Physical human acceptance of durable viewer lifetime','CharlieAI-side integration remains outside authorized scope','Universal application compatibility is not claimed'])
        release['supportSnapshot']=str(out/'support');support=out/'support';support.mkdir()
        for relative in candidate['files']:
            destination=support/relative;destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes((ROOT/relative).read_bytes())
        write_metadata(selected,release)
        print('Durable application release selected. Existing legacy selections are preserved.',flush=True)
    except Exception as error:failure=repr(error);raise
    finally:(out/'verification.json').write_text(json.dumps(dict(passed=failure is None,failure=failure,reports=reports,selected=failure is None),indent=2),encoding='utf-8')
if __name__=='__main__':main()

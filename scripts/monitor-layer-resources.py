"""Read-only process resource sampling alongside the actual application endurance run."""
import importlib.util
import json
from pathlib import Path
import sys
import time
import uuid
from viavw_layers import ROOT,resolve_keeper,owner_alive,read_metadata,write_metadata
spec=importlib.util.spec_from_file_location('resource_reader',ROOT/'scripts/test-layer-endurance.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
endurance=Path(sys.argv[1]).resolve()
if not endurance.is_relative_to(ROOT/'artifacts'):raise ValueError('Use a VIAVW endurance evidence directory')
setup=json.loads((endurance/'fixture/launch.json').read_text(encoding='utf-8-sig'));document=setup['Document']
records=[]
for path in (ROOT/'artifacts/layers').glob('*/owner/layer-record.json'):
    record=read_metadata(path);create=path.parent/('session-'+record['session'])/'create.json'
    if create.exists() and document in json.loads(create.read_text(encoding='utf-8-sig'))['request'].get('arguments',[]):records.append(record)
if len(records)!=1:raise RuntimeError('Expected exactly one matching endurance layer')
record=records[0];descriptor=Path(record['descriptor']);output=ROOT/'artifacts'/('layer-resource-monitor-'+uuid.uuid4().hex);output.mkdir();samples=[];started=time.monotonic();deadline=started+25*60
while time.monotonic()<deadline:
    owner=read_metadata(descriptor);pids={record['applicationPid']}
    try:
        _,keeper=resolve_keeper(record['keeperDescriptor']);pids.add(keeper['pid']);pids.update(v['pid'] for v in keeper['snapshot'].get('infrastructure',[]))
        if owner_alive(owner):pids.add(owner['pid'])
        sample=dict(elapsedSeconds=time.monotonic()-started,ownerPid=owner['pid'],processes=module.resources(pids));samples.append(sample)
    except (OSError,KeyError,ValueError) as error:samples.append(dict(elapsedSeconds=time.monotonic()-started,error=str(error)))
    write_metadata(output/'samples.json',dict(endurance=str(endurance),samples=samples))
    if (endurance/'verification.json').exists():break
    time.sleep(5)
(output/'verification.json').write_text(json.dumps(dict(completed=(endurance/'verification.json').exists(),endurance=str(endurance),elapsedSeconds=time.monotonic()-started,samples=samples,scope='Read-only owner, keeper, worker/infrastructure and application counters. Caller and monitor Python process costs excluded; GPU use not measured.'),indent=2),encoding='utf-8');print(str(output),flush=True)

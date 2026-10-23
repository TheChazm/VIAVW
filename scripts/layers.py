"""Manage durable VIAVW application layers; closing this command leaves apps running."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid
from viavw_layers import ROOT,Layers,LayerClient,LayerHandle,checked_host,read_metadata,metadata_path


def handle_for(descriptor,host):
    descriptor=metadata_path(descriptor)
    if not descriptor.is_relative_to(ROOT/'artifacts'):raise ValueError('Use a VIAVW layer descriptor')
    record=read_metadata(descriptor.parent/'layer-record.json')
    if Path(record['descriptor']).resolve()!=descriptor:raise ValueError('Layer record does not match descriptor')
    return LayerHandle(descriptor,record['session'],host,Path(record['keeperDescriptor']))


def demo(host,smoke=False):
    output=ROOT/'artifacts/layers'/('demo-'+uuid.uuid4().hex);output.mkdir(parents=True)
    subprocess.run([str(host),'--prepare-installed-notepad-gdi-demo',str(output/'documents')],check=True,capture_output=True)
    setup=json.loads((output/'documents/launch.json').read_text(encoding='utf-8-sig'))
    layer=Layers(host).create(setup['Executable'],setup['Arguments'])
    # Persist the management address before attempting any optional interaction.
    result=dict(descriptor=str(layer.descriptor),session=layer.session,document=setup['Document'],keeper=str(layer.keeper))
    (output/'demo.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    try:
        with layer.connect() as client:
            observation=client.observe(layer.session,capture=True)
            editor=next(w['Handle'] for w in observation.data['result']['windows'] if w['ClassName']=='Scintilla' and w['Visible'])
            for kind,fields in [('chord',dict(keys=[17,65])),('text',dict(text='This application lives in its own VIAVW layer.\r\nClosing the launcher does not close it.\r\n')),('chord',dict(keys=[17,83]))]:
                client.act(client.observe(layer.session),kind,window=editor,**fields)
        deadline=time.monotonic()+3
        while 'Closing the launcher' not in Path(setup['Document']).read_text(encoding='utf-8-sig'):
            if time.monotonic()>deadline:raise RuntimeError('Application save was not verified; layer retained')
            time.sleep(.02)
        if smoke:
            result['closed']=layer.close_application()['result']['closed']
            if not result['closed']:raise RuntimeError('Application close needs attention')
        else:layer.show(human=True,reason='Your independent application layer is ready.')
        result['passed']=True
    except Exception as error:
        result['passed']=False;result['error']=str(error)
        print('Application retained; use its descriptor to inspect or recover it.',flush=True)
        raise
    finally:
        (output/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2),flush=True)
        print("VIAVW_EVIDENCE="+str((output/"result.json").resolve()),flush=True)
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['demo','list','status','show','human','hide','recover','close'])
    parser.add_argument('--descriptor');parser.add_argument('--host');parser.add_argument('--smoke',action='store_true')
    parser.add_argument('--force',action='store_true',help='Explicitly discard this layer, including unsaved work')
    args=parser.parse_args();host=checked_host(args.host)
    if args.force and args.action!='close':parser.error('--force is only valid with close')
    if args.smoke and args.action!='demo':parser.error('--smoke is only valid with demo')
    if args.action=='demo':demo(host,args.smoke);return
    if args.action=='list':print(json.dumps(Layers(host).list(),indent=2));return
    if not args.descriptor:parser.error('--descriptor is required')
    layer=handle_for(args.descriptor,host)
    if args.action in ('show','human'):result=layer.show(human=args.action=='human')
    elif args.action=='close':result=layer.close_application(force=args.force)
    elif args.action=='recover':
        from viavw_layers import owner_alive
        info=read_metadata(layer.descriptor)
        if not owner_alive(info):layer=layer.adopt()
        with layer.connect() as client:
            state=client.request('status',layer.session)['session']
            if not state['keeperReady']:client.request('repair-keeper',layer.session,expectedRevision=state['revision']);state=client.request('status',layer.session)['session']
            result=client.request('recover',layer.session,expectedRevision=state['revision']) if state['workerFaulted'] else dict(session=state)
        result['note']='Inspect the running application before explicitly reconciling automation. Recovery does not replay input.'
    else:
        with layer.connect() as client:result=client.request('viewer',layer.session,visible=False) if args.action=='hide' else client.request('status',layer.session)
    print(json.dumps(result,indent=2))
if __name__=='__main__':main()

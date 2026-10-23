"""Disposable real installed application acceptance for independent layer owners."""
import concurrent.futures
import ctypes
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid
from viavw_layers import ROOT,LayerClient,LayerError,start_owner

def alive(pid):
    kernel=ctypes.WinDLL("kernel32",use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    handle=kernel.OpenProcess(0x100000,False,pid)
    if not handle:return False
    try:return kernel.WaitForSingleObject(handle,0)==258
    finally:kernel.CloseHandle(handle)

def main():
    host=Path(sys.argv[1]).resolve();output=ROOT/"artifacts"/("durable-verification-"+uuid.uuid4().hex);output.mkdir()
    checks={};owners=[];clients=[];owned=set();failure=None;measurements={}
    def check(name,value):
        checks[name]=bool(value);print(("PASS " if value else "FAIL ")+name,flush=True)
        if not value:raise AssertionError(name)
    def connect(d):
        c=LayerClient(d,host);clients.append(c);return c
    def start(index,verification=False):
        d,p=start_owner(output/("owner-"+str(index)),host,verification);owners.append((d,p));owned.add(p.pid);return connect(d)
    def state(c,sid):
        r=c.request("status",sid)["session"];owned.update(r.get("ownedProcessIds",[]));
        if r.get("keeperPid"):owned.add(r["keeperPid"])
        return r
    def observe(c,sid,**kwargs):
        for _ in range(10):
            try:return c.request("observe",sid,**kwargs)
            except LayerError as e:
                if e.code not in ("no_surface","observation_changed","capture_unavailable"):raise
                time.sleep(.02)
        raise AssertionError("Could not observe actual application")
    def send(c,sid,window,kind,**fields):
        current=state(c,sid)
        return c.request("input",sid,expectedRevision=current["revision"],expectedInputSequence=current["inputSequence"],window=window,kind=kind,**fields)
    def close(c,sid,force=False):return c.request("close",sid,expectedRevision=state(c,sid)["revision"],force=force)
    try:
        fixture=output/"notepad"
        subprocess.run([str(host),"--prepare-installed-notepad-gdi-demo",str(fixture)],check=True,capture_output=True)
        setup=json.loads((fixture/"launch.json").read_text(encoding="utf-8-sig"))
        # Setup is serialized by the existing installed-application fixture.
        c=start(0,True);sid=c.request("create",executable=setup["Executable"],arguments=setup["Arguments"])["id"]
        first=observe(c,sid,capture=True);s=first["session"];pid=s["applicationPid"];owned.update(s["ownedProcessIds"])
        editor=next(w["Handle"] for w in first["result"]["windows"] if w["ClassName"]=="Scintilla" and w["Visible"])
        image=Path(first["result"]["image"]["path"]);check("actual installed Notepad++ has captured pixels",image.stat().st_size>1000)
        send(c,sid,editor,"chord",keys=[17,65]);send(c,sid,editor,"text",text="durable layer work");send(c,sid,editor,"chord",keys=[17,83])
        document=Path(setup["Document"])
        def wait_text(expected):
            end=time.monotonic()+5
            while time.monotonic()<end:
                if document.read_text(encoding="utf-8-sig")==expected:return True
                time.sleep(.02)
            return False
        check("ordinary editing saved actual exact text",wait_text("durable layer work"))
        d=owners[0][0];c.process.kill();c.process.wait();c.close();time.sleep(.05)
        check("controller process death preserves unsaved-capable app and owner",alive(pid) and owners[0][1].poll() is None)
        c=connect(d);check("reconnect preserves same application PID",state(c,sid)["applicationPid"]==pid)
        # Exercise the old lifetime request boundary without generating datasets.
        started=time.monotonic();replay=None
        for i in range(4200):
            request=dict(protocol=2,id=str(c.next_sequence),epoch=c.epoch,op="status",session=sid)
            r=c.raw(request);c.next_sequence+=1
            if i==0:replay=request
            if not r["ok"]:raise AssertionError(r)
        measurements["4200StatusWallMilliseconds"]=(time.monotonic()-started)*1000
        check("same owner passes 4096 requests without app restart",state(c,sid)["applicationPid"]==pid)
        expired=c.raw(replay);check("expired sequence is never re-executed",expired["error"]["code"]=="result_expired")
        # Six simultaneous independent owners, no four-session ceiling.
        peers=[]
        for i in range(1,6):
            peer=start(i);ps=peer.request("create",executable=r"C:\Program Files\7-Zip\7zFM.exe",arguments=[])["id"]
            peers.append((peer,ps));state(peer,ps)
        check("six real application layers run independently",len({state(c,sid)["layerId"]}|{state(p,s)["layerId"] for p,s in peers})==6)
        send(c,sid,editor,"key",key=35)
        fault_request=dict(protocol=2,id=str(c.next_sequence),epoch=c.epoch,op="research-stall-after-input",session=sid,window=editor,text="_once",expectedRevision=state(c,sid)["revision"])
        # state() above consumes its own sequence; choose the current sequence.
        fault_request["id"]=str(c.next_sequence)
        with concurrent.futures.ThreadPoolExecutor() as pool:
            pending=pool.submit(c.raw,fault_request);time.sleep(.1)
            started=time.monotonic();peer_state=state(*peers[0]);measurements["peerDuringStallMilliseconds"]=(time.monotonic()-started)*1000
            fault=pending.result(timeout=10);c.next_sequence+=1
        check("another layer stays responsive during worker stall",peer_state["state"]=="ready" and measurements["peerDuringStallMilliseconds"]<250)
        check("stalled mutating command reports unknown outcome",not fault["ok"] and fault["error"]["code"]=="layer_suspended" and fault["error"]["outcome"]=="unknown")
        check("failed worker preserves actual application and pauses layer",alive(pid) and state(c,sid)["state"]=="paused_worker_fault")
        check("exact uncertain request is deduplicated",c.raw(fault_request)==fault)
        c.close();c=connect(d);paused=state(c,sid)
        check("reconnect retains paused layer and original app",paused["applicationPid"]==pid and paused["workerFaulted"])
        c.request("recover",sid,expectedRevision=paused["revision"])
        recovered=state(c,sid);check("worker recovery preserves same app and requires reconciliation",recovered["applicationPid"]==pid and recovered["recoveryPending"] and not recovered["workerFaulted"])
        denied=False
        try:send(c,sid,editor,"text",text="MUST_NOT_TYPE")
        except LayerError as e:denied=e.code=="layer_suspended"
        check("new input is rejected before post-recovery reconciliation",denied)
        observed=observe(c,sid,capture=True);token=observed["result"]["recoveryToken"]
        check("post-recovery observation supplies a fresh reconciliation token",bool(token))
        c.request("reconcile",sid,expectedRevision=observed["session"]["revision"],observationToken=token,**{"continue":True})
        send(c,sid,editor,"chord",keys=[17,83]);check("actual post-fault file contains mutation exactly once",wait_text("durable layer work_once"))
        send(c,sid,editor,"text",text="_continued");send(c,sid,editor,"chord",keys=[17,83]);check("same app continues actual editing after recovery",wait_text("durable layer work_once_continued"))
        # Ring retention must replace old captures rather than closing work.
        for i in range(265):observe(c,sid,capture=True)
        observations=[p for p in (output/"owner-0/observations").glob("*") if not p.name.startswith(".")]
        check("observation files rotate within 256-file budget",len(observations)<=256 and not image.exists())
        check("observation bytes stay within 64 MiB",sum(p.stat().st_size for p in observations)<=64*1024*1024)
        # Shutdown is explicit and must not silently discard an open app.
        refused=False
        try:c.request("shutdown")
        except LayerError as e:refused=e.code=="applications_running"
        check("ordinary owner shutdown refuses running application",refused and alive(pid))
        close(c,sid)
        # More than sixteen lifetimes, same independent owner, no storage blast.
        peer,ps=peers.pop();close(peer,ps)
        for i in range(18):
            ps=peer.request("create",executable=r"C:\Program Files\7-Zip\7zFM.exe",arguments=[])["id"];state(peer,ps);close(peer,ps)
        check("same owner supports more than sixteen application lifetimes",peer.request("list") is not None)
        for peer,ps in peers:close(peer,ps)
        for desc,process in owners:
            # Close all controller relays before reconnecting for explicit stop.
            for client in clients:
                if client.descriptor==desc:client.close()
            with LayerClient(desc,host) as final:final.request("shutdown")
            check("owned layer owner exits on explicit shutdown "+str(process.pid),process.wait(timeout=10)==0)
    except Exception as error:
        import traceback
        failure=traceback.format_exc();print(failure,flush=True)
    finally:
        for client in clients:
            try:client.close()
            except Exception:pass
        for desc,process in owners:
            if process.poll() is None:
                try:
                    with LayerClient(desc,host) as end:end.request("shutdown",force=True)
                    process.wait(timeout=10)
                except Exception:process.kill();process.wait(timeout=5)
    survivors=[pid for pid in owned if alive(pid)]
    passed=failure is None and all(checks.values()) and not survivors
    report=dict(passed=passed,failure=failure,checks=checks,measurements=measurements,owned=sorted(owned),survivors=survivors,physicalHumanAcceptance="pending",scope="Actual installed Notepad++ and 7-Zip; caller disconnect, worker failure, request and artifact limits. Not owner-process-crash preservation or full unattended acceptance.")
    (output/"verification.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print("VIAVW_EVIDENCE="+str((output/"verification.json").resolve()),flush=True)
    print(json.dumps(dict(passed=passed,output=str(output),survivors=survivors)),flush=True)
    if not passed:raise SystemExit(1)
if __name__=="__main__":main()

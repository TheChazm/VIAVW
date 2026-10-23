"""Reconnectable local VIAVW layer owners. Closing a client never closes an app.
Protocol 2 is separate from legacy short-lived protocol 1. No input retries.
"""
import argparse
import ctypes
from dataclasses import dataclass
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import uuid

ROOT=Path(__file__).resolve().parents[1]

def metadata_path(path):
    # Resolve the stable directory, not a leaf that Windows may rename/delete
    # during atomic publication while GetFinalPathNameByHandle is inspecting it.
    absolute=Path(os.path.abspath(path));parent=absolute.parent.resolve()
    if not parent.is_relative_to(ROOT/"artifacts"):raise ValueError("Use VIAVW metadata: "+str(absolute))
    return parent/absolute.name

def _metadata_handle(path,access=0x80000000):
    kernel=ctypes.WinDLL("kernel32",use_last_error=True)
    kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p
    handle=kernel.CreateFileW(str(path),access,7,None,3,0,None)
    if handle==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
    return handle

def read_metadata(path):
    """Read one complete snapshot without preventing an atomic replacement."""
    import msvcrt
    path=metadata_path(path)
    for attempt in range(6):
        try:
            handle=_metadata_handle(path)
            try:descriptor=msvcrt.open_osfhandle(handle,os.O_RDONLY|os.O_BINARY)
            except Exception:
                kernel=ctypes.WinDLL("kernel32");kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.CloseHandle(handle);raise
            with os.fdopen(descriptor,"rb") as stream:return json.loads(stream.read().decode("utf-8-sig"))
        except OSError as error:
            if attempt==5 or getattr(error,"winerror",None) not in (2,32,33):raise
            time.sleep(.01)

def write_metadata(path,data):
    path=metadata_path(path)
    temporary=path.with_suffix(".tmp");temporary.write_text(json.dumps(data,indent=2),encoding="utf-8")
    kernel=ctypes.WinDLL("kernel32",use_last_error=True)
    kernel.ReplaceFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_wchar_p,ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_void_p];kernel.ReplaceFileW.restype=ctypes.c_int
    for attempt in range(51):
        try:
            if not kernel.ReplaceFileW(str(path),str(temporary),None,0,None,None):
                error=ctypes.get_last_error()
                if error==2:temporary.replace(path)
                else:raise ctypes.WinError(error)
            return
        except OSError as error:
            sharing=getattr(error,"winerror",None) in (32,33,1175)
            if getattr(error,"winerror",None)==5:
                try:
                    handle=_metadata_handle(path,access=0x10000)
                    kernel=ctypes.WinDLL("kernel32");kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.CloseHandle(handle)
                except OSError as probe:sharing=getattr(probe,"winerror",None) in (32,33)
            if attempt==50 or not sharing:raise
            time.sleep(.02)

class LayerError(RuntimeError):
    def __init__(self,error):
        self.code=error["code"];self.outcome=error.get("outcome","unknown");self.session=error.get("session");self.keeper=error.get("keeperDescriptor");self.layer=None
        super().__init__(error["message"])

def checked_host(host=None):
    if host is None:
        import hashlib
        manifest=read_metadata(ROOT/"artifacts/durable-layers-current.json")
        host=Path(manifest["executable"])
        if hashlib.sha256(host.with_suffix(".dll").read_bytes()).hexdigest().upper()!=manifest["assemblySha256"]:raise ValueError("Layer owner integrity mismatch")
    host=Path(host).resolve()
    if not host.is_relative_to(ROOT/"src/LayerHost/bin") or host.name!="LayerHost.exe" or not host.is_file():raise ValueError("Use a preserved VIAVW host")
    return host

def require_capacity(recovery=False):
    class Memory(ctypes.Structure):
        _fields_=[("length",ctypes.c_uint32),("load",ctypes.c_uint32)]+[(name,ctypes.c_uint64) for name in ["totalPhysical","availablePhysical","totalCommit","availableCommit","totalVirtual","availableVirtual","extended"]]
    value=Memory();value.length=ctypes.sizeof(value)
    if not ctypes.WinDLL("kernel32",use_last_error=True).GlobalMemoryStatusEx(ctypes.byref(value)):raise ctypes.WinError(ctypes.get_last_error())
    physical_reserve=(128 if recovery else 512)*1024**2
    commit_reserve=(128 if recovery else 1024)*1024**2
    if value.availablePhysical<physical_reserve or value.availableCommit<commit_reserve:
        raise MemoryError("Insufficient layer-0 headroom for "+("a control-owner replacement" if recovery else "a new layer")+
                          f"; available physical/commit {value.availablePhysical//1024**2}/{value.availableCommit//1024**2} MiB, required {physical_reserve//1024**2}/{commit_reserve//1024**2} MiB. Running applications are retained.")

def resolve_keeper(descriptor):
    descriptor=metadata_path(descriptor)
    for _ in range(4):
        if not descriptor.is_relative_to(ROOT/"artifacts"):raise ValueError("Keeper must be within VIAVW artifacts")
        data=read_metadata(descriptor)
        if "keeperDescriptor" not in data:return descriptor,data
        descriptor=metadata_path(data["keeperDescriptor"])
    raise ValueError("Keeper locator cycle")

def start_owner(output=None,host=None,verification=False,adopt=None):
    host=checked_host(host);require_capacity(recovery=adopt is not None)
    if output is None and adopt:
        _,keeper_info=resolve_keeper(adopt)
        output=Path(keeper_info["snapshot"]["createRecord"]).parent.parent
    output=Path(output or ROOT/"artifacts/layers"/("layer-"+uuid.uuid4().hex)/"owner").resolve()
    if not output.is_relative_to(ROOT/"artifacts") or (output.exists() and not adopt):raise ValueError("Use a fresh layer directory or explicitly adopt an existing layer")
    output.parent.mkdir(parents=True,exist_ok=True)
    if adopt and (output/"layer-owner.json").exists():
        previous=read_metadata(output/"layer-owner.json")
        if owner_alive(previous):raise RuntimeError("Existing owner is alive; no competing owner started")
    with output.with_suffix(".owner.log").open("wb" if adopt else "xb") as log:
        command=[str(host),"--layer-adopt-service",str(output),str(metadata_path(adopt))] if adopt else [str(host),"--test-layer-service" if verification else "--layer-service",str(output)]
        process=subprocess.Popen(command,stdin=subprocess.DEVNULL,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    descriptor=output/"layer-owner.json";deadline=time.monotonic()+12
    while True:
        try:
            if descriptor.exists() and read_metadata(descriptor).get("pid")==process.pid:break
        except (OSError,ValueError):pass
        if process.poll() is not None:raise RuntimeError("Layer owner startup failed: "+str(output.with_suffix(".owner.log")))
        if time.monotonic()>deadline:raise TimeoutError("Owner readiness unknown; inspect "+str(output)+" before starting another")
        time.sleep(.01)
    return descriptor,process

class LayerClient:
    def __init__(self,descriptor,host=None):
        self.descriptor=metadata_path(descriptor)
        if not self.descriptor.is_relative_to(ROOT/"artifacts"):raise ValueError("Use a VIAVW layer descriptor")
        self.info=read_metadata(self.descriptor);self.host=checked_host(host)
        self.process=subprocess.Popen([str(self.host),"--layer-connect",str(self.descriptor)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8",creationflags=subprocess.CREATE_NO_WINDOW)
        self.replies=queue.Queue();self.usable=True;self.command_lock=threading.RLock()
        def read():
            try:
                for line in self.process.stdout:self.replies.put(json.loads(line))
            except Exception as error:self.replies.put(error)
            finally:self.replies.put(EOFError("Client disconnected; reconcile outcome before new input"))
        threading.Thread(target=read,daemon=True).start()
        try:
            hello=self.raw({"protocol":2,"id":"0","op":"hello"})
            if not hello["ok"]:raise LayerError(hello["error"])
            data=hello["data"]
            if data["epoch"]!=self.info["epoch"]:raise RuntimeError("Layer owner epoch changed")
            self.epoch=data["epoch"];self.next_sequence=data["nextSequence"];self.hello=data
        except Exception:
            self.close()
            raise
    def raw(self,request,timeout=90):
        with self.command_lock:return self._raw(request,timeout)
    def _raw(self,request,timeout):
        if not self.usable:raise RuntimeError("Disconnected client; create a new connection, never replay uncertain input")
        line=request if isinstance(request,str) else json.dumps(request,separators=(",",":"),ensure_ascii=False)
        try:
            self.process.stdin.write(line+"\n");self.process.stdin.flush();reply=self.replies.get(timeout=timeout)
            if isinstance(reply,Exception):raise reply
            expected=json.loads(line).get("id")
            if reply.get("id")!=expected:raise RuntimeError("Misframed response; outcome unknown")
            return reply
        except Exception:
            self.close()
            raise
    def request(self,op,session=None,**fields):
        with self.command_lock:return self._request(op,session,**fields)
    def _request(self,op,session=None,**fields):
        request=dict(protocol=2,id=str(self.next_sequence),epoch=self.epoch,op=op)
        if session is not None:request["session"]=session
        if any(k in fields for k in ("protocol","id","epoch")):raise ValueError("Transport identity fields cannot be overridden")
        request.update(fields);reply=self.raw(request)
        # A sequence_gap or stale epoch was not committed. Reconnect before any
        # new decision; never silently renumber/retry a mutation.
        if not reply["ok"] and reply["error"]["code"] in ("sequence_gap","stale_host_epoch","id_conflict"):
            self.close();raise LayerError(reply["error"])
        self.next_sequence+=1
        if not reply["ok"]:raise LayerError(reply["error"])
        result=reply["data"]
        if op=="create":
            try:self._record(result["id"],result["observation"]["session"])
            except OSError as error:result["managementWarning"]="Application created; registry write failed: "+str(error)
        return result
    def _record(self,session,state):
        path=self.descriptor.parent/"layer-record.json"
        record=dict(schema=1,descriptor=str(self.descriptor),session=session,layerId=state["layerId"],applicationPid=state["applicationPid"],keeperDescriptor=state.get("keeperDescriptor"),host=str(self.host))
        write_metadata(path,record)
    def observe(self,session,wait_seconds=1.0,**options):
        deadline=time.monotonic()+max(0,min(wait_seconds,3))
        while True:
            try:return LayerObservation(str(self.descriptor),self.epoch,session,self.request("observe",session,**options))
            except LayerError as error:
                # A dialog disappearing can briefly leave no selected surface.
                # Only retry these read-only, definitely-not-started results.
                if error.outcome!="not_started" or error.code not in ("no_surface","observation_changed","capture_unavailable") or time.monotonic()>=deadline:raise
                time.sleep(.025)
    def act(self,observation,kind,window=None,**fields):
        if observation.epoch!=self.epoch or observation.descriptor!=str(self.descriptor):raise ValueError("Observation belongs to a different owner; observe after reconnect/replacement")
        state=observation.data["session"]
        return self.request("input",observation.session,expectedRevision=state["revision"],expectedInputSequence=state["inputSequence"],window=window or state["root"],kind=kind,**fields)
    def close(self):
        self.usable=False
        if self.process.poll() is None:
            try:self.process.stdin.close();self.process.wait(timeout=3)
            except (BrokenPipeError,OSError,subprocess.TimeoutExpired):
                self.process.kill();self.process.wait(timeout=3)
        for stream in [self.process.stdout,self.process.stderr]:
            if stream:stream.close()
    def __enter__(self):return self
    def __exit__(self,*_):self.close()

@dataclass(frozen=True)
class LayerObservation:
    descriptor:str
    epoch:str
    session:str
    data:dict

@dataclass(frozen=True)
class LayerHandle:
    descriptor:Path
    session:str
    host:Path
    keeper:Path
    def connect(self):return LayerClient(self.descriptor,self.host)
    def show(self,human=False,reason=None):
        with self.connect() as client:
            if reason:client.request("attention",self.session,reason=reason)
            if human:
                state=client.request("status",self.session)["session"]
                client.request("ownership",self.session,expectedRevision=state["revision"],owner="Human")
            return client.request("viewer",self.session,visible=True)
    def replace_owner(self):
        with self.connect() as client:
            result=client.request("replace-owner",self.session)
        info=read_metadata(self.descriptor)
        deadline=time.monotonic()+5
        while owner_alive(info):
            if time.monotonic()>deadline:raise TimeoutError("Owner exit is unconfirmed; no competing owner started")
            time.sleep(.01)
        descriptor,process=start_owner(host=self.host,adopt=result["keeperDescriptor"])
        with LayerClient(descriptor,self.host) as client:
            state=client.request("status",self.session)["session"];client._record(self.session,state)
        return LayerHandle(descriptor,self.session,self.host,Path(result["keeperDescriptor"]))
    def close_application(self,force=False):
        with self.connect() as client:
            status=client.request("status",self.session)["session"]
            result=client.request("close",self.session,expectedRevision=status["revision"],force=force)
            if result["result"]["closed"]:client.request("shutdown")
            return result
    def reconcile(self,context_matches):
        with self.connect() as client:
            observation=client.observe(self.session,capture=True)
            if not context_matches(observation.data):raise RuntimeError("Recovered application context does not match; layer remains paused")
            state=observation.data["session"]
            return client.request("reconcile",self.session,expectedRevision=state["revision"],observationToken=observation.data["result"]["recoveryToken"],**{"continue":True})
    def adopt(self):
        info=read_metadata(self.descriptor)
        if owner_alive(info):raise RuntimeError("Existing owner is alive; reconnect or explicitly replace it")
        descriptor,process=start_owner(host=self.host,adopt=self.keeper)
        with LayerClient(descriptor,self.host) as client:client._record(self.session,client.request("status",self.session)["session"])
        return LayerHandle(descriptor,self.session,self.host,self.keeper)

def owner_alive(info):
    kernel=ctypes.WinDLL("kernel32",use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
    handle=kernel.OpenProcess(0x1000|0x100000,False,info["pid"])
    if not handle:
        error=ctypes.get_last_error()
        if error==87:return False
        raise ctypes.WinError(error)
    try:
        if kernel.WaitForSingleObject(handle,0)==0:return False
        if "createdFileTime" not in info:raise ValueError("Owner descriptor lacks a process creation identity")
        times=[ctypes.c_uint64() for _ in range(4)];kernel.GetProcessTimes.argtypes=[ctypes.c_void_p]+[ctypes.POINTER(ctypes.c_uint64)]*4
        if not kernel.GetProcessTimes(handle,*[ctypes.byref(t) for t in times]):raise ctypes.WinError(ctypes.get_last_error())
        return times[0].value==info["createdFileTime"]
    finally:kernel.CloseHandle(handle)

class Layers:
    def __init__(self,host=None):self.host=checked_host(host)
    def create(self,executable=None,arguments=None,**profile):
        descriptor,process=start_owner(host=self.host)
        with LayerClient(descriptor,self.host) as client:
            fields=dict(profile)
            if executable is not None:fields["executable"]=str(executable);fields["arguments"]=arguments or []
            try:created=client.request("create",**fields)
            except LayerError as error:
                if error.session and error.keeper:error.layer=LayerHandle(descriptor,error.session,self.host,Path(error.keeper))
                elif error.outcome=="not_started":
                    try:client.request("shutdown")
                    except Exception as shutdown_error:error.add_note("Owner cleanup needs attention: "+str(shutdown_error)) if hasattr(error,"add_note") else None
                raise
            return LayerHandle(descriptor,created["id"],self.host,Path(created["observation"]["session"]["keeperDescriptor"]))
    def list(self,root=None):
        directory=Path(root or ROOT/"artifacts/layers").resolve()
        if not directory.is_relative_to(ROOT/"artifacts"):raise ValueError("Use the VIAVW layer registry")
        result={}
        for file in directory.rglob("layer-record.json"):
            try:
                record=read_metadata(file);descriptor=metadata_path(record["descriptor"])
                if not descriptor.is_relative_to(ROOT/"artifacts"):raise ValueError("Descriptor is outside VIAVW artifacts")
                info=read_metadata(descriptor);record["ownerAlive"]=owner_alive(info)
            except (OSError,KeyError,ValueError) as error:
                result[str(file)]=dict(record=str(file),state="management_attention",error=str(error));continue
            record["state"]="owned" if record["ownerAlive"] else "owner_disconnected"
            try:
                _,keeper=resolve_keeper(record["keeperDescriptor"]);record["keeperPid"]=keeper["pid"]
                record["keeperAlive"]=owner_alive(keeper)
                record["state"]=("owned" if record["ownerAlive"] else "retained") if record["keeperAlive"] else "closed_or_unavailable"
                record["retainedOwnerPid"]=keeper["ownerPid"]
            except (OSError,KeyError,ValueError) as error:record["state"]="management_attention";record["error"]=str(error)
            result[record["layerId"]]=record
        return list(result.values())

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("action",choices=["start","call"]);parser.add_argument("--descriptor");parser.add_argument("--host");parser.add_argument("--request",default='{"op":"list"}')
    args=parser.parse_args()
    if args.action=="start":
        descriptor,process=start_owner(host=args.host);print(json.dumps(dict(descriptor=str(descriptor),ownerPid=process.pid)))
    else:
        fields=json.loads(args.request);op=fields.pop("op")
        with LayerClient(args.descriptor,host=args.host) as client:print(json.dumps(client.request(op,**fields),ensure_ascii=False))
if __name__=="__main__":main()

"""Crash the owning VIAVW process; preserve and adopt the actual unsaved app."""
import json
from pathlib import Path
import subprocess
import sys
import time
import traceback
import uuid
from viavw_layers import ROOT,LayerClient,LayerError,start_owner
from importlib.util import spec_from_file_location,module_from_spec
spec=spec_from_file_location("durable_test",ROOT/"scripts/test-durable-layers.py");helpers=module_from_spec(spec);spec.loader.exec_module(helpers);alive=helpers.alive

def main():
 host=Path(sys.argv[1]).resolve();out=ROOT/"artifacts"/("owner-replacement-"+uuid.uuid4().hex);out.mkdir();checks={};owners=[];client=None;sid=None;keeper=None;owned=set();failure=None
 def check(name,value):
  checks[name]=bool(value);print(("PASS " if value else "FAIL ")+name,flush=True)
  if not value:raise AssertionError(name)
 def start(label,adopt=None):
  nonlocal client
  descriptor,process=start_owner(out/label,host,adopt=adopt);owners.append((descriptor,process));owned.add(process.pid);client=LayerClient(descriptor,host);return client
 def state():
  r=client.request("status",sid)["session"];owned.update(r.get("ownedProcessIds",[]))
  if r.get("keeperPid"):owned.add(r["keeperPid"])
  return r
 def observe():return client.request("observe",sid,capture=True)
 def input(kind,**fields):
  current=state();return client.request("input",sid,expectedRevision=current["revision"],expectedInputSequence=current["inputSequence"],window=editor,kind=kind,**fields)
 def reconcile():
  r=observe();return client.request("reconcile",sid,expectedRevision=r["session"]["revision"],observationToken=r["result"]["recoveryToken"],**{"continue":True})
 try:
  fixture=out/"fixture";subprocess.run([str(host),"--prepare-installed-notepad-gdi-demo",str(fixture)],check=True,capture_output=True);setup=json.loads((fixture/"launch.json").read_text(encoding="utf-8-sig"));document=Path(setup["Document"])
  client=start("first");sid=client.request("create",executable=setup["Executable"],arguments=setup["Arguments"])["id"];first=observe();s=state();pid=s["applicationPid"];keeper=s["keeperDescriptor"];editor=next(w["Handle"] for w in first["result"]["windows"] if w["ClassName"]=="Scintilla" and w["Visible"])
  input("chord",keys=[17,65]);input("text",text="saved work");input("chord",keys=[17,83]);time.sleep(.1);input("text",text="_unsaved_memory")
  check("test has unsaved in-memory work",document.read_text(encoding="utf-8-sig")=="saved work")
  previous_epoch=client.epoch;client.close();owners[-1][1].kill();owners[-1][1].wait();time.sleep(.15)
  check("application and keeper survive owner process crash",alive(pid) and alive(s["keeperPid"]))
  client=start("second",keeper);restored=state();check("new owner adopts same application PID and layer",restored["applicationPid"]==pid and restored["layerId"]==s["layerId"])
  check("adoption preserves Automation ownership but requires reconciliation",restored["owner"]=="Automation" and restored["recoveryPending"])
  stale=client.raw(dict(protocol=2,id=str(client.next_sequence),op="input",epoch=previous_epoch,session=sid,expectedRevision=s["revision"],window=editor,kind="text",text="MUST_NOT_REPLAY"));check("old owner epoch cannot issue input to replacement",stale["error"]["code"]=="stale_host_epoch")
  rejected=False
  try:input("text",text="MUST_NOT_TYPE")
  except LayerError as e:rejected=e.code=="layer_suspended"
  check("replacement rejects input until fresh state accepted",rejected)
  reconcile();input("chord",keys=[17,83]);time.sleep(.1)
  check("actual unsaved application memory survived owner crash",document.read_text(encoding="utf-8-sig")=="saved work_unsaved_memory")
  input("text",text="_continued");input("chord",keys=[17,83]);time.sleep(.1);check("adopted app continues normal editing and saving",document.read_text(encoding="utf-8-sig")=="saved work_unsaved_memory_continued")
  client.request("ownership",sid,expectedRevision=state()["revision"],owner="Human");human=state();client.close();owners[-1][1].kill();owners[-1][1].wait();time.sleep(.15)
  client=start("third",keeper);restored=state();check("Human ownership survives owner replacement",restored["owner"]=="Human" and restored["applicationPid"]==pid)
  r=observe();denied=False
  try:client.request("reconcile",sid,expectedRevision=r["session"]["revision"],observationToken=r["result"]["recoveryToken"],**{"continue":True})
  except LayerError as e:denied=e.code=="human_owns_input"
  check("reconciliation never reclaims Human ownership",denied)
  # Explicit programmatic user-return simulation; physical human acceptance pending.
  client.request("ownership",sid,expectedRevision=state()["revision"],owner="Automation");reconcile();input("text",text="_returned");input("chord",keys=[17,83]);time.sleep(.1)
  check("same app resumes after explicit ownership return",document.read_text(encoding="utf-8-sig")=="saved work_unsaved_memory_continued_returned")
  result=client.request("close",sid,expectedRevision=state()["revision"]);check("adopted app closes gracefully",result["result"]["closed"]);client.request("shutdown");client.close();owners[-1][1].wait(timeout=10)
 except Exception:
  failure=traceback.format_exc();print(failure,flush=True)
 finally:
  if client:
   try:client.close()
   except Exception:pass
  for descriptor,process in owners:
   if process.poll() is None:
    try:
     with LayerClient(descriptor,host) as end:end.request("shutdown",force=True)
     process.wait(timeout=10)
    except Exception:process.kill();process.wait(timeout=5)
  # A crash intentionally leaves a keeper. Recover it solely for explicit cleanup.
  if keeper and any(alive(p) for p in owned):
   try:
    descriptor,process=start_owner(out/("cleanup-"+uuid.uuid4().hex),host,adopt=keeper);owned.add(process.pid)
    with LayerClient(descriptor,host) as end:end.request("shutdown",force=True)
    process.wait(timeout=10)
   except Exception as cleanup:
    print("Cleanup adoption:",cleanup,flush=True)
    # Explicit discard is restricted to this exact disposable retained layer.
    subprocess.run([str(host),"--discard-owned-layer",str(keeper),"--force"],check=True,capture_output=True)
  time.sleep(1.1)
 survivors=[p for p in owned if alive(p)];passed=failure is None and all(checks.values()) and not survivors
 (out/"verification.json").write_text(json.dumps(dict(passed=passed,checks=checks,failure=failure,owned=sorted(owned),survivors=survivors,physicalHumanAcceptance="pending",scope="Owner crash and actual unsaved Notepad++ memory; process/software faults only, not OS reboot or application crash."),indent=2),encoding="utf-8")
 print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
 print(json.dumps(dict(passed=passed,output=str(out),survivors=survivors)),flush=True)
 if not passed:raise SystemExit(1)
if __name__=="__main__":main()

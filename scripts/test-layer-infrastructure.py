"""Real-app keeper repair and hung-owner replacement; no physical input."""
import concurrent.futures
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
 host=Path(sys.argv[1]).resolve();out=ROOT/"artifacts"/("layer-infrastructure-"+uuid.uuid4().hex);out.mkdir();checks={};owners=[];clients=[];owned=set();keeper=None;failure=None;timings={}
 def check(name,value):
  checks[name]=bool(value);print(("PASS " if value else "FAIL ")+name,flush=True)
  if not value:raise AssertionError(name)
 def start(label,verification=False,adopt=None):
  d,p=start_owner(out/label,host,verification=verification,adopt=adopt);owners.append((d,p));owned.add(p.pid);c=LayerClient(d,host);clients.append(c);return c
 def state(c,sid):
  r=c.request("status",sid)["session"];owned.update(r.get("ownedProcessIds",[]))
  if r.get("keeperPid"):owned.add(r["keeperPid"])
  return r
 def send(kind,**fields):
  s=state(c,sid);return c.request("input",sid,expectedRevision=s["revision"],expectedInputSequence=s["inputSequence"],window=editor,kind=kind,**fields)
 try:
  fixture=out/"fixture";subprocess.run([str(host),"--prepare-installed-notepad-gdi-demo",str(fixture)],check=True,capture_output=True);setup=json.loads((fixture/"launch.json").read_text(encoding="utf-8-sig"));document=Path(setup["Document"])
  c=start("first",True);sid=c.request("create",executable=setup["Executable"],arguments=setup["Arguments"])["id"];first=c.request("observe",sid,capture=True);s=state(c,sid);pid=s["applicationPid"];keeper=s["keeperDescriptor"];first_keeper=s["keeperPid"];editor=next(w["Handle"] for w in first["result"]["windows"] if w["ClassName"]=="Scintilla" and w["Visible"])
  send("chord",keys=[17,65]);send("text",text="Infrastructure buffer")
  c.request("viewer",sid,visible=True);c.request("research-close-viewer",sid);hidden=state(c,sid)
  check("viewer close hides presentation and retains application",not hidden["viewerVisible"] and hidden["applicationPid"]==pid and alive(pid))
  c.request("research-stop-keeper",sid,expectedRevision=state(c,sid)["revision"]);started=time.monotonic();deadline=started+10
  while time.monotonic()<deadline:
   current=state(c,sid)
   if current["keeperPid"]!=first_keeper and alive(current["keeperPid"]):break
   time.sleep(.05)
  timings["keeperRepairMilliseconds"]=(time.monotonic()-started)*1000
  check("keeper restarts automatically without application restart",current["keeperPid"]!=first_keeper and current["applicationPid"]==pid and alive(pid))
  check("stable keeper locator survives repair",current["keeperDescriptor"]==keeper)
  send("text",text="_after_keeper");send("chord",keys=[17,83]);time.sleep(.1);check("actual editor buffer survives keeper crash",document.read_text(encoding="utf-8-sig")=="Infrastructure buffer_after_keeper")
  # The owner must repair its keeper even when no control worker is available.
  try:c.request("research-stall-after-input",sid,expectedRevision=state(c,sid)["revision"],window=editor,text="_combined_fault")
  except LayerError as error:check("combined-fault input reports uncertainty",error.code=="layer_suspended" and error.outcome=="unknown")
  else:raise AssertionError("Injected worker stall was acknowledged")
  paused=state(c,sid);prior_keeper=paused["keeperPid"];c.request("research-stop-keeper",sid,expectedRevision=paused["revision"]);deadline=time.monotonic()+10
  while time.monotonic()<deadline:
   current=state(c,sid)
   if current["keeperReady"] and current["keeperPid"]!=prior_keeper:break
   time.sleep(.05)
  check("keeper repair does not depend on a working control worker",current["workerFaulted"] and current["keeperReady"] and current["applicationPid"]==pid)
  c.request("recover",sid,expectedRevision=current["revision"]);recovered=c.request("observe",sid,capture=True)
  c.request("reconcile",sid,expectedRevision=recovered["session"]["revision"],observationToken=recovered["result"]["recoveryToken"],**{"continue":True})
  send("chord",keys=[17,83]);time.sleep(.1);check("combined keeper and worker faults preserve unsaved memory exactly once",document.read_text(encoding="utf-8-sig")=="Infrastructure buffer_after_keeper_combined_fault")
  # Exports are bounded across owner replacement, not just within one process.
  for _ in range(260):c.request("observe",sid,capture=True)
  old_store=Path(first["result"]["image"]["path"]).parent
  peer=start("peer");peer_sid=peer.request("create",executable=r"C:\Program Files\7-Zip\7zFM.exe",arguments=[])["id"];state(peer,peer_sid)
  send("text",text="_unsaved_before_ui_stall")
  with concurrent.futures.ThreadPoolExecutor() as pool:
   pending=pool.submit(c.request,"research-stall-owner-ui",sid);time.sleep(.2);started=time.monotonic();peer_state=state(peer,peer_sid);timings["peerDuringOwnerHangMilliseconds"]=(time.monotonic()-started)*1000
   try:pending.result(timeout=14);raise AssertionError("Hung owner was incorrectly acknowledged")
   except LayerError as error:check("hung owner reaches bounded unknown-outcome response",error.code=="owner_unresponsive" and error.outcome=="unknown")
  check("another layer remains usable during owner UI hang",peer_state["state"]=="ready" and timings["peerDuringOwnerHangMilliseconds"]<250)
  check("registry lists unresponsive owner explicitly",any(v["id"]==sid and v["state"]=="owner_unresponsive" for v in c.request("list")))
  result=c.request("replace-owner",sid);check("replacement request preserves existing keeper",result["replacing"] and result["keeperDescriptor"]==keeper);c.close();owners[0][1].wait(timeout=5);check("unresponsive owner exits without its application",alive(pid))
  c=start("replacement",adopt=keeper);restored=state(c,sid);check("hung-owner replacement retains same app",restored["applicationPid"]==pid and restored["recoveryPending"])
  obs=c.request("observe",sid,capture=True);new_image=Path(obs["result"]["image"]["path"]);check("observation retention store survives owner replacement",new_image.parent==old_store and len([p for p in old_store.iterdir() if not p.name.startswith(".")])<=256)
  c.request("reconcile",sid,expectedRevision=restored["revision"],observationToken=obs["result"]["recoveryToken"],**{"continue":True})
  send("chord",keys=[17,83]);time.sleep(.1);check("unsaved app memory survives hung owner and keeper repair",document.read_text(encoding="utf-8-sig")=="Infrastructure buffer_after_keeper_combined_fault_unsaved_before_ui_stall")
  c.request("close",sid,expectedRevision=state(c,sid)["revision"]);peer.request("close",peer_sid,expectedRevision=state(peer,peer_sid)["revision"])
  for client in clients:client.close()
  for descriptor,process in owners:
   if process.poll() is None:
    with LayerClient(descriptor,host) as end:end.request("shutdown")
    process.wait(timeout=10)
 except Exception:
  failure=traceback.format_exc();print(failure,flush=True)
 finally:
  for client in clients:
   try:client.close()
   except Exception:pass
  for descriptor,process in owners:
   if process.poll() is None:
    try:
     with LayerClient(descriptor,host) as end:end.request("shutdown",force=True)
     process.wait(timeout=12)
    except Exception:process.kill();process.wait(timeout=5)
  if failure and keeper:
   try:subprocess.run([str(host),"--discard-owned-layer",str(keeper),"--force"],capture_output=True,timeout=10)
   except Exception:pass
  time.sleep(1.1)
 survivors=[p for p in owned if alive(p)];passed=failure is None and all(checks.values()) and not survivors
 (out/"verification.json").write_text(json.dumps(dict(passed=passed,checks=checks,timings=timings,failure=failure,owned=sorted(owned),survivors=survivors,physicalHumanAcceptance="pending"),indent=2),encoding="utf-8");print(json.dumps(dict(passed=passed,output=str(out),survivors=survivors)),flush=True)
 print("VIAVW_EVIDENCE="+str((out/"verification.json").resolve()),flush=True)
 if not passed:raise SystemExit(1)
if __name__=="__main__":main()

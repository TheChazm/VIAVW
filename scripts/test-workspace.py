"""Disposable installed Notepad++ acceptance of the adapter-facing workspace."""
import ctypes
import json
from pathlib import Path
import runpy
import subprocess
import time
import uuid

from viavw_client import ROOT, ViavwClient, ViavwError, verified_host
from viavw_workspace import ShutdownBlocked, ViavwWorkspace


def alive(pid):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.OpenProcess(0x100000, False, pid)
    if not handle:
        if ctypes.get_last_error() == 87:
            return False
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return kernel.WaitForSingleObject(handle, 0) == 258
    finally:
        kernel.CloseHandle(handle)


def main():
    output = ROOT / "artifacts" / ("workspace-verification-" + uuid.uuid4().hex)
    output.mkdir()
    checks = {}
    owned = set()
    workspace = None
    failure = None
    started = time.monotonic()
    def check(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise AssertionError(name)
        print("PASS", name, flush=True)
    def rejected(code, action):
        try:
            action()
        except ViavwError as error:
            return error.code == code
        return False
    def wait_for(predicate):
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            try:
                if predicate():
                    return True
            except PermissionError as error:
                if getattr(error, "winerror", None) not in (32, 33):
                    raise
            time.sleep(.05)
        return False
    try:
        host = verified_host()
        subprocess.run([str(host), "--prepare-installed-notepad-gdi-demo", str(output / "documents")],
                       check=True, creationflags=subprocess.CREATE_NO_WINDOW, stdout=subprocess.DEVNULL)
        setup = json.loads((output / "documents/launch.json").read_text(encoding="utf-8-sig"))
        document = Path(setup["Document"])
        guard = runpy.run_path(str(ROOT / "scripts/demo-agent.py"))["active_document_matches"]
        client = ViavwClient(output / "host", host)
        workspace = ViavwWorkspace(output / "host", client=client)
        owned.add(client.process.pid)
        session = workspace.create(executable=setup["Executable"], arguments=setup["Arguments"])
        state = workspace.status(session)
        owned.update(state["ownedProcessIds"])
        original_pid = state["applicationPid"]
        # Read-only polling accommodates initial window creation; never replay input.
        observation = None
        for _ in range(30):
            try:
                observation = workspace.observe(session, capture=True)
                if guard(observation.data, document):
                    break
            except ViavwError as error:
                if error.code not in ("observation_changed", "no_surface"):
                    raise
            time.sleep(.1)
        check("installed_application_context", observation is not None and guard(observation.data, document))
        editor = next(w["Handle"] for w in observation.data["result"]["windows"]
                      if w["ClassName"] == "Scintilla" and w["Visible"])
        workspace.input(observation, "click", window=editor, x=100, y=10)
        workspace.input(observation, "chord", keys=[17, 65])
        text = "Workspace acceptance: actual installed application."
        workspace.input(observation, "text", text=text)
        workspace.input(observation, "key", key=13)
        workspace.input(observation, "text", text="Enter and Backspace work.X")
        workspace.input(observation, "key", key=8)
        workspace.input(observation, "chord", keys=[17, 83])
        expected = text + "\nEnter and Backspace work."
        check("actual_edit_enter_backspace_and_save", wait_for(lambda: document.read_text(encoding="utf-8-sig") == expected))
        observation = workspace.observe(session)
        workspace.request_human(observation, "Programmatic workspace acceptance; no physical human claim")
        check("automation_blocked_during_human_control", rejected("resume_required", lambda: workspace.input(observation, "text", text="MUST NOT APPEAR")))
        check("resume_never_reclaims_human_control", rejected("human_owns_input", lambda: workspace.resume(session, lambda data: guard(data, document))))
        check("human_owner_preserved", workspace.status(session)["owner"] == "Human")
        # This simulates the human Return button; it is explicitly not manual acceptance.
        current = workspace.status(session)
        client.call("ownership", session, expectedRevision=current["revision"], owner="Automation")
        check("mismatched_document_guard_rejected", rejected("context_changed", lambda: workspace.resume(session, lambda data: guard(data, document.with_name("wrong.txt")))))
        check("guard_failure_keeps_host_and_app_alive", client.process.poll() is None and alive(original_pid))
        resumed = workspace.resume(session, lambda data: guard(data, document))
        check("same_application_after_handoff", resumed.state["applicationPid"] == original_pid)
        check("old_plan_remains_rejected_after_return", rejected("resume_required", lambda: workspace.input(observation, "text", text="MUST NOT APPEAR")))
        workspace.viewer(session, visible=False)
        workspace.input(resumed, "chord", keys=[17, 35])
        workspace.input(resumed, "text", text=" Saved after handoff.")
        workspace.input(resumed, "chord", keys=[17, 83])
        check("actual_save_after_guarded_resume", wait_for(lambda: document.read_text(encoding="utf-8-sig") == expected + " Saved after handoff."))
        workspace.input(resumed, "text", text=" UNSAVED")
        # Wait for actual dirty caption before the close decision.
        check("dirty_document_observed", wait_for(lambda: any(w["ClassName"] == "Notepad++" and w["Text"].startswith("*") for w in workspace.observe(session).data["result"]["windows"])))
        try:
            workspace.shutdown()
            blocked = False
        except ShutdownBlocked as error:
            blocked = session in error.pending
        check("graceful_shutdown_preserves_save_prompt", blocked and alive(original_pid) and client.process.poll() is None)
        prompt = workspace.observe(session, capture=True, accessibility=True)
        check("save_prompt_available_for_inspection", any(w["ClassName"] == "#32770" and w["Visible"] for w in prompt.data["result"]["windows"]))
        check("unsaved_text_not_written_to_disk", document.read_text(encoding="utf-8-sig") == expected + " Saved after handoff.")
        subprocess.run([str(host), "--prepare-installed-notepad-gdi-demo", str(output / "peer-documents")],
                       check=True, creationflags=subprocess.CREATE_NO_WINDOW, stdout=subprocess.DEVNULL)
        peer_setup = json.loads((output / "peer-documents/launch.json").read_text(encoding="utf-8-sig"))
        peer_original = Path(peer_setup["Document"]).read_text(encoding="utf-8-sig")
        peer = workspace.create(executable=peer_setup["Executable"], arguments=peer_setup["Arguments"])
        peer_state = workspace.status(peer)
        owned.update(peer_state["ownedProcessIds"])
        check("second_workspace_layer_has_independent_process", peer_state["applicationPid"] != original_pid)
        first_close = workspace.close_session(session, force=True)  # Discard only disposable UNSAVED edit.
        check("explicit_single_layer_force_leaves_peer_alive", first_close["result"]["closed"]
              and wait_for(lambda: not alive(original_pid)) and alive(peer_state["applicationPid"])
              and client.process.poll() is None)
        peer_observation = workspace.observe(peer, capture=True)
        check("peer_remains_observable_after_other_layer_closed", guard(peer_observation.data, Path(peer_setup["Document"])))
        check("peer_document_unchanged", peer_original == Path(peer_setup["Document"]).read_text(encoding="utf-8-sig"))
        workspace.shutdown()
        check("graceful_final_shutdown_releases_all_owned_processes", wait_for(lambda: all(not alive(pid) for pid in owned)))
    except Exception as error:
        failure = repr(error)
    finally:
        if workspace is not None:
            workspace.shutdown(force=True)  # Test owns only disposable fixture data.
        survivors = [pid for pid in owned if alive(pid)]
        report = dict(passed=failure is None and not survivors, checks=checks, failure=failure,
                      survivors=survivors, ownedProcessIds=sorted(owned), seconds=time.monotonic() - started,
                      humanAcceptance="pending; programmatic transitions only", output=str(output))
        (output / "workspace-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps({k: report[k] for k in ("passed", "failure", "survivors", "seconds", "output")}))
        if not report["passed"]:
            raise SystemExit(1)


if __name__ == "__main__":
    main()

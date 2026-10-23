"""Human/automation demonstration using the persistent workspace policy."""
import argparse
import json
from pathlib import Path
import runpy
import subprocess
import time
import uuid

from viavw_client import ROOT, ViavwError, verified_host
from viavw_workspace import ShutdownBlocked, ViavwWorkspace


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true")
    args = parser.parse_args()
    output = ROOT / "artifacts" / ("collaboration-demo-" + uuid.uuid4().hex)
    output.mkdir()
    host = verified_host()
    subprocess.run([str(host), "--prepare-installed-notepad-gdi-demo", str(output / "documents")],
                   check=True, creationflags=subprocess.CREATE_NO_WINDOW, stdout=subprocess.DEVNULL)
    setup = json.loads((output / "documents/launch.json").read_text(encoding="utf-8-sig"))
    document = Path(setup["Document"])
    matches = runpy.run_path(str(ROOT / "scripts/demo-agent.py"))["active_document_matches"]
    workspace = ViavwWorkspace(output / "host")
    outcome = "incomplete"
    session = None
    try:
        session = workspace.create(executable=setup["Executable"], arguments=setup["Arguments"])
        observation = None
        for attempt in range(30):
            try:
                observation = workspace.observe(session, capture=True)
                if matches(observation.data, document):
                    break
            except ViavwError as error:
                if error.code not in ("no_surface", "observation_changed"):
                    raise
            time.sleep(.1)
        if observation is None or not matches(observation.data, document):
            raise RuntimeError("The intended disposable document is not ready")
        original_pid = observation.state["applicationPid"]
        editor = next(w["Handle"] for w in observation.data["result"]["windows"]
                      if w["ClassName"] == "Scintilla" and w["Visible"])
        workspace.input(observation, "click", window=editor, x=100, y=10)
        workspace.input(observation, "chord", keys=[17, 35])
        workspace.input(observation, "key", key=13)
        workspace.input(observation, "text", text="Automation prepared this shared document.")
        workspace.input(observation, "chord", keys=[17, 83])
        workspace.request_human(observation, "Your turn: edit/save, then choose Return to automation")
        print("Your private application is ready. Select its viewer; it starts in Human control.", flush=True)
        print("Disposable document:", document, flush=True)
        if args.smoke:
            state = workspace.status(session)
            workspace.client.call("ownership", session, expectedRevision=state["revision"], owner="Automation")
        while True:
            if not args.smoke:
                input("Edit/save, choose Return to automation, then press Enter here: ")
            try:
                resumed = workspace.resume(session, lambda data: matches(data, document))
                break
            except ViavwError as error:
                if args.smoke or error.code not in ("human_owns_input", "context_changed", "observation_changed", "no_surface"):
                    raise
                print("Paused:", error, "Open the printed document and return ownership when ready.", flush=True)
        workspace.input(resumed, "chord", keys=[17, 35])
        workspace.input(resumed, "key", key=13)
        workspace.input(resumed, "text", text="Automation continued in the same running application.")
        workspace.input(resumed, "chord", keys=[17, 83])
        deadline = time.monotonic() + 5
        while True:
            try:
                saved = "Automation continued in the same running application." in document.read_text(encoding="utf-8-sig")
            except PermissionError as error:
                if getattr(error, "winerror", None) not in (32, 33):
                    raise
                saved = False
            if saved or time.monotonic() >= deadline:
                break
            time.sleep(.05)
        if not saved or workspace.status(session)["applicationPid"] != original_pid:
            raise RuntimeError("Saved content or application identity did not verify")
        outcome = "passed"
        print("Verified: automation saved in the same application after handoff.", flush=True)
        if not args.smoke:
            input("Inspect the result. Press Enter here when ready to close: ")
    except (Exception, KeyboardInterrupt) as error:
        print("Automation stopped; the workspace is still owned by this process:", error, flush=True)
        if args.smoke:
            workspace.shutdown(force=True)  # Only this smoke test's disposable session.
            raise
    # Routine failure has no implicit force-close. Keep this process alive while
    # the human resolves native prompts. Closing the terminal still ends the host.
    while True:
        try:
            workspace.shutdown()
            break
        except (ShutdownBlocked, ViavwError) as error:
            if args.smoke:
                workspace.shutdown(force=True)
                raise
            print("Application remains open:", error, flush=True)
            answer = input("Resolve/save/close in the viewer, then Enter; type FORCE to discard this disposable session: ")
            if answer == "FORCE":
                workspace.shutdown(force=True)
                outcome = "forced"
                break
    report = {"outcome": outcome, "session": session, "document": str(document),
              "humanAcceptance": "programmatic only" if args.smoke else "user-operated; inspect result", "output": str(output)}
    (output / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Evidence:", output, flush=True)
    if outcome != "passed":
        raise SystemExit(1)


if __name__ == "__main__":
    main()

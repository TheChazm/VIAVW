"""Disposable Notepad++ automation / human handoff demonstration."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid
from viavw_client import ROOT, ViavwClient, ViavwError, verified_host


def active_document_matches(observation, document):
    """Conservative Notepad++ demo precondition, not a generic app-readiness test."""
    root = observation["session"]["root"]
    window = next((w for w in observation["result"]["windows"] if w["Handle"] == root), None)
    if not window or window["ClassName"] != "Notepad++":
        return False
    title = window["Text"].removeprefix("*")
    suffix = " - Notepad++"
    if not title.endswith(suffix):
        return False
    return Path(title[:-len(suffix)]).resolve() == document.resolve()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true", help="Programmatic handoff only; no physical human acceptance claim")
    args = parser.parse_args()
    host = verified_host()
    output = ROOT / "artifacts" / ("agent-demo-" + uuid.uuid4().hex)
    output.mkdir()
    subprocess.run([str(host), "--prepare-installed-notepad-gdi-demo", str(output / "documents")],
                   check=True, creationflags=subprocess.CREATE_NO_WINDOW)
    setup = json.loads((output / "documents/launch.json").read_text(encoding="utf-8-sig"))
    document = Path(setup["Document"])
    with ViavwClient(output / "host", host) as client:
        hello = client.call("hello")
        created = client.call("create", executable=setup["Executable"], arguments=setup["Arguments"])
        session = created["id"]
        initial = client.call("observe", session, capture=True)
        state = initial["session"]
        original_pid = state["applicationPid"]

        def send(kind, **fields):
            nonlocal state
            state = client.input(session, state, kind, **fields)["session"]

        def saved(fragment):
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                try:
                    if fragment in document.read_text(encoding="utf-8-sig"):
                        return
                except PermissionError as error:
                    if getattr(error, "winerror", None) not in (32, 33):
                        raise
                time.sleep(.05)
            raise RuntimeError("Actual saved file did not contain the expected text")

        editor = next(w["Handle"] for w in initial["result"]["windows"] if w["ClassName"] == "Scintilla" and w["Visible"])
        send("click", window=editor, x=100, y=10)
        send("chord", keys=[17, 65])
        send("text", text="CharlieAI-facing interface demonstration")
        send("chord", keys=[17, 83])
        saved("CharlieAI-facing interface demonstration")
        client.call("attention", session, reason="Please inspect or edit this disposable document")
        state = client.call("ownership", session, expectedRevision=state["revision"], owner="Human")["session"]
        try:
            client.input(session, state, "text", text="THIS MUST BE REJECTED")
            raise RuntimeError("Human ownership failed to reject automation")
        except ViavwError as error:
            if error.code != "human_owns_input":
                raise

        if args.smoke:
            state = client.call("ownership", session, expectedRevision=state["revision"], owner="Automation")["session"]
        else:
            client.call("viewer", session, visible=True)
            print("All documents/settings in this demonstration are disposable.")
            print("The viewer opens without taking focus. Select it when ready.")
            print("Human control owns the SAME running Notepad++. Edit and save, then choose Return to automation.")
            print("Document:", document)
            while True:
                input("After returning ownership in the viewer, press Enter here: ")
                state = client.call("status", session)["session"]
                if state.get("state") == "closed":
                    print("You closed the session; automation will not reopen it.")
                    return
                if state["owner"] == "Automation":
                    break
                print("Human control still owns the app. Use Return to automation in the viewer.")

        if state["applicationPid"] != original_pid:
            raise RuntimeError("Application restarted during handoff")
        resumed_observation = client.call("observe", session, capture=True)
        state = resumed_observation["session"]
        resumed = active_document_matches(resumed_observation, document)
        if resumed:
            try:
                send("chord", keys=[17, 35])
                send("key", key=13)
                send("text", text="Automation resumed without restarting the application.")
                send("chord", keys=[17, 83])
                saved("Automation resumed without restarting the application.")
            except ViavwError as error:
                if error.code not in ("stale_revision", "human_owns_input", "invalid_target"):
                    raise
                resumed = False
                print("Control/context changed again; no input will be retried:", error)
        if not resumed:
            if args.smoke:
                raise RuntimeError("Smoke demonstration could not establish its intended document context")
            print("Automation paused: the active document or ownership changed. It will not type into an unexpected document.")
        observation = client.call("observe", session, capture=True, accessibility=True)
        state = observation["session"]
        print("Same application resumed and saved." if resumed else "Session preserved for inspection.")
        print("Screenshot:", observation["result"]["image"]["path"])
        if not args.smoke:
            input("Inspect the result, then press Enter to close this disposable session: ")
        closed = client.call("close", session, expectedRevision=state["revision"])
        while not closed["result"]["closed"]:
            if args.smoke:
                raise RuntimeError("Graceful close incomplete; smoke cleanup discards only disposable session data")
            client.call("viewer", session, visible=True)
            decision = input("Application retained a prompt/helpers. Resolve it, or type FORCE to discard this disposable session: ")
            current = client.call("status", session)["session"]
            if current["state"] == "closed":
                break
            closed = client.call("close", session, expectedRevision=current["revision"], force=decision == "FORCE")
        result = {"passed": True, "protocol": hello["protocol"], "applicationPid": original_pid,
                  "resumed": resumed,
                  "document": str(document), "screenshot": observation["result"]["image"]["path"],
                  "handoff": "programmatic only" if args.smoke else "user-operated; inspect independently",
                  "output": str(output)}
        (output / "demo-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        print("Evidence:", output)


if __name__ == "__main__":
    main()

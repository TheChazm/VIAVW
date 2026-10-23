"""Human inspector for the installed application configurations actually tested."""
import argparse
import hashlib
import json
from pathlib import Path
import runpy
import time
import uuid

from viavw_client import ROOT, ViavwClient, ViavwError
from viavw_workspace import ViavwWorkspace, ShutdownBlocked


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", choices=["word", "vlc", "7zip"], required=True)
    parser.add_argument("--smoke", action="store_true")
    args = parser.parse_args()
    manifest = json.loads((ROOT / "artifacts/installed-apps-current.json").read_text(encoding="utf-8"))
    host = Path(manifest["executable"]).resolve()
    if (not host.is_relative_to(ROOT / "src/LayerHost/bin") or host.name != "LayerHost.exe"
            or hashlib.sha256(host.with_suffix(".dll").read_bytes()).hexdigest().upper() != manifest["assemblySha256"]):
        raise ValueError("Preserved research host changed")
    configuration = manifest["applications"][args.app]
    executable = Path(configuration["executable"])
    if hashlib.sha256(executable.read_bytes()).hexdigest().upper() != configuration["executableSha256"]:
        raise ValueError("Installed application changed; repeat compatibility verification before using this preset")
    output = ROOT / "artifacts" / ("installed-demo-" + args.app + "-" + uuid.uuid4().hex)
    output.mkdir()
    prepare = runpy.run_path(str(ROOT / "scripts/prepare-installed-app-fixtures.py"))["prepare"]
    fixtures = output / "fixtures"
    prepare(fixtures, media=args.app == "vlc")
    if args.app == "word":
        arguments, capture = ["/a", str(fixtures / "word-first.rtf")], "print-window"
        expected_class, expected_title = "OpusApp", "word-first.rtf"
    elif args.app == "7zip":
        arguments, capture = [str(fixtures / "sample.zip")], "window-dc"
        expected_class, expected_title = "7-Zip::FM", "sample.zip"
    else:
        arguments = ["--no-one-instance", "--no-audio", "--no-video-title-show", "--no-qt-privacy-ask",
                     "--no-qt-updates-notif", "--config=" + str(output / "vlcrc"), "--vout=wingdi", "--loop", str(fixtures / "color-motion.avi")]
        capture, expected_class, expected_title = "window-dc", "Qt5QWindowIcon", "color-motion.avi"
    workspace = ViavwWorkspace(output / "host", client=ViavwClient(output / "host", host))
    failure = None
    graceful_close, forced_cleanup, pending_close = True, False, {}
    session = None
    try:
        session = workspace.create(executable=str(executable), arguments=arguments, captureMode=capture)
        deadline = time.monotonic() + 60
        while True:
            try:
                observed = workspace.observe(session)
                ready = any(w["Handle"] == observed.state["root"] and w["ClassName"] == expected_class
                            and expected_title in w["Text"] for w in observed.data["result"]["windows"])
                if ready:
                    break
            except ViavwError as error:
                if error.code not in ("no_surface", "observation_changed"):
                    raise
            if time.monotonic() >= deadline:
                raise TimeoutError("The intended installed-application window was not ready")
            time.sleep(.1)
        observed = workspace.observe(session, capture=True)
        if args.app == "word":
            # A document title can precede Office's loaded editor. Check actual
            # application content, rather than treating a first window as ready.
            document_deadline = time.monotonic() + 30
            while True:
                observed = workspace.observe(session, capture=True, accessibility=True)
                tree = json.loads(Path(observed.data["result"]["accessibilityPath"]).read_text(encoding="utf-8-sig"))
                if any(n["type"] == "ControlType.Document" and "Original disposable Word document." in (n.get("text") or "") for n in tree["nodes"]):
                    break
                if time.monotonic() >= document_deadline:
                    raise TimeoutError("Word's actual disposable document content was not ready")
                time.sleep(.1)
        shown = workspace.request_human(observed, "Disposable installed-application inspection")
        if shown["owner"] != "Human" or not shown["viewerVisible"]:
            raise AssertionError("Inspector did not enter visible Human control")
        print("Human control is ready. Select the viewer; all source files are disposable.", flush=True)
        print("Files:", fixtures, flush=True)
        if args.app == "7zip":
            print("Known limitation: synchronous clicks can time out and native popup menus may be missing from capture.", flush=True)
        if args.smoke:
            try:
                workspace.input(observed, "text", text="MUST NOT BE WRITTEN")
                raise AssertionError("Automation was not rejected")
            except ViavwError as error:
                if error.code != "resume_required":
                    raise
        else:
            input("Keep this terminal open. Inspect/edit/save in the viewer, then press Enter here to close: ")
    except (Exception, KeyboardInterrupt) as error:
        failure = repr(error)
        print("Demonstration paused:", error, flush=True)
        if args.smoke:
            workspace.shutdown(force=True)
    if not args.smoke or failure is None:
        while True:
            try:
                workspace.shutdown()
                break
            except (ShutdownBlocked, ViavwError) as error:
                if args.smoke:
                    if isinstance(error, ShutdownBlocked):
                        pending_close = {sid: data["result"]["reason"] for sid,data in error.pending.items()}
                        # A normal application can still be handling close after
                        # the host's bounded two-second attempt. Repeat reads,
                        # then make one further explicit graceful close attempt.
                        deadline = time.monotonic() + 3
                        while time.monotonic() < deadline:
                            states = [workspace.client.call("status", sid)["session"] for sid in error.pending]
                            if all(s["state"] in ("closed", "application_root_exited") for s in states):
                                break
                            time.sleep(.1)
                        try:
                            workspace.shutdown()
                            break
                        except ShutdownBlocked as remaining:
                            graceful_close = False
                            pending_close = {sid: data["result"]["reason"] for sid,data in remaining.pending.items()}
                            if not all(reason == "owned_processes_remaining" for reason in pending_close.values()):
                                failure = repr(remaining)
                        except ViavwError as remaining:
                            failure = repr(remaining)
                    else:
                        failure = repr(error)
                    forced_cleanup = True
                    workspace.shutdown(force=True)  # This test owns only disposable fixtures.
                    break
                print("Resolve/save/close any native prompt in the viewer:", error, flush=True)
                answer = input("Enter to retry; type FORCE to discard this disposable session: ")
                if answer == "FORCE":
                    workspace.shutdown(force=True)
                    break
    report = dict(passed=failure is None, failure=failure, application=args.app, session=session,
                  gracefulClose=graceful_close, forcedDisposableCleanup=forced_cleanup, pendingClose=pending_close,
                  humanAcceptance="pending; programmatic only" if args.smoke else "user-operated; inspect result", output=str(output))
    (output / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Evidence:", output, flush=True)
    if failure:
        raise SystemExit(1)


if __name__ == "__main__":
    main()

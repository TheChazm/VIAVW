"""Real installed 7-Zip workflow through the same console used directly by Codex."""
import json
from pathlib import Path
import queue
import runpy
import subprocess
import sys
import threading
import time
import uuid

from viavw_client import ROOT


def main():
    output = ROOT / "artifacts" / ("console-verification-" + uuid.uuid4().hex)
    output.mkdir()
    manifest = json.loads((ROOT / "artifacts/installed-apps-current.json").read_text(encoding="utf-8"))
    prepare = runpy.run_path(str(ROOT / "scripts/prepare-installed-app-fixtures.py"))["prepare"]
    prepare(output / "fixtures", media=False)
    process = subprocess.Popen([sys.executable, "-u", str(ROOT / "scripts/viavw-console.py"),
               str(output / "host"), "--host", manifest["executable"]], stdin=subprocess.PIPE,
               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8",
               creationflags=subprocess.CREATE_NO_WINDOW)
    replies, diagnostics = queue.Queue(), []
    def reader():
        try:
            for line in process.stdout:
                replies.put(json.loads(line))
        except Exception as error:
            replies.put(error)
    def errors():
        for line in process.stderr:
            diagnostics.append(line)
            del diagnostics[:-64]
    threads = [threading.Thread(target=reader, daemon=True), threading.Thread(target=errors, daemon=True)]
    for thread in threads:
        thread.start()
    checks, owned, sequence, transcript = {}, set(), 0, []
    failure = None
    def check(name, passed):
        checks[name] = bool(passed)
        if not passed:
            raise AssertionError(name)
        print("PASS", name, flush=True)
    def exchange(command):
        nonlocal sequence
        if "id" not in command:
            sequence += 1
            command = {"id": "test-" + str(sequence), **command}
        process.stdin.write(json.dumps(command, separators=(",", ":")) + "\n")
        process.stdin.flush()
        result = replies.get(timeout=90)
        if isinstance(result, Exception):
            raise result
        transcript.append(result)
        state = result.get("data", {}).get("session", {})
        owned.update(state.get("ownedProcessIds", []))
        return result
    def call(op, **fields):
        result = exchange(dict(op=op, **fields))
        if not result["ok"]:
            raise RuntimeError(result["error"])
        return result["data"]
    def observe(session, title=None, capture=False):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            result = exchange(dict(op="observe", session=session, capture=capture))
            if result["ok"]:
                data = result["data"]
                if title is None or any(w["Handle"] == data["session"]["root"] and w["Text"] == title
                                        for w in data["result"]["windows"]):
                    return data
            elif result["error"]["code"] not in ("no_surface", "observation_changed"):
                raise RuntimeError(result["error"])
            time.sleep(.05)
        raise TimeoutError("Application surface did not reach expected state")
    try:
        check("console startup", replies.get(timeout=20).get("ready") is True)
        created = call("create", launch={"executable": manifest["applications"]["7zip"]["executable"],
                        "arguments": [str(output / "fixtures/sample.zip")]})
        session, original_pid = created["id"], created["session"]["applicationPid"]
        first = observe(session, capture=True)
        editor = next(w for w in first["result"]["windows"] if w["ClassName"] == "SysListView32")
        check("installed 7-Zip owned window", first["session"]["state"] == "ready")
        call("input", observationId=first["observationId"], kind="click",
             fields=dict(window=editor["Handle"], x=70, y=54, deliveryMode="posted"))
        call("input", observationId=first["observationId"], kind="chord", fields=dict(keys=[17, 65]))
        call("input", observationId=first["observationId"], kind="key", fields=dict(key=116))
        dialog = observe(session, title="Copy", capture=True)
        check("real extraction dialog", dialog["session"]["root"] != first["session"]["root"])
        call("input", observationId=dialog["observationId"], kind="chord", fields=dict(keys=[17, 65]))
        call("input", observationId=dialog["observationId"], kind="text", fields=dict(text=str(output / "extracted") + "\\"))
        confirm = dict(id="confirm-once", op="input", observationId=dialog["observationId"], kind="key", fields=dict(key=13))
        accepted = exchange(confirm)
        check("extraction confirmation forwarded", accepted["ok"])
        deadline = time.monotonic() + 10
        expected = {"greeting.txt": "VIAVW archive extraction verified.\n", "nested/details.txt": "Second disposable archive member.\n"}
        while time.monotonic() < deadline:
            if all((output / "extracted" / name).is_file() for name in expected):
                break
            time.sleep(.05)
        check("actual extracted file contents", all((output / "extracted" / name).read_text(encoding="utf-8") == text for name,text in expected.items()))
        repeated = exchange(confirm)
        check("exact console duplicate returns historical response", repeated == accepted)
        conflict = exchange({**confirm, "fields": dict(key=32)})
        check("conflicting console ID rejected", not conflict["ok"] and conflict["error"]["code"] == "id_conflict")
        state = call("status", session=session)["session"]
        check("duplicate caused no new input", state["inputSequence"] == accepted["data"]["session"]["inputSequence"])
        current = observe(session, capture=True)
        call("surface", observationId=current["observationId"], window=current["session"]["root"])
        stale = exchange(dict(op="input", observationId=current["observationId"], kind="key", fields=dict(key=13)))
        check("surface selection invalidates old console plans", not stale["ok"] and stale["error"]["code"] == "observation_expired")
        current = observe(session)
        human = call("human", observationId=current["observationId"], reason="Automated console ownership check")
        check("visible inspector and same app in Human control", human["session"]["owner"] == "Human" and human["session"]["viewerVisible"] and human["session"]["applicationPid"] == original_pid)
        denied = exchange(dict(op="input", observationId=current["observationId"], kind="text", fields=dict(text="MUST NOT APPEAR")))
        check("console cannot type during Human turn", not denied["ok"] and denied["error"]["code"] == "resume_required")
        reclaimed = exchange(dict(op="resume", session=session, context=dict(rootTitle=str(output / "fixtures/sample.zip") + "\\", rootClass="7-Zip::FM"), capture=False))
        check("resume never reclaims Human ownership", not reclaimed["ok"] and reclaimed["error"]["code"] == "human_owns_input")
        call("shutdown")
        check("console exits after graceful shutdown", process.wait(timeout=20) == 0)
    except Exception as error:
        failure = repr(error)
    finally:
        if process.poll() is None:
            # Explicit cleanup of this test's disposable owned workspace only.
            try:
                exchange(dict(op="shutdown", force=True))
                process.wait(timeout=25)
            except Exception:
                process.kill()
                process.wait(timeout=10)
        for thread in threads:
            thread.join(timeout=2)
        alive = runpy.run_path(str(ROOT / "scripts/test-workspace.py"))["alive"]
        deadline = time.monotonic() + 10
        while any(alive(pid) for pid in owned) and time.monotonic() < deadline:
            time.sleep(.05)
        survivors = [pid for pid in owned if alive(pid)]
        report = dict(passed=failure is None and not survivors, checks=checks, failure=failure,
                      ownedProcessIds=sorted(owned), survivors=survivors, stderr="".join(diagnostics),
                      humanAcceptance="pending; programmatic only", output=str(output))
        (output / "console-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        (output / "transcript.json").write_text(json.dumps(transcript, indent=2), encoding="utf-8")
        print(json.dumps({k:report[k] for k in ("passed", "failure", "survivors", "output")}))
        if not report["passed"]:
            raise SystemExit(1)


if __name__ == "__main__":
    main()

"""Codex direct-use console: JSON lines over one persistent terminal session.

All application input uses VIAVW. No physical desktop input or network listener.
EOF ends owned apps; keep the console alive across inspection/human turns.
"""
import argparse
from collections import OrderedDict
import hashlib
import json
from pathlib import Path
import sys
import time
import uuid

from viavw_client import ROOT, ViavwClient, ViavwError
from viavw_workspace import ShutdownBlocked, ViavwWorkspace


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--host", type=Path, help="Explicit preserved experimental host; leaves selected manifests unchanged")
    parser.add_argument("--compact", action="store_true", help="Write full responses to local evidence; print concise observation metadata")
    args = parser.parse_args()
    if args.host:
        args.host = args.host.resolve()
        if not args.host.is_relative_to(ROOT / "src/LayerHost/bin") or args.host.name != "LayerHost.exe":
            parser.error("Experimental host must be a preserved VIAVW build")
    workspace = ViavwWorkspace(args.output, client=ViavwClient(args.output, args.host))
    response_dir = args.output.resolve() / "console-responses"
    response_dir.mkdir(exist_ok=False)
    sequence = 0
    observations = OrderedDict()
    seen, replies = {}, OrderedDict()
    print(json.dumps({"ready": True, "output": str(args.output.resolve()),
                      "lifetime": "Keep this console alive; EOF closes its owned applications"}), flush=True)
    try:
        while True:
            line = sys.stdin.readline(65538)
            if not line:
                break
            command_id = None
            command_started = time.monotonic()
            stop = False
            remember = False
            try:
                if len(line) > 65536 or not line.endswith("\n"):
                    raise ViavwError("invalid_line", "Use a complete JSON line of at most 65536 characters")
                command = json.loads(line)
                command_id = command["id"]
                if not isinstance(command_id, str) or not 1 <= len(command_id) <= 128:
                    raise ValueError("Use a unique command ID of 1–128 characters")
                digest = hashlib.sha256(line.rstrip("\r\n").encode("utf-8")).hexdigest()
                if command_id in seen:
                    if seen[command_id] != digest:
                        raise ViavwError("id_conflict", "Console ID already names another command")
                    if command_id not in replies:
                        raise ViavwError("result_expired", "Console response expired; input will not execute again", "unknown")
                    print(replies[command_id], flush=True)
                    continue
                if len(seen) >= 4000 and command.get("op") != "shutdown":
                    raise ViavwError("console_budget", "End this workspace before starting another console")
                seen[command_id] = digest
                remember = True
                op = command["op"]
                def observed():
                    key = command["observationId"]
                    if key not in observations:
                        raise ViavwError("observation_expired", "Inspect again; this console retains 128 observations")
                    return observations[key]
                def export(observation):
                    key = uuid.uuid4().hex
                    observations[key] = observation
                    while len(observations) > 128:
                        observations.popitem(last=False)
                    return {"observationId": key, **observation.data}
                if op == "hello":
                    data = workspace.client.call("hello")
                elif op == "create":
                    session = workspace.create(**command["launch"])
                    data = {"id": session, "session": workspace.status(session)}
                elif op == "list":
                    data = workspace.client.call("list")
                elif op == "status":
                    data = {"session": workspace.status(command["session"])}
                elif op in ("inspect-begin", "inspect-poll"):
                    fields = {key:command[key] for key in ("ticket", "automationId") if key in command}
                    data = workspace.client.call(op, command["session"], **fields)
                elif op == "observe":
                    data = export(workspace.observe(command["session"], capture=command.get("capture", False),
                                                    accessibility=command.get("accessibility", False)))
                elif op == "input":
                    data = workspace.input(observed(), command["kind"], **command.get("fields", {}))
                elif op == "surface":
                    observation = observed()
                    data = workspace.client.call("surface", observation.session,
                          expectedRevision=observation.state["revision"], window=command["window"])
                    # A different document invalidates the console's old plans.
                    for key in [k for k,v in observations.items() if v.session == observation.session]:
                        del observations[key]
                elif op == "human":
                    data = {"session": workspace.request_human(observed(), command["reason"])}
                elif op == "resume":
                    # Operator must inspect actual state and supply its expected
                    # context; matching a caption alone is not semantic completion.
                    context = command["context"]
                    title, class_name = context["rootTitle"], context["rootClass"]
                    if not title or not class_name:
                        raise ValueError("Supply exact nonempty rootTitle/rootClass after inspection")
                    def matches(data):
                        root = data["session"]["root"]
                        return any(w["Handle"] == root and w["Text"] == title and w["ClassName"] == class_name
                                   for w in data["result"]["windows"])
                    data = export(workspace.resume(command["session"], matches,
                                  capture=command.get("capture", True), accessibility=command.get("accessibility", False)))
                elif op == "viewer":
                    data = workspace.viewer(command["session"], visible=command["visible"])
                elif op == "close":
                    data = workspace.close_session(command["session"], force=command.get("force", False))
                elif op == "shutdown":
                    workspace.shutdown(force=command.get("force", False))
                    data, stop = {"closed": True}, True
                else:
                    raise ViavwError("unsupported_operation", "Unknown console operation")
                response = {"id": command_id, "ok": True, "data": data}
            except ShutdownBlocked as error:
                response = {"id": command_id, "ok": False, "error": {"code": "shutdown_blocked", "message": str(error), "outcome": "not_started"}, "pending": error.pending}
            except Exception as error:
                response = {"id": command_id, "ok": False, "error": {"code": getattr(error, "code", "invalid_command"),
                            "message": str(error), "outcome": getattr(error, "outcome", "not_started")}}
            encoded = json.dumps(response, ensure_ascii=True, separators=(",", ":"))
            sequence += 1
            response_path = response_dir / (str(sequence).zfill(4) + ".json")
            response_path.write_text(encoded, encoding="utf-8")
            with (args.output.resolve() / "console-timings.jsonl").open("a", encoding="utf-8") as log:
                log.write(json.dumps({"id": command_id, "milliseconds": (time.monotonic()-command_started)*1000,
                                     "response": str(response_path)}) + "\n")
            if remember:
                replies[command_id] = encoded
                while len(replies) > 32:
                    replies.popitem(last=False)
            if args.compact and response.get("ok") and isinstance(response.get("data"), dict) and "observationId" in response["data"]:
                data = response["data"]
                print(json.dumps({"id":command_id,"ok":True,"data":{"observationId":data["observationId"],
                    "session":data["session"],"image":data["result"].get("image"),
                    "accessibilityPath":data["result"].get("accessibilityPath"),"fullResponse":str(response_path)}}), flush=True)
            else:
                print(encoded, flush=True)
            if stop:
                break
    finally:
        workspace.shutdown(force=True)  # Documented EOF cleanup of owned apps.


if __name__ == "__main__":
    main()

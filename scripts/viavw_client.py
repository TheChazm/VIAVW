"""Reference client for VIAVW protocol 1. Standard library only; no CharlieAI dependency."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import threading
import uuid

ROOT = Path(__file__).resolve().parents[1]


class ViavwError(RuntimeError):
    def __init__(self, code, message, outcome="not_started"):
        super().__init__(f"{code}: {message}")
        self.code, self.outcome = code, outcome


def verified_host() -> Path:
    manifest = json.loads((ROOT / "artifacts/agent-api-current.json").read_text(encoding="utf-8-sig"))
    executable = Path(manifest["executable"]).resolve()
    if not executable.is_relative_to(ROOT / "src/LayerHost/bin") or executable.name != "LayerHost.exe":
        raise ValueError("Host must be a preserved VIAVW build")
    actual = hashlib.sha256(executable.with_suffix(".dll").read_bytes()).hexdigest().upper()
    if actual != manifest["assemblySha256"]:
        raise ValueError("Preserved host assembly changed")
    return executable


class ViavwClient:
    """Owns one host. Closing it ends its owned apps, including unsaved sessions.

    Calls are serialized. No operation is retried automatically. A timed-out
    stream is unusable because its next response could belong to the old call.
    """
    def __init__(self, output: Path, executable: Path | None = None):
        self.output = output.resolve()
        if not self.output.is_relative_to(ROOT / "artifacts"):
            raise ValueError("Keep session artifacts inside VIAVW artifacts")
        self.executable = executable or verified_host()
        self.process = subprocess.Popen(
            [str(self.executable), "--agent-stdio", str(self.output)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", bufsize=1,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        self._responses = queue.Queue(maxsize=4)
        self._lock = threading.Lock()
        self._broken = False
        self._last = None
        self._diagnostics = ""
        self._requests_sent = 0


        def read_responses():
            try:
                while True:
                    line = self.process.stdout.readline(1024 * 1024 + 1)
                    if not line:
                        raise EOFError("VIAVW host disconnected")
                    if len(line) > 1024 * 1024 or not line.endswith("\n"):
                        raise ValueError("Invalid or oversized VIAVW response")
                    self._responses.put(json.loads(line))
            except Exception as error:
                self._responses.put(error)

        def read_diagnostics():
            while True:
                chunk = self.process.stderr.read(4096)
                if not chunk:
                    return
                self._diagnostics = (self._diagnostics + chunk)[-65536:]

        self._threads = [threading.Thread(target=read_responses, daemon=True),
                         threading.Thread(target=read_diagnostics, daemon=True)]
        for thread in self._threads:
            thread.start()

    def _exchange(self, request, timeout):
        if self._broken:
            raise ViavwError("stream_unusable", "Close this host; an earlier request has an uncertain outcome", "unknown")
        try:
            self.process.stdin.write(request + "\n")
            self.process.stdin.flush()
            reply = self._responses.get(timeout=timeout)
            if isinstance(reply, Exception):
                raise reply
            sent = json.loads(request)
            if not isinstance(reply, dict) or reply.get("protocol") != 1 or reply.get("id") != sent["id"]:
                raise ValueError("VIAVW response does not match its request")
            if type(reply.get("ok")) is not bool:
                raise ValueError("VIAVW response has no Boolean success flag")
            if reply["ok"] and "data" not in reply:
                raise ValueError("VIAVW success response has no data")
            if not reply["ok"]:
                error = reply.get("error")
                if (not isinstance(error, dict) or not isinstance(error.get("code"), str)
                        or not isinstance(error.get("message"), str)
                        or error.get("outcome") not in ("not_started", "unknown")):
                    raise ValueError("Malformed VIAVW error response")
        except Exception as error:
            self._broken = True
            raise ViavwError("transport_failed", str(error), "unknown") from error
        if not reply["ok"]:
            error = reply["error"]
            raise ViavwError(error["code"], error["message"], error["outcome"])
        return reply["data"]

    def call(self, op, session=None, *, timeout=90, **fields):
        with self._lock:
            request = {"protocol": 1, "id": uuid.uuid4().hex, "op": op}
            if session is not None:
                request["session"] = session
            if {"protocol", "id", "op", "session"}.intersection(fields):
                raise ValueError("Reserved request field")
            request.update(fields)
            self._last = None
            serialized = json.dumps(request, separators=(",", ":"), ensure_ascii=True)
            if len(serialized) > 65536:
                raise ValueError("Request exceeds the host's 65536-character limit")
            if self._requests_sent >= 4096:
                raise ViavwError("request_budget", "This host's unique request budget is exhausted")
            self._requests_sent += 1  # Conservative even if transport delivery is uncertain.
            self._last = serialized
            return self._exchange(self._last, timeout)

    @property
    def requests_remaining(self):
        """Conservative unique-request allowance; exact response repeats don't consume it."""
        return max(0, 4096 - self._requests_sent)

    def repeat_last_response(self, timeout=90):
        """Explicit exact-ID retransmission. It never creates a new input command."""
        with self._lock:
            if self._last is None:
                raise ValueError("No preceding request")
            return self._exchange(self._last, timeout)

    def input(self, session, observed_state, kind, *, window=None, **fields):
        # Never silently refresh a revision: that would permit stale work after
        # a Human -> Automation ownership round trip.
        return self.call("input", session, expectedRevision=observed_state["revision"],
                         window=observed_state["root"] if window is None else window, kind=kind, **fields)

    def close(self):
        try:
            if self.process.poll() is None:
                try:
                    self.process.stdin.close()
                except (OSError, ValueError):
                    pass
                try:
                    self.process.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    self.process.kill()  # Owned host; its kill-on-close jobs release its apps.
                    self.process.wait(timeout=10)
        finally:
            self._broken = True
            for thread in self._threads:
                thread.join(timeout=2)
            for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
                try:
                    stream.close()
                except (OSError, ValueError):
                    pass

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

"""Long-lived collaboration owner for a CharlieAI adapter. Standard library only.

Keep one instance alive in the adapter process across tool calls and human turns.
There is deliberately no context-manager exit that kills an unsaved application.
"""
from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
from pathlib import Path
import threading
from typing import Callable

from viavw_client import ViavwClient, ViavwError


class ShutdownBlocked(RuntimeError):
    def __init__(self, pending):
        super().__init__("Applications remain open; keep this workspace alive and resolve their prompts")
        self.pending = pending


@dataclass(frozen=True)
class Observation:
    session: str
    generation: int
    data: dict

    @property
    def state(self):
        return deepcopy(self.data["session"])


class ViavwWorkspace:
    """Serialized adapter-facing ownership, resume and graceful-shutdown policy.

    Context guards are app/workflow-specific predicates on a fresh observation.
    This class cannot establish semantic readiness for an arbitrary application.
    A successful input remains forwarded_unverified, never a completion claim.
    """
    def __init__(self, output: Path, *, client=None):
        self.client = client if client is not None else ViavwClient(output)
        self._lock = threading.RLock()
        self._sessions = {}
        self._closed = False
        hello = self.client.call("hello")
        self.capabilities = deepcopy(hello)
        required = {"create", "status", "observe", "input", "attention", "ownership", "viewer", "close"}
        if hello.get("protocol") != 1 or not required.issubset(hello.get("operations", [])):
            self.client.close()  # No applications have been launched by this workspace.
            raise ViavwError("incompatible_host", "The host lacks required collaboration operations")

    def _entry(self, session):
        if self._closed:
            raise ViavwError("workspace_closed", "This workspace is closed")
        if session not in self._sessions:
            raise ViavwError("unknown_session", "Session does not belong to this workspace")
        return self._sessions[session]

    def _capacity(self):
        # Reserve enough protocol requests to inspect/show four native save
        # prompts and retry graceful close after the human resolves them.
        # Never restart a host with running applications to reset this limit.
        if getattr(self.client, "requests_remaining", 4096) <= 64:
            raise ViavwError("workspace_capacity", "Request allowance is low; resolve work and call shutdown(). Applications remain open")

    def create(self, **launch):
        with self._lock:
            if self._closed:
                raise ViavwError("workspace_closed", "This workspace is closed")
            self._capacity()
            if "captureMode" in launch and launch["captureMode"] not in self.capabilities.get("captureModes", ["window-dc"]):
                raise ViavwError("unsupported_capability", "Selected host does not advertise this capture mode; choose the explicit research host")
            response = self.client.call("create", **launch)
            session = response["id"]
            self._sessions[session] = {"generation": 0, "paused": False,
                                       "pid": response["observation"]["session"]["applicationPid"]}
            return session

    def status(self, session):
        with self._lock:
            self._entry(session)
            self._capacity()
            return self.client.call("status", session)["session"]

    def observe(self, session, *, capture=False, accessibility=False):
        with self._lock:
            entry = self._entry(session)
            self._capacity()
            response = self.client.call("observe", session, capture=capture, accessibility=accessibility)
            if response["session"]["owner"] != "Automation":
                entry["paused"] = True
            return Observation(session, entry["generation"], deepcopy(response))

    def _snapshot(self, observation):
        entry = self._entry(observation.session)
        if entry["paused"] or observation.generation != entry["generation"]:
            raise ViavwError("resume_required", "Re-observe and validate context with resume() before input")
        if observation.state["applicationPid"] != entry["pid"]:
            raise ViavwError("application_changed", "The observed application identity changed")
        return entry

    def input(self, observation, kind, *, window=None, **fields):
        with self._lock:
            entry = self._snapshot(observation)
            self._capacity()
            if kind == "click" and fields.get("deliveryMode", "synchronous") not in self.capabilities.get("clickDeliveryModes", ["synchronous"]):
                raise ViavwError("unsupported_capability", "Selected host does not advertise this click delivery mode")
            if fields.get("validationMode", "full-window-scan") not in self.capabilities.get("inputValidationModes", ["full-window-scan"]):
                raise ViavwError("unsupported_capability", "Selected host does not advertise this input validation mode")
            try:
                response = self.client.input(observation.session, observation.state, kind,
                                             window=window, **fields)
            except ViavwError as error:
                # No retries, including when delivery has an unknown outcome.
                # A later decision requires a fresh context check.
                entry["paused"] = True
                entry["generation"] += 1
                raise
            return response

    def request_human(self, observation, reason):
        with self._lock:
            entry = self._snapshot(observation)
            self._capacity()
            # Invalidate old plans even if a later notification/viewer call fails.
            entry["paused"] = True
            entry["generation"] += 1
            self.client.call("attention", observation.session, reason=reason)
            response = self.client.call("ownership", observation.session,
                                        expectedRevision=observation.state["revision"], owner="Human")
            shown = self.client.call("viewer", observation.session, visible=True)
            return shown["session"]

    def resume(self, session, context_matches: Callable[[dict], bool], *, capture=True, accessibility=False):
        """Resume only AFTER the human returns control in the viewer.

        Never reclaims ownership. A rejected or failing guard leaves input paused.
        Supply a guard for the intended document/dialog; don't use `lambda _: True`.
        """
        with self._lock:
            entry = self._entry(session)
            entry["paused"] = True
            entry["generation"] += 1
            observation = self.observe(session, capture=capture, accessibility=accessibility)
            state = observation.state
            if state["owner"] != "Automation":
                raise ViavwError("human_owns_input", "Use Return to automation in the viewer first")
            if state["applicationPid"] != entry["pid"]:
                raise ViavwError("application_changed", "Application identity changed during handoff")
            if not context_matches(deepcopy(observation.data)):
                raise ViavwError("context_changed", "Intended application context was not established; input remains paused")
            # The host checks this revision again on every input, closing the
            # race if the human changes ownership while the guard is inspecting.
            entry["paused"] = False
            return observation

    def viewer(self, session, visible=True):
        with self._lock:
            self._entry(session)
            self._capacity()
            return self.client.call("viewer", session, visible=visible)

    def close_session(self, session, *, force=False):
        """Close one layer without ending the host or its other applications."""
        with self._lock:
            entry = self._entry(session)
            entry["paused"] = True
            entry["generation"] += 1
            state = self.client.call("status", session)["session"]
            if state["state"] == "closed":
                return {"session": state, "result": {"closed": True, "forced": False}}
            response = self.client.call("close", session, expectedRevision=state["revision"], force=force)
            if not response["result"]["closed"]:
                self.client.call("viewer", session, visible=True)
            return response

    def shutdown(self, *, force=False):
        """Graceful by default; retain host and apps if any close remains pending.

        `force=True` explicitly discards remaining owned sessions/unsaved changes.
        Adapter errors must not automatically call force shutdown or client.close.
        """
        with self._lock:
            if self._closed:
                return
            if force:
                self.client.close()  # Explicitly authorized loss of all owned unsaved sessions.
                self._closed = True
                return
            pending = {}
            for session in self._sessions:
                response = self.close_session(session)
                if not response["result"]["closed"]:
                    pending[session] = response
            if pending:
                raise ShutdownBlocked(pending)
            self.client.close()
            self._closed = True

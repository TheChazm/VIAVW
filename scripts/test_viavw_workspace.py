"""Policy tests; real installed-application tests live in test-workspace.py."""
from copy import deepcopy
from pathlib import Path
import unittest

from viavw_client import ViavwError
from viavw_workspace import ShutdownBlocked, ViavwWorkspace


class FakeHost:
    def __init__(self):
        self.state = dict(state="ready", applicationPid=123, revision=0, root=42, owner="Automation")
        self.calls = []
        self.closed = False
        self.prompt = False
        self.input_error = None

    def call(self, op, session=None, **fields):
        self.calls.append((op, fields))
        if op == "hello":
            return {"protocol": 1, "operations": ["create", "status", "observe", "input", "attention", "ownership", "viewer", "close"]}
        if op == "create":
            return {"id": "session", "observation": {"session": deepcopy(self.state)}}
        if op == "ownership":
            self.state.update(owner=fields["owner"], revision=self.state["revision"] + 1)
        if op == "viewer":
            self.state["viewerVisible"] = fields["visible"]
        if op == "close":
            if not self.prompt:
                self.state["state"] = "closed"
            return {"session": deepcopy(self.state), "result": {"closed": not self.prompt}}
        return {"session": deepcopy(self.state), "result": {"document": "expected"}}

    def input(self, session, state, kind, **fields):
        self.calls.append(("input", fields))
        if self.input_error:
            raise self.input_error
        if state["revision"] != self.state["revision"]:
            raise ViavwError("stale_revision", "Changed")
        return {"session": deepcopy(self.state), "result": {"delivery": "forwarded_unverified"}}

    def close(self):
        self.closed = True


class WorkspaceTests(unittest.TestCase):
    def setUp(self):
        self.host = FakeHost()
        self.workspace = ViavwWorkspace(Path("unused"), client=self.host)
        self.session = self.workspace.create(executable="fixture")
        self.observation = self.workspace.observe(self.session)

    def assert_code(self, code, action):
        with self.assertRaises(ViavwError) as result:
            action()
        self.assertEqual(code, result.exception.code)

    def test_handoff_does_not_reclaim_and_old_plan_stays_invalid(self):
        shown = self.workspace.request_human(self.observation, "Review")
        self.assertTrue(shown["viewerVisible"])
        self.assert_code("resume_required", lambda: self.workspace.input(self.observation, "key", key=13))
        self.assert_code("human_owns_input", lambda: self.workspace.resume(self.session, lambda _: True))
        self.assertEqual("Human", self.host.state["owner"])
        self.host.call("ownership", self.session, owner="Automation")
        fresh = self.workspace.resume(self.session, lambda data: data["result"]["document"] == "expected")
        self.workspace.input(fresh, "key", key=13)
        self.assert_code("resume_required", lambda: self.workspace.input(self.observation, "key", key=13))

    def test_changed_context_blocks_fresh_observation_too(self):
        self.assert_code("context_changed", lambda: self.workspace.resume(self.session, lambda _: False))
        fresh = self.workspace.observe(self.session)
        self.assert_code("resume_required", lambda: self.workspace.input(fresh, "key", key=13))
        self.assertFalse(any(op == "input" for op, _ in self.host.calls))

    def test_guard_exception_leaves_paused(self):
        def broken_guard(_):
            raise RuntimeError("inspection failed")
        with self.assertRaises(RuntimeError):
            self.workspace.resume(self.session, broken_guard)
        self.assert_code("resume_required", lambda: self.workspace.input(self.workspace.observe(self.session), "key", key=13))

    def test_ambiguous_delivery_is_not_retried_or_implicitly_closed(self):
        self.host.input_error = ViavwError("transport_failed", "Disconnected", "unknown")
        self.assert_code("transport_failed", lambda: self.workspace.input(self.observation, "text", text="once"))
        self.assert_code("resume_required", lambda: self.workspace.input(self.observation, "text", text="once"))
        self.assertEqual(1, sum(op == "input" for op, _ in self.host.calls))
        self.assertFalse(self.host.closed)

    def test_native_save_prompt_keeps_workspace_alive(self):
        self.host.prompt = True
        with self.assertRaises(ShutdownBlocked) as result:
            self.workspace.shutdown()
        self.assertIn(self.session, result.exception.pending)
        self.assertFalse(self.host.closed)
        self.host.prompt = False
        self.workspace.shutdown()
        self.assertTrue(self.host.closed)

    def test_close_error_keeps_host_alive_until_explicit_force(self):
        original = self.host.call
        def failing(op, *args, **kwargs):
            if op == "close":
                raise ViavwError("transport_failed", "Disconnected", "unknown")
            return original(op, *args, **kwargs)
        self.host.call = failing
        self.assert_code("transport_failed", self.workspace.shutdown)
        self.assertFalse(self.host.closed)
        self.workspace.shutdown(force=True)
        self.assertTrue(self.host.closed)

    def test_identity_change_blocks_resume(self):
        self.host.state["applicationPid"] += 1
        self.assert_code("application_changed", lambda: self.workspace.resume(self.session, lambda _: True))

    def test_explicit_zero_window_is_preserved(self):
        self.workspace.input(self.observation, "click", window=0, x=0, y=0)
        self.assertEqual(0, self.host.calls[-1][1]["window"])

    def test_owner_race_invalidates_plan(self):
        self.host.call("ownership", self.session, owner="Human")
        self.assert_code("stale_revision", lambda: self.workspace.input(self.observation, "key", key=13))
        self.assert_code("resume_required", lambda: self.workspace.input(self.observation, "key", key=13))

    def test_request_headroom_preserves_shutdown_path(self):
        self.host.requests_remaining = 64
        self.assert_code("workspace_capacity", lambda: self.workspace.input(self.observation, "key", key=13))
        self.assert_code("workspace_capacity", lambda: self.workspace.create(executable="fixture"))
        self.assertFalse(self.host.closed)
        self.workspace.shutdown()
        self.assertTrue(self.host.closed)

    def test_close_one_layer_keeps_host_alive(self):
        response = self.workspace.close_session(self.session)
        self.assertTrue(response["result"]["closed"])
        self.assertFalse(self.host.closed)

    def test_close_one_layer_exposes_native_prompt(self):
        self.host.prompt = True
        response = self.workspace.close_session(self.session)
        self.assertFalse(response["result"]["closed"])
        self.assertEqual(("viewer", {"visible": True}), self.host.calls[-1])
        self.assertFalse(self.host.closed)

    def test_legacy_host_does_not_silently_ignore_capture_option(self):
        before = len(self.host.calls)
        self.assert_code("unsupported_capability", lambda: self.workspace.create(executable="fixture", captureMode="print-window"))
        self.assertEqual(before, len(self.host.calls))

    def test_legacy_host_does_not_silently_ignore_posted_click_option(self):
        before = len(self.host.calls)
        self.assert_code("unsupported_capability", lambda: self.workspace.input(self.observation, "click", deliveryMode="posted", x=0, y=0))
        self.assertEqual(before, len(self.host.calls))

    def test_legacy_host_rejects_input_validation_option_before_delivery(self):
        before = len(self.host.calls)
        self.assert_code("unsupported_capability", lambda: self.workspace.input(self.observation, "key", key=13, validationMode="target-subtree"))
        self.assertEqual(before, len(self.host.calls))

    def test_supported_input_validation_option_reaches_host(self):
        self.workspace.capabilities["inputValidationModes"] = ["full-window-scan", "target-subtree"]
        self.workspace.input(self.observation, "key", key=13, validationMode="target-subtree")
        self.assertEqual(self.host.calls[-1][1]["validationMode"], "target-subtree")


if __name__ == "__main__":
    unittest.main()

"""Transport failure containment without launching applications."""
import io
import json
import queue
from types import SimpleNamespace
import threading
import unittest

from viavw_client import ViavwClient, ViavwError


class ClientTests(unittest.TestCase):
    def client(self, reply):
        client = ViavwClient.__new__(ViavwClient)
        client.process = SimpleNamespace(stdin=io.StringIO())
        client._responses = queue.Queue()
        client._responses.put(reply)
        client._broken = False
        client._requests_sent = 0
        client._lock = threading.Lock()
        client._last = None
        return client

    def test_bad_envelopes_invalidate_stream_without_replay(self):
        for reply in ([], {"protocol": 1, "id": "test"},
                      {"protocol": 1, "id": "test", "ok": True},
                      {"protocol": 1, "id": "test", "ok": False, "error": {}},
                      {"protocol": 1, "id": "other", "ok": True, "data": {}}):
            with self.subTest(reply=reply):
                client = self.client(reply)
                request = json.dumps({"id": "test", "protocol": 1, "op": "status"})
                with self.assertRaises(ViavwError) as result:
                    client._exchange(request, .1)
                self.assertEqual("unknown", result.exception.outcome)
                before = client.process.stdin.getvalue()
                with self.assertRaises(ViavwError) as result:
                    client._exchange(request, .1)
                self.assertEqual("stream_unusable", result.exception.code)
                self.assertEqual(before, client.process.stdin.getvalue())

    def test_local_size_rejection_does_not_consume_host_budget(self):
        client = self.client({})
        with self.assertRaises(ValueError):
            client.call("input", text="X" * 65536)
        self.assertEqual(4096, client.requests_remaining)
        self.assertEqual("", client.process.stdin.getvalue())

    def test_local_request_budget_rejects_before_write(self):
        client = self.client({})
        client._requests_sent = 4096
        with self.assertRaises(ViavwError) as result:
            client.call("status")
        self.assertEqual("request_budget", result.exception.code)
        self.assertEqual("", client.process.stdin.getvalue())

    def test_timeout_consumes_conservative_allowance_and_stops_stream(self):
        client = self.client({})
        client._responses.get_nowait()
        with self.assertRaises(ViavwError):
            client.call("status", timeout=.001)
        self.assertEqual(4095, client.requests_remaining)
        self.assertTrue(client._broken)


if __name__ == "__main__":
    unittest.main()

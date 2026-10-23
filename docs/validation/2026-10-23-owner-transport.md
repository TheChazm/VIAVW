# Fail-closed owner transport: 23 October 2026

The reusable C# owner connection accepted another call after an ambiguous reply.
An in-process fake owner reproduced this using the original source: a mismatched
reply returned transport_unknown, but a second simulated input was accepted and
the fake owner counted two requests. No real application or layer was involved.
This establishes a transport-class reuse defect, not an observed physical-input
incident. The higher-level tool's persistent uncertainty fence already provides
additional protection and remains unchanged.

The connection now validates protocol/request identity, success/error envelopes
and handshake epoch/sequence. Timeout, EOF, malformed or misframed replies close
only that local connection and leave its outcome unknown. Later calls fail before
writing, requiring a fresh connection and actual state observation/reconciliation;
they are never silently replayed. The normal 90-second response deadline remains;
the internal constructor allows a shorter deadline for deterministic tests.

Well-framed owner errors are preserved, including not_started and unknown. The
stream remains usable for follow-up inspection after such a response; the
higher-level ownership and input-uncertainty policies still determine whether a
mutation is allowed. No ownership reclaim or recovery action was added.

The console regression links actual production Transport.cs and uses randomly
named current-user in-process fake pipes. Twenty-seven assertions passed:
wrong ID, malformed JSON, wrong protocol, wrong-protocol rejection, missing ok,
malformed errors, EOF and timeout all report unknown and reject a second dispatch;
ordinary ordering and valid owner errors retain their behavior; uninitialized,
disposed and invalid-handshake connections reject input. Tool and harness builds
passed offline with zero warnings/errors and no package dependencies downloaded.

The first harness run passed fourteen assertions and failed while disposing the
fake EOF writer. Its cleanup now catches the expected InvalidOperationException
only when the fake pipe is disconnected; unexpected failures still propagate.
The corrected harness passed all twenty-seven checks. Failed and successful raw
results are retained locally, not replaced or committed as private artifacts.

No GUI, physical input, real application layer, live service, broad fault
injection or installed-package test ran. This source-only result does not prove
actual application compatibility, durable memory recovery or Human acceptance.
Installed/selected 0.1.2, CharlieAI and parallel platform work remain untouched.
See BUILDING.md for the no-GUI regression commands.

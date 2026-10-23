# VIAVW automation interface for CharlieAI integration

An external client can now launch and operate installed applications through VIAVW, inspect their state, request attention, and exchange ownership with the existing viewer. CharlieAI itself remains untouched. The interface is a tested first integration seam, rather than a complete autonomous agent or universal application driver.

## Implementation

Protocol 1 exchanges JSON lines with a local child process. It exposes session creation/discovery, windows, optional screenshots/accessibility snapshots, native text/key/chord/click input, attention, ownership, viewer visibility and close. There is no new network listener, driver or persistent service. Each session keeps the existing private desktop, owned process job, capture worker and viewer thread.

Input requires the current ownership revision. A Human → Automation round trip invalidates old commands. Exact request retransmissions return cached historical results; expired IDs never execute again and report an unknown original outcome. The standard-library Python client serializes calls, never silently refreshes an input revision, and stops using a stream after uncertain transport failure.

Hidden viewers start without activation. Explicit observation exports immutable PNG/JSON files under a shared 64 MiB / 256-file budget. The host bounds active sessions, request IDs, response history and window metadata. Native input remains restricted to currently owned windows and valid parent hierarchies.

The API build has its own manifest, `artifacts/agent-api-current.json`. The normal Visual Studio demo retains its previous manifest and accepted executable. Source/PDB snapshots and failed experiments remain available for recovery.

## Installed application results

The final interface suite passed 33 checks. The preceding implementation passed repeated runs and the public test script; its only final runtime change is the neutral close-reason name for remaining owned processes.

- **Notepad++:** two independent instances with separate disposable settings; selection/edit/save; Open and Save As dialogs; file switching; exact saved content; native editor clicking; rejected cross-session targets; attention and same-process ownership handoff.
- **Visual Studio 2022 Community:** normal package loading in the established isolated software-rendering profile; locating/clicking the actual editor; verifying selection; Enter, text, Backspace and exact save; opening another source file through its ordinary dialog.
- **Failure handling:** a save prompt preserves unsaved data and transfers ownership; one killed application leaves its peer usable; graceful VS exit reports remaining owned processes instead of silently killing them; explicit force, EOF, and abrupt host termination release owned trees.

Captured Notepad++, VS editor and save-prompt images were inspected and matched the demonstrated state. The final suite's read-only desktop monitor passed: no owned process became foreground, and pointer, focus, desktop and confinement were preserved. These tests use programmatic ownership transitions; physical human acceptance of the new interface remains pending.

Regression checks also passed: 32 existing viewer editing checks, 15 mouse checks, 15 independent-capture checks and 25 shared-transfer checks. Five reference-client checks verified cached responses, local oversized-request rejection, recovery after that rejection, host disconnection and rejection of further commands on an uncertain stream.

Six additional context/target checks used the ordinary Open dialog to switch to another real disposable file after programmatic ownership changes. The demo's resumption guard rejected the different file even when its name shared the original path prefix, emitted no further input and left both files unchanged. An explicit zero HWND is also rejected rather than silently redirected to the root. No physical human action was fabricated.

## Performance observations

External-client round trips from two installed-application runs:

| Operation | Samples | Median |
| --- | ---: | ---: |
| Status | 168 | 0.39 ms |
| Window observation without exported image/accessibility | 73 | 4.39 ms |
| Ownership change | 4 | 1.55 ms |

These are small diagnostic samples with varying application state, not visual-latency or CPU benchmarks. Window observation reached 306 ms during transitions. Mixed input requests ranged from about 2 to 257 ms; that group includes different text, key, chord and click operations and is not a per-character typing measure. Deep VS accessibility inspection cost approximately 1.14–3.69 seconds in the readiness/selection checks. It stays opt-in and should not be polled at typing frequency. The hidden Notepad++ preview version stayed unchanged during its 2.2-second idle check. New CPU/GPU utilization benchmarks were not collected for this interface.

## Discoveries and unsuccessful experiments

Dialogs can disappear between enumeration and inspection. The interface reports an observation change rather than replaying a command; clients may repeat that read. A closing viewer also needs a stable closed-status tombstone to avoid racing disposal. Save As can briefly hold its newly created file exclusively, so disk verification retries only known sharing violations.

Replacing synchronous input-window interrogation with presentation-only discovery lost the VS saved result. Verifying editor readiness and selection alone did not fix that variant. The final implementation retains the proven synchronous validation on input, while using faster stored-caption discovery for observations. Its precise synchronization effect is not established; retained evidence supports further controlled investigation rather than an assumption that faster enumeration is equivalent.

Input acknowledgement is deliberately separate from application completion. The verifier checks actual editor text before requesting Save, and checks disk content afterward. A document title or successful message submission alone is insufficient evidence.

## Try the collaboration demonstration

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-agent.ps1"
```

The demonstration edits/saves a disposable Notepad++ document, requests your attention and gives Human ownership. Select the viewer when convenient; it does not activate itself. Edit and save, choose **Return to automation**, then press Enter in the terminal. The client resumes in the same running application and saves its appended text. It leaves you time to inspect the result before closing.

If you switched documents or left a dialog open, the demo pauses rather than typing into an unexpected context. It re-observes the actual document after ownership returns; the unchanged process ID alone is insufficient.

Keep the terminal/client alive while working: EOF or host exit ends its owned sessions, including unsaved disposable work. Graceful close preserves prompts; force close is explicit. Use `scripts/test-agent.ps1` for automated verification or `scripts/demo-agent.ps1 -Smoke` for programmatic handoff only. Protocol details and examples are in `docs/AGENT-PROTOCOL.md`.

## Remaining work

The API currently exposes native text/key/chord/click commands, not every viewer pointer adaptation, drag or wheel operation. GPU capture limitations remain application-specific. Notepad++ used built-in GDI rendering; VS used the accepted software profile. Word and arbitrary launch configurations have not been verified. Applications that broker work into an existing instance require an explicitly tested launch configuration; this wrapper is not a security sandbox.

Existing secure-desktop guards remain: input/capture can be refused while Windows makes the input desktop unavailable, such as during a protected prompt. This interface does not bypass those boundaries.

Commands are serialized within one host, even though its applications and viewer threads run independently. There is no persistent cross-host session recovery, automatic input replay or generic inference that an application is ready. Human notification policy and long-lived host ownership belong in the future CharlieAI adapter.

The next useful experiments are selective accessibility observation and input-queue synchronization, followed by a thin CharlieAI adapter that preserves the host across agent turns and human handoff. Changes to CharlieAI remain outside this milestone.

The final audit passed 19 checks, including preserved baseline hashes/manifest, source/PDB correspondence, scripts, test results and cleanup. No owned test application, helper or project LayerHost process remained. Retained experiment/build/snapshot/demo directories occupy about 162 MiB; the exact scoped total is in the audit. All 139 available PDB source documents match the preserved source; one SDK-generated virtual document is unavailable. Protected browser profiles remain retained; no deletion policy was bypassed. No Git operations or unrelated project changes were performed.

Machine-readable results: `artifacts/agent-api-20261021-evidence.json`. Final audit: `artifacts/agent-api-20261021-final-audit.json`. Final source/PDB snapshot: `artifacts/baselines/agent-api-20261021l`.

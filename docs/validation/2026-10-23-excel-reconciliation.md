# Excel modal uncertainty: recovery coverage — 2026-10-23

The installed-application report records Excel F12 opening Save As while input returned unknown. That report demonstrated containment, fresh observation, explicit reconciliation and cancellation, rather than completion of Save As through that uncertain transition. See [the original results](../INSTALLED-APPLICATION-FOLLOWUP-20261023.md).

This follow-up validates the actual ToolService/Transport reconciliation boundary with an in-process fake modal owner and synthetic private files. It does not run Excel, start LayerHost, invoke application automation or manufacture workbook output. The constructor checks a deliberately nonexecutable synthetic host-hash fixture; the fake descriptor identifies the test process and a random current-user pipe. Only hello/observe/status/input/reconcile operations are permitted by the peer. No global input, clipboard, foreground or capture APIs are called.

Independent offline build and verification passed with .NET SDK 10.0.302: zero warnings/errors and 30 assertions. Tests link the unchanged production ToolService.cs, Transport.cs and DetachedProcess.cs. An allowlisted test entry point never calls create/recover/handoff/close; detached launch APIs are not invoked. Runtime fixtures are removed using exact known file paths and nonrecursive empty-directory deletion; unexpected files cause cleanup failure rather than broad deletion.

Covered: intent persisted before dispatch; F12-like unknown result; stale pre-input token; persistent fence after a new ToolService reconnect; required context confirmation/reason; tampered token; changed revision/sequence/root; Human ownership; worker/keeper suspension; accepted reconciliation without replay; reason persistence; explicit subsequent input; partial multiline dispatch whose later not-started segment must leave the whole operation unknown; no trailing text replay; and unknown owner-recovery reconciliation preserving the fence until a fresh explicit decision is acknowledged.

No new product defect was established, and production behavior was not changed. Fake responses assert protocol/persistence semantics; they do not establish actual Office control behavior, visual freshness, saved workbook correctness or human noninterference. Several existing VIAVW client processes were present in read-only process metadata; absence of an active Excel process did not establish a conflict-free integration window, so no live test was started. Existing processes were not contacted or altered.

| Application / operation | Evidence and current status |
| --- | --- |
| Excel formulas and CSV values | Historical installed run verified computed 6 and 5; not rerun here. |
| Excel F12 opens Save As, then cancel after reconciliation | Historical installed run; modal input can return unknown. No additional GUI qualification here. |
| Tool fence/reconciliation for unknown modal and partial input | 30 new production-linked fake-peer assertions passed. Cross-client persistence and no-replay behavior covered. |
| Save the intended new XLSX after that unknown F12 transition; reopen and verify formulas/data | Pending a dedicated isolated real-Excel workflow. Earlier XLSX evidence does not prove this exact transition. |
| Fresh visual capture and concurrent physical human use for this workflow | Pending. No captures were generated in this follow-up. |

Installed/selected 0.1.2 tool-20261023i and CharlieAI remain unchanged. No GUI, installation, security change or external publication occurred.

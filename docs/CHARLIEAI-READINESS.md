# VIAVW collaboration readiness — October 22, 2026

Later [installed-application research](INSTALLED-APPLICATIONS-20261022.md) added direct Codex control and Word/VLC/7-Zip evidence. It also observed an intermittent deep VS accessibility timeout that terminates the owned layer. The unchanged accepted API shares that containment mechanism: controlled integration status does not mean crash-safe preservation of unsaved application state. The later `Apps-20261022l` research build isolates passive tree/menu reads and adds nonblocking inspection commands; the accepted API host remains unchanged. Other provider-assisted paths and physical human acceptance remain gates before broader unattended use.

The VIAVW side has a tested Python integration interface and a working human/automation demonstration. Actual CharlieAI integration and physical human acceptance remain separate release gates. At the user's explicit instruction on October 22, CharlieAI remains unchanged; only VIAVW is prepared here. This is ready for a controlled integration using the demonstrated applications, not a universal Windows application guarantee.

## Start here

Run the shared application demonstration:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-collaboration.ps1"
```

It opens installed Notepad++ with disposable documents/settings on a private desktop. Automation edits/saves, then the inspector opens in Human control without taking foreground focus. Select it, click in the document, use Enter/Backspace, edit and save. Choose **Return to automation**, then press Enter in the demonstration terminal. Automation checks the complete document path, preserves the same process, appends text and verifies the saved file. Switching to another document pauses continuation; reopen the printed original path when ready.

After inspection, press Enter in the terminal to close. A native save prompt keeps the workspace alive and opens the inspector. Resolve the prompt there, then retry. Only typing `FORCE` explicitly discards remaining disposable state. Keep the terminal open: terminating its owning process still closes its applications. A routine automation exception does not automatically force-close the workspace. `-Smoke` tests the programmatic path; it does not establish physical human acceptance.

The existing Visual Studio demonstration remains available:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

## Check this workstation

```powershell
& "C:\Programming\Projects\VIAVW\scripts\test-agent-readiness.ps1" -StartupOnly
& "C:\Programming\Projects\VIAVW\scripts\test-agent-readiness.ps1"
```

Startup checks validate Windows/Python, the preserved host and client bundle, input-desktop access, and the normal installed Notepad++ location. The input-desktop check only opens a read handle; it never switches desktops or sends physical input. A UAC/secure-desktop interruption can make this check fail; dismiss the prompt normally and retry. Custom application installation paths may require updating this prerequisite check, not reinstalling the application.

The complete command runs transport/collaboration policy tests, actual installed Notepad++ workspace workflows, the existing installed Notepad++/Visual Studio protocol suite and both demonstration smoke paths. Test files/processes are disposable and project-owned. New evidence directories are printed; retained experimental files are not deleted.

## CharlieAI adapter contract

Use `scripts/viavw_workspace.py` with `scripts/viavw_client.py` in the **existing long-lived CharlieAI backend**, keeping a single workspace object alive across tool invocations and human turns. Both use only the Python standard library, with Python 3.10 or newer. They load this workstation's absolute paths from the preserved project manifests; this is not a relocatable distribution. Other languages can implement [protocol 1](AGENT-PROTOCOL.md).

| Adapter action | VIAVW call | Required behavior |
| --- | --- | --- |
| Start application | `workspace.create(executable=..., arguments=[...])` | Use a verified disposable launch configuration. VS uses `profile="visual-studio", setupFile=...`. |
| Inspect | `workspace.observe(session, capture=True)` | Returns an `Observation` with `data` and `state`; capture/accessibility are optional. |
| Act | `workspace.input(observation, "text", text=...)` | Supply the observed revision and owned HWND; semantic completion must be checked separately. |
| Ask for user | `workspace.request_human(observation, reason)` | Opens the viewer, gives Human control, invalidates previous plans. CharlieAI handles its own user notification. |
| Wait for user | Keep backend/workspace alive; inspect status sparingly | Never automatically reclaim ownership. The user chooses Return to automation. |
| Continue | `workspace.resume(session, context_matches)` | Requires Automation ownership, the same application PID, and a workflow-specific predicate on a fresh observation. |
| End work | `workspace.shutdown()` | A `ShutdownBlocked` exception keeps the host/apps alive and exposes pending native prompts. |
| Close one application | `workspace.close_session(session)` | Returns the close result, opens any pending prompt, and leaves other layers/host running. Explicit `force=True` affects only that owned layer. |
| Explicit discard | `workspace.shutdown(force=True)` | Ends all owned sessions, including unsaved state. Never put this in a routine adapter error handler. |

`Observation.data` uses the protocol observation shape. The predicate receives that dictionary, not an `Observation` object. See the Notepad++ full-path predicate in `demo-agent.py` and its use in `demo-collaboration.py`. There is no generic application-context oracle: do not substitute an always-true predicate. Document paths, active modal state, editor identity and workflow results must be checked for the application being automated. Human edits to the intended document are preserved and must be reread before deciding what to do next.

Do not spawn a host inside each short-lived tool call, use the low-level client's context manager around a whole conversational turn, or force-close in a `finally` handler. **Host exit/EOF still releases its owned application jobs and can lose unsaved state.** Neither this wrapper nor the OS job provides crash recovery of an application's unsaved memory. On transport failure, stop automation and report the uncertain outcome; do not reconnect and replay input.

The workspace serializes adapter actions, leaves existing viewer concurrency intact, and refuses ordinary operations with 64 protocol requests remaining so graceful shutdown has headroom. Resolve/save work and end that workspace before creating a fresh one; never restart a host while preserving its application state is required. Four active sessions, sixteen lifetime sessions and 4096 unique requests remain host limits. Observation exports retain the shared 64 MiB / 256-file budget; stop requesting new artifacts when exhausted. Window/status inspection and viewer operations do not require exporting an artifact. There is no background artifact deletion or unlimited unattended-run claim.

## Verified scope and evidence

- Eighteen policy/transport tests: human ownership, context rejection, stale plans, identity changes, uncertain delivery without replay, malformed replies, request headroom, experimental-mode negotiation and per-layer/graceful/explicit shutdown.
- Nineteen workspace checks against installed Notepad++: exact disk contents after editing, Enter/Backspace, ownership handoff, wrong-document guard, same process, save prompt preservation, inspection, closing one layer while another remains usable, and owned-process cleanup.
- Thirty-three existing installed-application protocol checks rerun successfully: two independent Notepad++ instances, dialogs/file switching/Save As, real Visual Studio editor selection/type/Enter/Backspace/save/open, ownership rejection, source failure containment, EOF/crash cleanup and unchanged personal Notepad++ settings.
- New collaboration launcher completed its programmatic smoke path and verified actual saved text after handoff. Physical human operation is still pending.

The current bundle manifest, `artifacts/agent-workspace-current.json`, records the preserved scripts/docs and evidence. Existing normal-viewer and API executable selections remain separate and unchanged. No C# runtime or capture/input engine was changed in this readiness pass. Policy tests are not counted as additional application compatibility coverage.

## Remaining release gates

1. Implement the actual CharlieAI adapter in its own project with permission to edit that project. Verify its tool cancellation, error reporting, backend lifetime, human notification and shutdown behavior using disposable documents.
2. Run the interactive demonstration above and confirm physical mouse, shortcuts, Enter/Backspace, save and same-session return. Automated ownership transitions cannot replace that acceptance.
3. Accept supported application launch profiles/workflows individually. Private-desktop GPU capture is still application-specific; the verified VS profile uses software rendering. The API does not expose every viewer drag/wheel adaptation, and universal input/device emulation is not claimed.

These gates must remain visible in CharlieAI's integration status. Full-tree VS accessibility observations can take seconds, whereas inexpensive window/status inspection is much faster; inspect deeply only when the workflow needs it. No new foreground-native speed claim or CPU/GPU optimization is made by this packaging pass.

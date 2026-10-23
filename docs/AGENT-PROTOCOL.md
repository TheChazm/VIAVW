# VIAVW local automation interface

For the adapter-facing collaboration owner, startup checks and current handoff demonstration, use [CharlieAI readiness](CHARLIEAI-READINESS.md). `viavw_workspace.py` adds explicit context-checked resume and graceful shutdown that preserves pending save prompts. The low-level client's context manager still ends its owned sessions on exit.

The separate [installed-application research build](INSTALLED-APPLICATIONS-20261022.md) adds `surface` selection and advertises `captureModes`/`clickDeliveryModes` in `hello`. Those additions are available through the explicit research host/direct console; the accepted ordinary API executable remains unchanged. The workspace rejects requested experimental modes when the selected host does not advertise them. Deep accessibility can exceed the existing worker timeout and terminate its owned application layer; that observed inspection-fault containment limit remains unresolved.

Protocol 1 is a small interface for a future CharlieAI adapter. CharlieAI is not modified or required. A local client owns a `LayerHost.exe --agent-stdio <fresh-artifact-directory>` process and exchanges one UTF-8 JSON object per line through its standard input/output. There is no network listener, driver, or persistent service.

The host uses the existing private-desktop layers, capture workers and viewer. Each session has its own application job and viewer STA thread. Protocol commands are serialized; the applications and their existing background workers continue independently between commands. This first interface does not promise concurrent RPC execution or universal application compatibility.

## Demonstration

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-agent.ps1"
```

The Python standard-library client starts installed Notepad++ with disposable documents/settings and its built-in GDI rendering setting. It edits and saves, requests attention, rejects automation while Human owns input, and opens the existing viewer without activating it. Select that viewer, edit and save, then choose **Return to automation**. Press Enter in the demonstration terminal to let automation append text in the same application. The demonstration checks the actual saved file and captures the result.

`-Smoke` performs programmatic ownership transitions without showing the viewer; this does not establish physical human acceptance. `scripts/test-agent.ps1` runs the installed-application protocol suite and the reference-client smoke demonstration.

The preserved interface executable and SHA-256 are recorded separately in `artifacts/agent-api-current.json`. The normal Visual Studio demonstration keeps its existing `artifacts/visual-studio-current.json` selection. The Python client validates the selected DLL hash before starting it.

## Requests and responses

Each request contains `protocol: 1`, a unique string `id` of 1–128 characters, and `op`. Session operations also require the opaque `session` returned by `create`.

```json
{"protocol":1,"id":"hello-1","op":"hello"}
```

Success returns `{"protocol":1,"id":"hello-1","ok":true,"data":...}`. Errors return `ok:false` and an `error` object containing `code`, `message` and `outcome`. An unparsable request can have a null response ID. Standard output contains protocol responses; diagnostics use standard error and project artifacts.

| Operation | Fields and result |
| --- | --- |
| `hello` | Capabilities, input kinds, lifecycle contract and limits. |
| `create` | `profile:"private-dc"`, absolute `executable`, optional `arguments` array. Returns session ID and initial state. The installed executable and launch arguments are supplied by the trusted caller. |
| `create` for VS | `profile:"visual-studio"`, `setupFile` from the existing disposable runnable-solution preparation. Validates that setup and holds the exclusive experimental-profile lease. |
| `list` | Known session IDs and whether their viewer threads remain alive. Use `status` for application state. A closed viewer returns a minimal `state:"closed"` tombstone. |
| `status` | State, application PID, owned process IDs, owner, ownership revision, selected root, input sequence, preview version, attention and viewer visibility. |
| `observe` | Optional `capture:true` and/or `accessibility:true`. Returns current owned windows, source-client origin, and optional PNG/accessibility artifact paths. PNG metadata includes size and SHA-256. |
| `input` | `expectedRevision`, owned `window`, `kind` and fields below. Successful delivery is explicitly `forwarded_unverified`. |
| `attention` | `reason` of 1–1024 characters. Records attention intent in status/viewer; the client decides how to notify its user. It does not take ownership or steal focus. |
| `ownership` | `expectedRevision`, `owner:"Human"` or `"Automation"`. Updates the same ownership mechanism used by the viewer toolbar. |
| `viewer` | `visible:true` shows the inspector without activation and enables its normal preview. `false` hides it and stops its preview. |
| `close` | `expectedRevision`; optional `force:true`. Graceful close can return `closed:false`, `needsHuman:true` and a reason. Explicit force releases only that session's owned process tree. |

Input kinds are `text` (`text`, up to 4096 UTF-16 code units), `key` (`key`, Windows virtual-key number), `chord` (`keys`, 1–8 virtual keys), and `click` (`x`,`y`, target-client coordinates). Modifiers precede the final key. Use real Enter/Backspace key commands rather than assuming character input reproduces those keys. Nonfunction chords retain the tested precise pacing; function transitions retain protective pacing.

```json
{"protocol":1,"id":"edit-1","op":"input","session":"SESSION_ID","expectedRevision":0,"window":123456,"kind":"text","text":"Example text"}
```

Use the actual session ID, revision and HWND returned by observation. Window handles are checked against the session's current owned windows; disabled/hidden targets and disabled parent hierarchies are rejected. Clicks require valid target-client coordinates. Images use the selected root's source-client coordinates; child-window coordinates may differ.

## Ownership and completion

`ready` means the layer has a usable owned window, not that the application finished loading or its editor has focus. Observe the actual document/control before acting. `application_root_exited` is distinct from a closed session: owned descendants may still be running.

The revision changes when ownership changes, including an Automation → Human → Automation round trip. Old commands remain stale after that round trip. The reference client's `input` method requires a previously observed state and never silently refreshes its revision. Human toolbar actions and external commands share the viewer command gate; source input also retains the layer's owner checks.

Window discovery uses bounded stored captions. Input validation deliberately retains the proven synchronous control interrogation: replacing it with presentation-only discovery lost rapid VS edits in this hidden-session experiment. The underlying mechanism still needs controlled investigation.

An accepted input request means messages were forwarded, not that the application processed them, rendered the result, completed a build, or saved a file. Inspect actual accessible/visual state or a workflow-specific result before proceeding. `observation_changed` and `no_surface` can occur while a dialog closes or another surface opens; only the read-only observation may be repeated. Input is never automatically replayed.

Request IDs deduplicate submissions for one host lifetime. An exact retransmission of the same serialized request returns its cached historical response. Changing its payload, including serialization, produces `id_conflict`. The newest 32 responses are retained. Older IDs remain remembered: `result_expired` reports an **unknown original outcome** and never executes that ID again. Observe current state before making a new decision. This is not a persistent exactly-once guarantee across host restarts.

The Python reference client marks a timed-out or misframed stream unusable, because its next response could belong to an earlier request. It does not turn a transport timeout into a fresh input request.

After a handoff, re-observe the application context as well as its owner. The Notepad++ demonstration verifies the complete active document path before appending. A different file, open dialog or another ownership change pauses its automation instead of silently reusing the old plan.

## Lifecycle and bounds

Keep the host process alive throughout human collaboration. **EOF or host exit closes every session it owns, and unsaved changes can be lost.** Kill-on-close jobs also release the owned applications when the host terminates abruptly. This is cleanup, not application-state recovery after a host crash.

Graceful close first gives the application its normal close request. A save prompt preserves the application and transfers input ownership to Human. Visual Studio may exit while owned ServiceHub helpers remain; the response distinguishes `owned_processes_remaining` from `application_or_prompt_running`. The reason names remaining owned processes without assuming they are all helpers; other applications may delegate work to child processes. The client must inspect and decide whether explicit force close is appropriate. Observations remain available while a prompt is pending.

One host permits four active sessions, sixteen total session IDs and 4096 unique request IDs. Request lines are limited to 65536 characters. Window observations prioritize the selected root/top-level windows and return at most 256 entries with 256-character titles; `truncated` reports omitted entries. Accessibility is a separate bounded inspection snapshot. Exported observations have a shared 64 MiB / 256-file budget, with at most 16 MiB per artifact. Existing per-viewer status files are overwritten rather than retained as history. Background sessions do not continuously capture after startup; observations explicitly request work.

## Demonstrated scope

Installed Notepad++ was tested with separate settings, `-multiInst -nosession -noPlugin`, and built-in GDI rendering. Installed Visual Studio 2022 Community used the established isolated software-rendering profile with normal packages. Tests cover real files, selection/edit/save, ordinary file dialogs, file switching, ownership rejection and cleanup. API-native clicks were tested in the Notepad++ editor; Visual Studio dialog navigation used keyboard input. The API does not yet expose every viewer pointer adaptation, drag or wheel interaction.

This is an interaction wrapper, not a filesystem/security sandbox or virtual keyboard/mouse device. A trusted caller chooses launch arguments and must account for application single-instance/broker behavior; the host never attaches unrelated windows. Unverified launch configurations, Word, arbitrary VS workloads, and general private-desktop GPU capture are not accepted compatibility claims. Actual physical human acceptance of this interface demonstration is pending.

Implementation: `src/LayerHost/AgentHost.cs`, `AgentForm.cs`, and `scripts/viavw_client.py`. Verification and development findings are summarized in `docs/AGENT-INTEGRATION-20261021.md`.


The installed-app research build `Apps-20261022l` additionally advertises `inspect-begin`/`inspect-poll`, which isolate passive accessibility reads in a reusable helper. These return sampled context, partial/timeout state and a stale flag without waiting for the provider; the normal API selection above remains unchanged. See [direct Codex control](CODEX-DIRECT-CONTROL.md#fast-passive-inspection-research-build-apps-20261022l) for its deadlines and limits. This is a research capability, not a guarantee that every capture or provider-assisted input path preserves application state on failure.

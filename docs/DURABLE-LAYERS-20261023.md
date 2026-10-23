# Durable layer verification — October 23, 2026

The selected `Durable-20261023d` release separates an application's lifetime from the automation client and its capture/input workers. Your physical machine remains layer 0; application layers share its Windows installation and resources while using separate private desktops and owned process groups. There is no four-layer limit or 4,096-request lifetime cap in protocol 2. Actual Windows resources still bound capacity.

## Implementation and observed behavior

- Client exit/disconnect leaves the application running.
- A failed input/capture worker is replaced independently of the application.
- A small keeper retains the application group if its controller dies. A replacement controller adopts the same running application after verifying the previous owner's identity/death.
- Keeper failure is repaired by a surviving controller, including when the input worker is also failed. Simultaneous controller and keeper loss is outside this preservation guarantee.
- Uncertain input is paused and never automatically replayed. A fresh observation and application-specific reconciliation are required before automation continues.
- Human ownership survives controller replacement. Recovery cannot silently reclaim it.
- Closing a durable viewer hides presentation and leaves its application alive. An explicit application close remains available and preserves native save prompts; explicit force close discards only that layer.
- Protocol-2 text converts CR, LF and CRLF to Enter and Tab to a Tab key. Actual Notepad++ output preserves the tested newlines, tab and Unicode. Applications retain their own indentation/navigation behavior; this is keyboard interaction rather than a generic literal-paste guarantee. Legacy protocol-1 text behavior is unchanged.
- Observations and request logs have per-layer retention budgets. Replacements reuse the original management address and observation store.

The early W candidate failed its first endurance cycle because raw character messages silently omitted line breaks. A separate mixed-input test exposed the same problem with Tab. Both failures and actual saved files are retained; the B candidate addresses the control-key delivery. Existing selected Visual Studio and legacy API executables remain preserved. A subsequent native-save-dialog test found that graceful close had not persisted its Human handoff. Candidate C persists ownership before requesting close and opens the pending Human viewer. The test kills/replaces the controller, verifies that Automation cannot reclaim the prompt, cancels it through private input and saves the exact previously unsaved contents. The client also retries only known, definitely-not-started observation failures briefly while a dialog disappears; mutating input is never retried.

## Verification

The final serial release run checks actual installed Notepad++ and 7-Zip, six independent layers, 4,200 client requests, duplicate/expired requests, uncertain input, worker/keeper/controller failures, actual unsaved document contents, viewer close behavior, ownership, per-layer retention, and application cleanup. It also tests ordinary archive extraction while another layer edits and saves.

A twenty-minute installed Notepad++ endurance run performs repeated selection, multiline typing, Backspace, Enter and saving, with client reconnects and controller replacements. Saved file bytes and the application PID are checked throughout. Process resources are sampled during the workload; a separate read-only monitor includes the controller process. The selected build passed 1,012 complete edit/save cycles over 1,202.5 seconds, with sixteen controller replacements and no surviving test processes. The full release gate passed 98 explicit checks across ten stages. The separate legacy Notepad++/Visual Studio regression passed 33 checks with physical focus, pointer and desktop unchanged; eight snapshot-publication cases, twenty existing client-policy tests and four memory-admission boundary cases also passed.

The six-action workflow (selection, text, Backspace, Enter, more text and Save, including observations/file verification) measured 158.3 ms median and 267.3 ms maximum. This is not individual-keystroke/display latency. The 4,200-status workload took 2,081.7 ms; a peer status request during another layer’s worker stall took 1.01 ms. Read-only sampling including the controller observed approximately 0.41% machine CPU across 24 logical processors and a 108.5 MiB sampled private-memory peak. Python caller/monitor costs and GPU use are excluded. This was a paced hidden workflow, not a visible-viewer performance benchmark; twenty minutes does not establish weeks of unattended reliability.

The [final audit](../artifacts/durable-layers-20261023-final-audit.json) links each machine-readable result and the resource samples. Binary/source lineage matched 152 PDB documents; one virtual SDK document was unavailable, with no mismatches.

No physical human acceptance is fabricated. Programmatic opening/ownership tests do not prove every physical mouse/menu gesture. Existing Visual Studio performance evidence is retained separately; these durability checks do not establish foreground-native latency or universal application/GPU compatibility. Application crashes, Windows logoff/reboot and loss of both keeper and controller do not preserve unsaved application memory.

## Run and manage a layer

The selected executable and support files are pinned in `artifacts/durable-layers-current.json`. Start the installed-app demonstration:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-layers.ps1"
```

This launches installed Notepad++ with disposable data, edits/saves through VIAVW and opens the viewer in Human control. The launcher prints its descriptor and document, then exits while the application continues. Click the viewer, edit normally, and choose Return to automation when finished. Closing the viewer hides it. Use `scripts/layers.py list`, then `human`, `show`, `hide`, `status`, `recover` or `close` with the printed `--descriptor` to manage it. A pending save prompt opens its Human viewer automatically; resolve it there and retry close afterward. `close --force` explicitly discards that layer's unsaved work.

The existing Visual Studio demonstration remains:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

For integration, use `scripts/viavw_layers.py` and the selected `artifacts/durable-layers-current.json`. Each `LayerHandle` persists its address/session independently of a client connection. Connect for a tool invocation, observe, act against that observation, check actual results, then disconnect. On uncertain delivery, inspect/reconcile rather than retrying input. `show(human=True)` requests human attention without reclaiming ownership on return.

CharlieAI itself remains unchanged at the user's explicit direction. The prepared VIAVW interface is available for its adapter; actual CharlieAI end-to-end integration is a separate task. Older `viavw_client.py` / `viavw_workspace.py` and `demo-collaboration.ps1` still use the preserved legacy lifetime and should not be mistaken for the durable interface.

A private desktop is interaction separation, not a security sandbox. Known installed-app launch profiles are preferable: an application that delegates to an already-running instance needs its own isolated-instance launch configuration. No drivers, privileged system changes, physical input injection, desktop switching or Git operations are part of this implementation.

Metadata contention testing exposed an intermittent controller-start failure under concurrent status readers. Candidate D publishes the controller address once per lifetime, uses atomic metadata replacement with bounded sharing retries, and opens reader handles with delete sharing. Permission denials do not trigger ACL/attribute changes. A controlled test verifies held-reader recovery and twenty replacements during continuous metadata reads, preserving the same unsaved editor buffer. The predecessor B completed 1,012 edit/save cycles and sixteen replacements in twenty minutes; C stopped at cycle 360 on the contention failure and was not selected. D subsequently passed its own complete release gate and was selected. The default `demo-layers.ps1 -Smoke` packaging path also passed after selection.

The existing viewer/API selections remain intact. All owned test application/layer hosts were closed, while generated evidence, source snapshots and the previously policy-blocked browser profiles were retained. Runtime records under `artifacts/layers` belong to live layers while those applications are running. The next useful step is physical human handoff acceptance and an authorized CharlieAI adapter using protocol 2, followed by longer everyday-workflow observation.

A final direct protocol-2 test of installed Visual Studio 2022 also passed six workflow checks: observed real editor selection, Enter/Backspace editing and exact file saving, preserved actual unsaved editor contents and the same IDE process across controller replacement, ordinary Open File navigation verified in the actual editor contents and inspected captured frame, and saved IDE/helper cleanup. This uses the existing disposable experimental software-rendering profile; it does not establish GPU capture or every Visual Studio feature. Its [result](../artifacts/durable-vs-a990f3dbecdd421bb1830b7874019b18/verification.json) is included in the final audit.

Retained durable-development builds, snapshots and evidence occupy approximately 361.8 MiB in the scoped project directories, including earlier candidates. This excludes other research and AppData. The previously reported approximately 322 MiB of policy-blocked browser profiles remain untouched.

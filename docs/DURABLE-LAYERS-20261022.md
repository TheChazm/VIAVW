# Durable application layers — October 22, 2026

Layer 0 is the existing physical Windows session. Each application layer has its own private desktop, application process group, replaceable control workers, viewer and explicit Human/Automation ownership. Several layer owners can run independently. There is no global four-layer or lifetime request limit; capacity is bounded by actual Windows resources and admission checks. A private desktop is interaction separation, not a security sandbox or a separate Windows installation.

## What changed

Protocol 2 separates the application lifetime from the client and control worker lifetimes. A small per-layer keeper retains the application job and desktop handles. Client disconnect only detaches. Worker failure pauses the layer and preserves the application; owner replacement reconnects to the same application process and its existing memory. Application and control workers have separate Windows jobs, so a failed capture worker can be replaced without terminating the application group.

Keeper loss is repaired by the surviving owner, with three failed automatic attempts before attention is required. Owner replacement requires verified death or explicit release of the exact previous owner. Process creation identities and pipe process identities are checked. This protects against accidental PID reuse; it does not defend against a hostile process running as the same Windows user. Simultaneous loss of both owner and keeper, application crashes, logoff, OS failure and reboot do not preserve unsaved application memory.

Unknown input outcomes are never automatically replayed. The layer pauses; the client recovers the worker or owner, captures current state, checks application-specific context, then explicitly reconciles. New input needs the current owner epoch, ownership revision and input sequence. Human ownership is retained across owner replacement; recovery cannot silently reclaim it.

The existing fast Visual Studio viewer and legacy API executables remain preserved. Protocol 1 still has its original owner-exit behavior and should not be used for durable unattended applications.

## Release status

`Durable-20261022v` passed the main installed-app suite: six independent layers, actual Notepad++ editing/saving, 4,200 requests, controller disconnect, injected uncertain mutation, worker recovery with no replay, 265 captures within retention limits, and more than sixteen successive application lifetimes. All owned test processes exited. Another run verified keeper repair and preserved editor contents, but was blocked from starting its peer application by memory admission and is recorded as failed.

`Durable-20261022u` separately passed twelve owner-crash/unsaved-Notepad++ checks and the 33-case legacy Notepad++/Visual Studio regression. Earlier infrastructure and cross-invocation experiments are retained; they are not represented as final-build acceptance.

`Durable-20261022w` adds exact keeper process-creation validation and releases a temporary recovery handle. It builds with zero warnings/errors. Its preserved PDB matches 152 source documents, with one virtual SDK document unavailable and no mismatches. Final application tests and endurance acceptance remain pending when workstation memory admission blocks new applications. The default durable launcher must not be promoted on build success alone.

Actual CharlieAI integration remains outside the authorized edit scope. CharlieAI is unchanged. Programmatic ownership checks do not constitute physical human acceptance. Universal application/GPU/input-device compatibility is not claimed.

## Interface and demonstration

The standard-library Python client is `scripts/viavw_layers.py`; the management command is `scripts/layers.py`. Once a tested `artifacts/durable-layers-current.json` is selected, launch a disposable installed Notepad++ demonstration:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-layers.ps1"
```

Until promotion, explicit candidate testing uses:

```powershell
python "C:\Programming\Projects\VIAVW\scripts\layers.py" demo --host "C:\Programming\Projects\VIAVW\src\LayerHost\bin\Durable-20261022w\net10.0-windows\LayerHost.exe"
```

The command prints the layer descriptor and disposable document path. It edits/saves through VIAVW, opens its viewer in Human control and exits. The application remains alive after the command exits. Click the viewer to type; choose Return to automation to relinquish input. Closing the viewer detaches it from a durable layer; explicitly close the application when finished.

Manage a printed descriptor with the same `--host` until promotion:

```powershell
python "C:\Programming\Projects\VIAVW\scripts\layers.py" list
python "C:\Programming\Projects\VIAVW\scripts\layers.py" status --descriptor "<printed descriptor>"
python "C:\Programming\Projects\VIAVW\scripts\layers.py" human --descriptor "<printed descriptor>"
python "C:\Programming\Projects\VIAVW\scripts\layers.py" hide --descriptor "<printed descriptor>"
python "C:\Programming\Projects\VIAVW\scripts\layers.py" recover --descriptor "<printed descriptor>"
python "C:\Programming\Projects\VIAVW\scripts\layers.py" close --descriptor "<printed descriptor>"
```

`close` asks the application to close gracefully. A pending save prompt keeps it alive. Inspect/resolve the prompt and retry. `close --force` explicitly discards only that layer, including unsaved work. Do not place it in routine automation exception cleanup. `recover` preserves the application and leaves uncertain automation paused; it does not approve continuation. Show the viewer and inspect it, or use a workflow-specific reconciliation predicate.

For integration, construct `Layers()`, call `create(executable, arguments)`, keep the returned `LayerHandle` address/session, and use `with layer.connect() as client:` for each tool invocation. Exiting that context disconnects the client only. `client.observe()` supplies a current observation; `client.act()` binds input to that observation. Inspect actual application/file results before advancing. A single client's requests are serialized, while separate layers operate independently. A layer intentionally permits one connected controlling client at a time.

`LayerHandle.show(human=True, reason=...)` requests human attention and opens the viewer. The user returns ownership in the viewer. `replace_owner()` preserves the management address, changes epoch and requires reconciliation. `adopt()` is for a verified dead owner. `reconcile(context_matches)` captures fresh state and requires your application-specific predicate. Never substitute an unconditional true predicate in an unattended workflow.

## Resource policy and observations

New layers require at least 512 MiB available physical memory and 1 GiB available commit capacity before launch. Replacement control owners use a smaller 128 MiB reserve for each, so recovery does not unnecessarily require the headroom of a new application. These are admission thresholds, not guaranteed future resource limits. Existing applications are retained when new admission is refused. No unrelated applications are terminated and no paging settings are changed.

Each layer retains at most 256 generated observation files / 64 MiB, with a persistent index across owner replacement. Request diagnostics retain two 2 MiB logs. Replacements reuse the original observation/report directory and default owner slot. Research reports, source/binary snapshots and application-created files are preserved separately, so this is not a total project storage cap. Previously policy-blocked browser profiles remain retained.

The verified V run completed 4,200 status requests in 5,257 ms; a peer status request during another layer's stalled worker took 1.76 ms. These are control measurements, not application rendering or input-to-screen latency. Earlier selected viewer measurements remain approximately 22.76 ms median glyph-to-viewer-paint in seven samples; physical scanout/foreground-native equivalence is unverified. Moving work to another thread does not imply lower total CPU use.

## Repeatable checks and outstanding gates

Run suites serially, using the candidate executable as their argument:

- `scripts/test-durable-layers.py`: installed Notepad++/7-Zip, six layers, request/retention limits, worker-fault/no-replay behavior.
- `scripts/test-layer-owner-replacement.py`: real unsaved Notepad++ contents survive owner crashes; ownership preservation.
- `scripts/test-layer-infrastructure.py`: keeper repair, hung owner replacement, independent peer, retention across replacement.
- `scripts/test-layer-client.py`: public factory/registry, stale observations, default owner-slot reuse, actual unsaved contents, handoff and graceful close.
- `scripts/test-layer-endurance.py --host <candidate> --minutes 20`: timed real editing/Backspace/Enter/saving, reconnects, owner replacement and process-resource samples.
- `scripts/layers.py demo --host <candidate> --smoke`: actual demo edit/save/close without physical acceptance.

Outstanding: final-build recovery/public-client tests, measured endurance, physical handoff with this durable lifetime, and actual CharlieAI-side integration. Installed Word/VLC/Visual Studio historical compatibility is useful evidence, not proof that every workflow is accepted on this new lifetime model.

Windows job and desktop semantics informed the experiments: [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [OpenJobObjectW](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-openjobobjectw), [OpenDesktopW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-opendesktopw).

A serial release gate is available as `scripts/verify-durable-release.ps1`. It checks candidate/support integrity, runs every listed durable suite and a minimum twenty-minute endurance test, and selects the default durable release only after all stages pass. It stops on memory admission or any test failure. It does not change the existing Visual Studio or legacy API selection.

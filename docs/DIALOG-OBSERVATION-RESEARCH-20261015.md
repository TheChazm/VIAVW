# Milestone 18 — Ordinary dialogs and bounded visual observation

October 15, 2026. **The qIV viewer now operates an ordinary Windows file picker while retaining its main-window capture session.** It can inspect the dialog, accept independent clicks and typing, open a selected image, and resume native or hidden GPU inspection in the same application. A reusable observation component separately waits for a caller-defined visual condition without replaying input.

## Investigation and implementation

An application layer needs to manage multiple HWNDs with different presentation mechanisms. A GPU-backed main window does not imply that its file picker uses that swap chain. Previously, selecting a modal root invalidated the viewer's experimental GPU binding.

`LayerWindowSelection` distinguishes child parentage from popup ownership, follows bounded owner chains, ignores hidden utility windows, and selects a unique enabled modal descendant when the main window is disabled. Enumeration now uses `GetWindow(GW_OWNER)`: Microsoft documents that [GetParent can return either parent or owner](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getparent), while [GetWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindow) exposes ownership explicitly. Ambiguous selection clears the interactive picture and target.

`DemoForm` retains the original GPU binding while an owned modal uses native full-content capture. Dialog input uses child hit testing and its actual focused control. After dismissal, the same application PID, input owner, reader and mapping remain available. Backend changes and resize controls refuse operation while a dialog is selected. The qIV viewer is larger so the tested picker fits in its canvas.

Targeted messages can reach a disabled window. The worker now checks the destination and its child ancestors are enabled, stopping at [GA_ROOT](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getancestor). This excludes the modal's intentionally disabled owner. Tests reject End input to the disabled main window, confirm its image is unchanged, and continue typing into the enabled filename control. Job and desktop checks still precede input.

Posted F2 can return before the picker exists. A three-second watch after relevant dialog commands enumerates windows at the existing 150 ms timer interval and captures when the selected root changes. It does not repeatedly capture unchanged windows. Hiding preview prevents this observation activity. Discovery, input and capture stay off the UI thread under existing command serialization.

`VisualObservation.WaitAsync` accepts capture, predicate and validation callbacks, a deadline and an inspection interval. It returns match status, the last frame, attempts and timings. Ownership revisions invalidate a wait even if control moves away and back. Cancellation interrupts the interval; the layer gate is released between inspections.

An in-flight capture retains its existing watchdog and can overrun the observation budget. Late matching frames return a non-match. Cancellation discards the result after that bounded call finishes. This internal component neither infers universal readiness nor replays commands; external-agent integration remains future work.

## Compatibility and actual outcomes

The target remains pinned **QuickImageViewer 3.2.0.452**, unchanged on disk, using dedicated project-local configuration. Runtime cursor protection and optional sharing remain explicit instrumentation. No application-specific file-opening adapter, UIA value mutation, clipboard operation or physical-device injection was used.

Direct tests passed F2 opening, native picker capture, cancellation, focused filename typing, a nested missing-file warning, owner-chain selection, ownership rejection, and opening the second generated image in the same GPU source. Picker, warning and resulting pixels were inspected.

Actual viewer tests passed native/GPU startup, pointer cancellation, human-sender F2 reopening, path typing, Enter to open, worker recovery with the picker open, modal backend refusal and delayed inspection after opening with preview hidden. Two viewers held ordinary pickers simultaneously on separate private desktops. Human senders here are automated; **physical human acceptance remains pending**.

Unsuccessful attempts remain retained:

- Initial direct selection chose a hidden qIV utility window and failed PrintWindow. Visibility/ownership selection corrected it: `artifacts/m18-dialog-initial`.
- The first viewer refreshed before F2 created its dialog. Bounded discovery corrected this race: `artifacts/m18-dialog-viewer-initial`.
- A negative test expected a foreign-job error; another private desktop reported the HWND nonexistent. Both refusals are now accepted while the original source is independently confirmed alive: `artifacts/m18-readiness-initial`.
- An idle assertion mistook one already-queued explicit inspection for continuous capture. Before/settled/final versions now allow that completion, then require a stable interval and unchanged GPU copies: `artifacts/qiv-dialog-research-20261015-095110`. This corrected the observation method.

## Measurements

Primary evidence: `artifacts/qiv-dialog-research-20261015-095933`.

| Large-image condition | Transitions | Stale first frames | Median verified ready | Maximum | Inspections |
| --- | ---: | ---: | ---: | ---: | ---: |
| Native, zero added settle | 8 | 8 | 47.2 ms | 59.3 ms | 2 each |
| Hidden GPU sharing | 8 | 8 | 59.2 ms | 202.0 ms | 2 each |

Four 4096×3072 PNGs carry known visual identifiers. Home/End was sent once per transition; all 16 reached the selected pixels. These markers are a research oracle, not a production readiness heuristic. Both backends can correctly capture pixels from the previous application state after successful input.

An unmet 260 ms budget returned after 265.2 ms and five inspections. A deliberately delayed match returned a non-match after 92.5 ms against a 25 ms budget. Interval cancellation finished in 61.8 ms without another capture. Recorded ownership transfer took 0.096 ms and invalidated the prior wait after one capture.

Active large-image phases used **6.97% machine CPU over 0.43 s** and **6.26% over 0.64 s**, including supervisor validation/evidence work. An earlier complete run measured 3.72% and 5.32%. These short, quantized samples do not rank backend efficiency. Source private memory was about 285–341 MiB and reader memory 51 MiB. The generator/supervisor retained 183–185 MiB; that is not viewer memory cost. The original 22% observation remains unexplained.

Tracked surviving child processes used approximately 2.18–3.36% machine CPU per layer during the 3.5–4.3 s dialog workflows; replaced workers and the test supervisor are excluded. After dialog activity, 48 targeted viewer pings returned within 0.32 ms; this does not measure full gesture latency. Idle modal intervals showed unchanged GPU copies. Recorded 640×360 GPU resumption retained one reader device, one resource opening and one staging creation. GPU memory counters describe process allocations, not total GPU utilization.

## Verification, cleanup and recovery

`scripts/test-qiv-dialogs.ps1 -SkipBuild` reproduces five cases with **192 passing assertions, 76 named viewer frames and 16 large-image transitions**. Existing guard/native-startup, GPU viewer, WPF, Qt Quick, mouse state, worker recovery and save-opportunity regressions are indexed in `artifacts/m18-verification-index.json`. Final process, pinned-file, build, storage and snapshot checks are in `artifacts/m18-final-audit.json`.

Accepted M17 sources, working managed executables and native builds were preserved before edits at `artifacts/baselines/accepted-guard-readiness-20261015`, with hashes. Final audit found **zero owned process survivors and zero mismatches across 3,127 files in 16 baselines**, no script parse/build-freshness issues, and unchanged pinned qIV/mpv/accepted-probe hashes. No Git operation, driver, privilege adjustment, desktop switch or system-wide modification was performed.

Browser profiles remain **123 / 3,976.239 MiB**, with no new profiles or deletion attempts. Retained large-image inputs total **32 PNGs / 13.543 MiB** across successful and unsuccessful runs; retained Y4M media totals **68 files / 224.139 MiB**. No cleanup policy was bypassed. Test applications and readers are closed; the working executable remains available.

## Manual demonstration and next direction

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-qiv.ps1 -NativeStartup -SkipBuild
# Optional GPU-startup variant:
.\scripts\demo-qiv.ps1 -SkipBuild
```

Choose **Automation: open file dialog**, then **Human control**. Click the filename field in the picture, type this existing disposable path, and press Enter:

```text
C:\Programming\Projects\VIAVW\artifacts\qiv-dialog-research-20261015-095933\gpu-viewer\images\02-alternate.png
```

The second image should appear. Return to automation; picture typing should be ignored and **Automation: next image** should work. Switch backends with the picker closed. Close the viewer to release its layer. This physical verification remains pending.

The evidence supports capture selection per application window with a shared lifecycle and input owner. It does not establish a composed desktop, arbitrary root replacement, raw-input emulation, general modeless-window selection or universal dialog capture. Nested warnings were tested directly; their full physical viewer workflow remains unaccepted.

Next, test an ordinary application with multiple document windows and native menus, including explicit window choice, dialog keyboard navigation and DPI-dependent pointer coordinates, while retaining the private-desktop backends.

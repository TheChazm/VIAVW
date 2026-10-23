# Viewer responsiveness and editing keys

This records the M27 measurements. The later [asynchronous preview repair](ASYNC-PREVIEW-20261016.md) fixes stale New Project content and adds a low-rate live picture while Human views the application; Automation/hidden preview can still remain quiet.

The installed Visual Studio viewer now defaults to a faster interaction path. Backspace is delivered as an editing key and is verified against the actual IDE document. This improves the existing private-desktop implementation; it does not establish foreground-session performance equivalence or resolve native GPU capture.

## Findings and changes

The demo previously enabled research diagnostics during ordinary use. Each captured frame was followed by a full accessibility traversal, including document/selection reads, while holding the same serialized command gate as input. Shortcut/pointer traces also enumerated windows and wrote evidence. The approximately 40 ms capture figure therefore substantially understated the delay before the viewer could accept its next command.

The default VS path now:

- Captures and publishes lightweight frame metadata without a full accessibility scan or input trace. **Refresh** updates the picture; **Inspect details** explicitly collects the slower read-only accessibility snapshot. `-Diagnostics` restores the previous continuous evidence path when needed.
- Discovers owned window geometry and classes without sending `WM_GETTEXT` to every child. Top-level titles use the cross-process stored caption. Full text enumeration remains available to existing research callers. [Microsoft documents this distinction](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowtextw).
- Checks pending refreshes every 25 ms rather than 150 ms. Input and capture remain serialized off the UI thread. It coalesces pending frames and retains bounded window/menu watches; it does not continuously capture at idle.
- Publishes a bounded startup burst, because the shell window appears before asynchronous solution loading finishes. The burst ends after five seconds, followed by a bounded window/title watch.
- Reads current text-control geometry in bulk at a left-button interaction, rather than rereading document contents or relying on stale editor rectangles after layout changes. This uses read-only UIA property caching, [as documented by Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/caching-in-ui-automation-clients). It does not set focus, selection or values through UIA. Worker deadlines still contain provider hangs.

Backspace previously reached VS only as `WM_CHAR '\b'`, which did not delete the typed text in the reproduced case. The viewer now forwards `VK_BACK` down/up and suppresses its own translated character to avoid duplicate deletion. Ctrl+Backspace uses the existing private modifier-state path. The existing keyboard, mouse capture, popup composition and explicit ownership policies remain in use.

## Measurements

Same installed VS 2022 version, separate disposable solutions, retained software-rendering experimental profile, maximized viewer, all source/viewer windows on private QA desktops. The preserved M26 assembly was copied unchanged to an equivalent binary-directory depth so its project-relative validation still applies. Its SHA256 remains `30B0EC1C62F0A5B6D5E6EEF9E94DF61BCB8E1E4455FD013DB732E5D983EFAEDE`.

| Measurement | Preserved M26 | Revised default |
|---|---:|---:|
| Eight single-character frame-report samples | 827–1,013 ms | 91–124 ms |
| Median of those samples | 963 ms | 95 ms |
| 24 characters submitted at 40 chars/sec, until final frame report | 1,770 ms | 868 ms |
| Single Backspace | Did not delete | 87 ms; exactly one character removed |
| Eight repeated Backspaces, including paced submission | Unsupported deletion | 404 ms; exact result |
| Ctrl+Backspace | Not measured | 309 ms; correct word removed |
| Owned tree CPU time during the same typing workload | 9,109 ms | 2,500 ms |

The median frame-report latency improved about tenfold. Earlier revised runs ranged from 80–125 ms. Timing starts before the outer private viewer input submission and ends at a newer frame report containing the forwarding sequence. This includes queueing, capture and publication; it is **not physical input-to-monitor latency**. Actual document contents, saved files and screenshots are checked separately. Eight samples per run and short active-work comparisons are useful evidence, not a comprehensive benchmark.

The revised active phase took 2.17 seconds rather than 9.80 seconds. Average CPU over its shorter active interval was higher (1.15 cores versus 0.93), despite lower total CPU time. Moving work off the UI thread alone would not establish a CPU reduction. Owned-tree summed peak working sets were about 2.87 GiB revised and 2.82 GiB baseline, including the IDE/helpers; summing individual peaks does not describe a simultaneous peak. GPU utilization was not newly measured.

The final revised shell-frame report arrived in 1.72 seconds and a report with the document title in 3.09 seconds. These are warm-machine observations and **not** a foreground launch comparison or proof of completed project loading. In a two-second idle sample after startup/watch deadlines, the viewer recorded zero CPU milliseconds at process-timer resolution, with no additional captured frames.

## Verification and retained failures

`VisualStudioResponsivenessVerification` exercises the actual viewer canvas: ordered paced typing, single/repeated/word/selection Backspace, forward Delete, exact Ctrl+S persistence, rejected typing/deletion under Automation, and same-process ownership handoff. The installed Notepad++ viewer regression saves the typed suffix before deleting it, so the test cannot pass merely by reading a stale saved file. The full VS workflow also deletes an extra filename character in the ordinary Open File dialog before opening the file.

Evidence:

- Baseline: `artifacts/vs-m27-baseline-measured/responsiveness.json`. Its completed benchmark records the Backspace failure explicitly; `requireDeletion=false` permits observing that negative result.
- Final performance/deletion run: `artifacts/vs-responsiveness-20261016-173406/viewer/responsiveness.json`.
- Full mouse/menu, file, build and handoff evidence is listed in `artifacts/m27-verification-index.json`, together with regressions, baseline integrity and process cleanup.

The first revised full workflow stopped at a nested submenu observation. Its saved viewer image already shows the submenu. The test incorrectly required a full accessibility snapshot to share every automatic frame version after scans became on-demand. The observer now requests fresh passive evidence as deferred UI arrives, without replaying the input. The failed run remains under `artifacts/visual-studio-verification-20261016-171752`. Initial benchmark attempts also retain project-path validation/startup failures; no successful performance claim comes from them.

A later full run (`artifacts/vs-m27-final-mouse`) entered the file picker with an incorrect shortened filename after its deletion test. The exact cause of that intermittent result was not isolated. The subsequent controlled workflow (`artifacts/vs-m27-checked`) records exact typed filename values, presses End, verifies collapsed selection, then verifies exactly one deleted suffix before opening each file. It passed the complete workflow. This qualifies the filename test; it does not prove the earlier failure was caused by autocomplete. Editor deletion passed independently without that extra End step.

Broader Notepad++ regressions exposed partial selection/replacement failures: two concurrent runs failed their initial replacement, and a later full run failed a selected-line edit. Adding wrapper forwarding waits alone did not eliminate those failures. An isolated concurrent pair passed, including exact Backspace saves in both independent editors; the preserved M26 ordinary-workflow control also passed. The final build passed a targeted repeat of all 31 ordinary document checks and a concurrent pair, including saved Backspace deletion in both viewers. These outcomes do not isolate the earlier cause or establish a clean full-suite regression pass. The retained failures, final targeted checks and cleanup are enumerated in the verification index. qIV's repaired GPU-startup workflow passed, but the later native-startup run retained an extra hidden modeless choice; its other functional checks and cleanup passed, and the script stopped before the concurrent case. These broader results remain qualified.

Physical human acceptance of this revision is pending. Read-only desktop monitoring checks owned processes never become foreground, desktop/confinement stability, and pointer/focus equality when physical input is quiet. Where user input occurs, it does not attribute that activity to the experiment.

Broader capture verification reproduced a snapshot replacement race in both the revised host and the unchanged M26 binary (`artifacts/vs-m27-publishing-baseline-control`). Windows reported error 1175 while shared readers were active, but the later sharing probe could find no remaining lock. The snapshot writer now retries that same atomic operation within its existing five-retry limit (100 ms total retry delay, excluding filesystem duration). [Microsoft specifies that error 1175 retains both original filenames](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew). Persistent sharing failures still stop with the original intact, and unrelated filesystem errors still propagate. No permissions, attributes or protected profiles are altered. This repair also addresses report-publishing stalls observed in the qIV viewer regression.

## Run and remaining work

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
& "C:\Programming\Projects\VIAVW\scripts\test-visualstudio-responsiveness.ps1" -SkipBuild
```

Close an older viewer first so the rebuilt executable is used. Choose **Human control**, type in the code editor and try Backspace, holding Backspace, Ctrl+Backspace and selection deletion. Test menus, document tabs and scrolling. **Return to automation** should reject picture input while retaining the same application. **Inspect details** intentionally pauses input briefly while collecting its diagnostic snapshot. Existing IDE sessions are refused rather than reused.

Native `PrintWindow` rendering, PNG encoding/decoding, polling and fixed shortcut/menu state-settling delays still add latency. The next focused performance experiment should compare reusable bitmap/shared-memory presentation against PNG transport, with frame freshness, lifecycle recovery and idle behavior verified. Foreground-equivalent fluidity, general drag/drop/docking, complete device input semantics and native VS GPU capture remain unproven. No drivers, global interception, security bypasses or Git operations were used. Retained profiles were not deleted.

The final release build has no warnings/errors. The final audit found no surviving owned application/wrapper processes, no changed files in the accepted M26 snapshot, no script parse errors and no sources newer than the executable. The original Notepad++ PID 40008 remains present. This run retained about 79.3 MiB of evidence and the existing VS experimental profile totals about 140.8 MiB; no browser profiles were created or deleted. `artifacts/m27-snapshot.json` identifies the new source/executable snapshot and SHA256 manifest.

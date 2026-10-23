# Milestone 16 — Choosing and switching capture backends

October 15, 2026. **qIV can supply fresh native captures through temporary private presentation, without retaining a GPU reader or active Present interception.** The viewer now switches between that path and hidden GPU sharing while preserving the same running application, image state and input owner. Both private-desktop implementations remain available.

This is verified for the pinned ordinary QuickImageViewer 3.2.0.452 executable and its two disposable test images. The disk executable remains unchanged. A process-local cursor guard remains necessary in both modes; application transparency is therefore still scoped. Physical human acceptance remains pending.

## Investigation and evidence

Milestone 15 found correct native full-content pixels when qIV was shown privately, but blank pixels while hidden. That left two questions: whether native inspection could observe a **new** image promptly, and whether prior GPU sharing had made native capture work.

The new experiment selects a different image with real targeted wheel input before every measured capture. It verifies mutually exclusive red/magenta image identities and actual 640×360 dimensions. Eight rotating rounds compare hidden GPU sharing with native show/capture/hide at 0, 10, 30 and 100 ms added settle. Native/GPU/GPU/native paced blocks then compare the retained paths. Subsequent blocks stop interception and close the reader. A separate fresh process stops the adapter **before any shared frame is requested or copied**.

Final evidence: `artifacts/qiv-capture-comparison-20261015-025033`. All **238 measured input/capture transactions** returned the expected image on their first attempt: 155 in the main comparison and 83 in the fresh native-only experiment. Initial image readiness is checked separately and may retry inspection; input is never replayed. First-attempt failures would remain recorded even after inspection-only resynchronization. None occurred in the final run.

All tested settle intervals worked for these small preloaded images. Zero added delay was sufficient in this workload; it is not a general frame-ready guarantee for cold decoding or arbitrary applications. Microsoft describes [PrintWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-printwindow) as a synchronous operation handled by the source application, which supports keeping it off the viewer UI thread. API success alone does not establish current application pixels. [DwmFlush](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush) documents synchronization of the caller's queued graphics work; we did not treat it as an acknowledgement of another application's image selection.

## Implemented behavior

The existing qIV demo gains two explicit controls:

- **Use native inspection** stops the capture adapter, restores its Present callbacks and closes the owned GPU reader. Inspection temporarily shows the source only on its non-input private desktop, obtains full-content native pixels, and restores its prior visibility. The known demo uses zero added settle.
- **Use hidden GPU capture** creates a new owned reader and reinstalls the accepted sharing backend. Source application state and ownership remain intact. Recreating resources carries the existing first-capture initialization cost.

Input remains serialized through the existing ownership gate. Native mode preserved human Ctrl-wheel zoom, buffered image panning, rejection of non-owner input, worker recovery and return to automation. Hiding the preview suppresses inspection and does not create a reader. Native mode asks the user to select GPU capture before using the existing GPU resize controls or reader recovery.

The actual viewer passed repeated switches in both directions. Concurrent tests held different images simultaneously, with one layer using native inspection and the other hidden GPU sharing. A capture switch does not restart the application or grant input ownership. Failure to create the replacement GPU session leaves the native option selected; errors remain visible.

`DemoForm` implements the explicit switch and reports its current backend; `GpuBridgeResearchSession` retains startup diagnostics. New `QivCaptureComparison` and the comparison script collect direct timing, readiness, resource and cleanup evidence. `QivViewerVerification` exercises switches through actual controls. `ModuleSnapshot.h` supplies the bounded helper retry, with a separate native fault-test executable. Existing native capture, ownership and process-job mechanisms are reused.

The native option retains the exact-process cursor guard, denying its imported ClipCursor calls until that owned source exits. SetCursorPos protection is conditional; Milestone 17 found no named import slot for it in this release. The fresh native experiment still uses the existing restricted adapter initialization to install this guard, briefly creating initialization graphics resources before stopping interception. It is not a completely instrumentation-free startup path. No application remote protocol, UIA mutation, physical input injection, driver or system-wide setting change was introduced.

## Performance and resources

These are complete input/capture transactions until PNG bytes reach supervisor memory, including input delivery, native show/settle/hide where applicable and IPC. GPU transport writes and reads a PNG file; native transport returns PNG bytes over RPC. Validation and evidence-file writing are outside each transaction timer. The results compare the implemented paths, not isolated API throughput.

| Final workload | Captures | Median | Range |
| --- | ---: | ---: | ---: |
| Paired native presentation, zero added settle | 32 | 12.35 ms | 9.42–20.46 ms |
| Paired retained hidden GPU sharing | 32 | 19.97 ms | 6.32–21.32 ms |
| Fresh native-only source, paced block | 16 | 13.64 ms | 9.97–19.41 ms |

The GPU reader held approximately **49.75 MiB private memory** before native-only work; its process exited when selected out. This is an observed reader footprint, not an exact estimate of total physical memory saved. Native worker GDI objects remained at three across the measured blocks; source GDI objects remained at 42. Snapshot handle lifetime was also checked separately.

Child-process CPU across paired blocks ranged approximately **0.21–0.33% machine CPU for GPU** and **0.25–0.28% for native** in this run. The native-only block measured about **0.15%**. Samples are short, paced and coarse; earlier runs varied, so there is no established general CPU advantage. The separately recorded supervisor CPU includes validation and counter-sampling overhead and is not production viewer CPU. Individual GPU-engine observations are retained without summing them into whole-device utilization. The original 22% utilization remains unexplained.

## Startup failure and correction

The first mpv regression stopped one concurrent viewer at startup. Its status recorded `0x80070018 / ERROR_BAD_LENGTH` during module discovery; the failed run remains at `artifacts/gpu-viewer-20261015-024446`. This was a startup failure, not a physical pointer-equality failure.

[Microsoft documents](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot) that a changing module list can cause this snapshot error and recommends retrying it. The helper now retries **only this read-only snapshot error**, for at most approximately 500 ms per snapshot. Other errors remain immediate. It does not repeat DLL loading or application input and preserves the existing path, hash, PID-creation and desktop checks. Startup diagnostics record retry counts.

The isolated fault tests inject three transient errors before a real snapshot, immediate access denial, and persistent failure. All passed; persistent failure stopped in **512.8 ms**, and 64 real snapshots left handle counts unchanged. The complete mpv GPU-viewer regression then passed, including concurrent startup, a stalled reader, recovery, duplicate refusal and real launcher cleanup. This does not prove the Windows race can never recur or exhaust the bounded retry.

## Verification and working demonstration

The new suite passed **165 application assertions**, **88 named actual-viewer frames**, the 238 measured direct captures and four snapshot-policy checks. The accepted qIV suite also passed during regression. Additional final regression results and cleanup are indexed in `artifacts/m16-verification-index.json`; audit results are in `artifacts/m16-final-audit.json`.

Final checks also passed for the accepted D3D fixture bridge, the mpv/dynamic GPU viewers and launchers, WPF viewers in three rendering modes, Qt Quick GPU viewer, native mouse state, input-worker recovery and save-opportunity behavior. The qIV regression was run before the final snapshot-retry addition; the full new qIV switch suite and mpv suite were rerun on the final helper builds.

Both final direct comparison runs observed unchanged foreground, pointer, cursor confinement and input desktop during quiet input intervals; no owned process was observed as the physical foreground. These sampled observations and automated human senders do not constitute physical human acceptance. Milestone 15's older pointer/focus equality failures remain retained and are not reclassified by this result.

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-qiv.ps1 -SkipBuild
# Reproduce comparisons, fault checks and viewer switching:
.\scripts\test-qiv-capture-comparison.ps1 -SkipBuild
```

Select **Use native inspection**, change images, then take **Human control**, Ctrl-wheel and drag the picture. Select **Use hidden GPU capture** and confirm that the same image, zoom, pan and owner remain. Return to automation to reject picture input. Try hiding the preview, changing the image and inspecting later. Native inspection may briefly present the source on its private desktop; GPU sharing keeps its source hidden throughout capture. Close normally when finished. Physical verification of this experience is pending.

The accepted M15 code, working managed binaries and native helper builds were copied first to `artifacts/baselines/accepted-ordinary-d2d-20261015`, with a hash manifest. No Git operations were performed. Disposable browser profiles remain retained without deletion attempts. The final audit records process cleanup and storage totals.

The audit found **zero owned process survivors**, **zero mismatches across 2,712 files in 14 saved baselines**, no script parse errors and no managed/native sources newer than the tested builds. Pinned qIV, mpv and accepted D3DProbe hashes match. Browser profiles remain **123 / 3,976.239 MiB**, with no new profiles. Retained media now totals **50 Y4M files / 164.808 MiB**, an increase of 29.665 MiB; the portable qIV and mpv packages remain 10.747 MiB and 90.467 MiB. No deletion policy was bypassed.

## Architectural conclusion and next experiment

The evidence supports **explicit per-application capture choices and state-preserving switching**. Native private presentation is useful for this qIV workload; retained sharing provides a different capability when native hidden pixels are unavailable or presentation should be avoided. No universal backend or automatic compatibility classifier is justified yet.

Remaining limits include two small cached test images, unscaled previews, GPU-only resize controls, separate dialogs/menus outside the selected client surface, and untested raw input, IME, GPU migration and real device removal. A useful next experiment is separating cursor protection from GPU initialization, then testing larger uncached documents and extra application windows. That would challenge the current readiness assumptions while keeping this working demonstration intact.

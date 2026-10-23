# Milestone 15 — Ordinary Direct2D application and stateful mouse input

Recorded October 15, 2026. This milestone extends the accepted private-desktop GPU viewer to a second ordinary application: **QuickImageViewer (qIV) 3.2.0.452**. Hidden GPU inspection, image navigation, modifier-wheel zoom, held mouse interactions, ownership, recovery and two independent application layers passed automated verification. The accepted mpv viewer and default capture paths remain available.

The release executable is unchanged on disk. Its running process is instrumented for capture and cursor protection. This is a demonstrated compatibility option for one pinned release, not universal transparent input or virtual-device emulation. Physical human acceptance remains pending.

## Investigation and containment

qIV provides a useful contrast with mpv: a native Direct2D/WIC image renderer using a hardware D3D11 swap chain, with wheel navigation, Ctrl-wheel zoom and stateful mouse gestures. The [first-party release](https://github.com/icyhoty2k/QuickImageViewer/releases/tag/v3.2.0.452) supplies the executable. Its 10,262,528 bytes have SHA256:

```text
852F10EFFE6A99C96B73EFD1720C56749C8AAA659C7057DA72F9D833AA0E5CD4
```

The executable, metadata, hash, license and selected audited source files are retained under `.deps/qiv-3.2.0.452`. Setup downloads and validates the executable; it does not install or launch it.

Source inspection revealed two important boundaries:

- Ordinary portable/file-backed operation still performs shell registration. The built-in **dedicated instance** mode skips those paths. Each experiment therefore receives a pre-created project-local UTF-16 INI with `Dedicated=1`, a unique instance name and mutex. Normal mode was never launched. See the original [registry manager](https://github.com/icyhoty2k/QuickImageViewer/blob/v3.2.0.452/src/Persistence/RegistryManager.cpp) and [dedicated settings](https://github.com/icyhoty2k/QuickImageViewer/blob/v3.2.0.452/src/Dedicated/DedicatedSettings.cpp).
- Its [mouse handler](https://github.com/icyhoty2k/QuickImageViewer/blob/v3.2.0.452/src/Input/MouseHandler.cpp) requests cursor confinement. Windows documents [ClipCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor) as affecting the shared cursor resource; [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos) requires the input desktop. We did not test real confinement against the physical desktop. Before mouse experiments, the qIV-specific DLL replaces its named ClipCursor import with access-denied responses and conditionally handles a SetCursorPos import if present. Milestone 17 found one named ClipCursor slot and no named SetCursorPos slot in this pinned release; cursor warping was not demonstrated.

The guard recorded three denied confinement calls and zero reposition requests in the final native run. It remains resident after capture stops, until the owned application exits; stopping inspection must not re-enable unsafe cursor requests. It does not simulate cursor confinement. Targeted registry snapshots were unchanged. Startup, display-awake, history, context-menu and clipboard-copy options are disabled in disposable configuration; no remote/listener feature was enabled.

## Demonstrated behavior

All source windows run on non-input private Windows desktops. Actual image colors, dimensions and fiducial movement determine success, rather than successful API returns alone.

| Capability | Observed result |
| --- | --- |
| Hidden hardware capture | Real Direct2D image pixels from the NVIDIA GeForce RTX 4060 Ti; software-adapter flag false |
| Native wheel input | Alternates two independently identifiable image files |
| Ctrl-wheel | Enlarges the rendered image using temporary private queue modifier state |
| Held mouse interactions | Image pans while a button is held; click zoom persists while held and resets on release |
| Resizing | Actual captures at 480×270, 960×540 and restored 640×360 |
| Ownership | Automated human sender succeeds under human ownership; rejected after return to automation |
| Recovery | Reader and input-worker replacement preserve the same source process and owner |
| Two layers | Different source PIDs simultaneously retain different images; closing one preserves the other |
| Launcher | Visible startup, duplicate refusal, reopen, wrong-target refusal and owned cleanup |

The actual viewer uses its toolbar and preview controls in the tests. Wheel modifiers are now read on the UI thread before asynchronous dispatch. For qIV, the input path lets the application acquire mouse capture itself. Worker-side capture priming cancelled qIV's drag via its capture-change handler; retaining the existing priming behavior for other backends preserved their compatibility. Viewer drags remain buffered until release.

### Native capture comparison

The same ready image was also captured through existing native backends in the instrumented session:

| Source state | Native capture | Measured time | Image result |
| --- | --- | ---: | --- |
| Hidden | PrintWindow / client only | 3.35 ms | Blank |
| Hidden | PrintWindow / full content | 12.68 ms | Blank |
| Shown on private desktop | PrintWindow / client only | 3.63 ms | Blank |
| Shown on private desktop | PrintWindow / full content | 6.09 ms | Correct image |

Thus GPU sharing supplies a demonstrated **hidden** capture benefit. This comparison does not establish that qIV always needs instrumentation for capture, nor that sharing is faster than temporary private presentation. The shown-native timing excludes a full show/settle/hide transaction. The bridge and cursor guard were already installed during these controls.

## Implementation

New `setup-qiv-research.ps1`, `demo-qiv.ps1` and `test-qiv-research.ps1` scripts provide the pinned setup, visible demonstration and repeatable suite. `build-gpu-research.ps1 -Qiv` produces a separate helper/DLL pair under `artifacts/gpu-qiv-build`. The helper accepts only the owned, hash-checked qIV executable on its validated private desktop. Existing fixture and mpv variants retain separate outputs.

`QivResearch` creates two small disposable PNGs and dedicated configuration. Native, actual-viewer, concurrent-viewer and launcher harnesses retain pixel evidence and cleanup results. No qIV remote protocol, semantic input adapter or asynchronous-key shim is used. Native input remains serialized through the existing ownership gate.

The GPU reader reports its matching adapter name and software flag. It retains device/readback resources between captures, reopening shared resources on resize. A reader restart intentionally creates new resources. Cursor guard telemetry was appended without moving existing protocol fields.

## Performance

Final evidence: `artifacts/qiv-research-20261015-015602`; derived measurements: `artifacts/m15-qiv-performance.json`.

- First hidden capture: **155.49 ms** inside the helper, **172.27 ms** including managed coordination.
- Sixteen paced, changed-image captures: helper median **16.37 ms**, range **3.55–17.52 ms**; median including managed coordination **23.27 ms**.
- Sixteen real wheel/capture cycles with 100 ms pacing took **2.075 s**. Owned source, worker, reader and console processes used **312.5 ms CPU**, about **0.63% of this 24-logical-processor machine** over that interval. Supervising verifier/viewer CPU is excluded; this is not a maximum-throughput benchmark.
- Private memory at that point: source **98.84 MiB**, reader **52.87 MiB**, worker **22.18 MiB**. DXGI local-memory accounting was approximately **26–35 MiB** for the source and **7.89 MiB** for the 640×360 reader; these are not unique physical allocations.
- Separate 1.5-second engine samples recorded source 3D **0.020%**, source copy **0.00037%**, reader 3D **0.063%**. These individual engine values are not a whole-GPU utilization measurement.
- During **1.706 s** hidden without inspection, copy and Present counters remained fixed and measured child CPU was zero at the counter resolution. This short observation does not prove indefinite zero activity. The original reported 22% utilization remains unexplained.

## Verification, failures and recovery

The final qIV suite passed **117 assertions and 71 named GPU frames**, with three actual-viewer screenshots. All four suite operations exited zero. The accepted GPU-viewer suite also passed. Targeted WPF viewer/gesture tests in three rendering modes, Qt Quick GPU viewer, native mouse-state, worker recovery and save-opportunity tests passed.

Five older reports retain failures in their final strict physical pointer/focus equality checks: native isolation, fixture bridge, dynamic GPU, concurrent bridge and accessibility. Their functional assertions and cleanup passed; accessibility also recorded different foreground focus. Input activity was recorded during this work, but its origin is not established. These reports remain failures; they do not demonstrate physical equivalence. The new qIV tests separately report pointer/focus equality, require unchanged confinement and input desktop, reject an owned foreground source, and require pointer equality during quiet input intervals. Their automated human sender is not physical acceptance.

Earlier unsuccessful experiments remain available: wrong mouse configuration moved the private source window; capture priming cancelled pan; a misplaced conditional in the new helper suppressed resizing. Pixel-dimension assertions exposed the resize error after earlier color-only checks had missed it. These were corrected and rerun. Readiness checks now retry inspection only, without replaying input. They are prototype failures, not Windows limits.

The accepted M14 implementation was copied before changes to `artifacts/baselines/accepted-gpu-viewer-20261015`. Final audit and the complete evidence index are `artifacts/m15-final-audit.json` and `artifacts/m15-verification-index.json`. No Git operations, drivers, desktop switching, physical input injection or persistent system-wide changes were performed. Browser profiles were retained without deletion attempts.

The audit found **zero owned process survivors**, **zero mismatches across 2,519 saved files in 13 baselines**, no PowerShell parse errors and no managed sources newer than the tested build. Pinned qIV, mpv and accepted D3DProbe hashes match. Retained browser profiles remain **123 / 3,976.239 MiB**, with no new profiles this milestone. Other retained packages include qIV **10.747 MiB**, mpv **90.467 MiB**, and 41 disposable Y4M files totaling **135.143 MiB**. No profile cleanup policy was bypassed.

## Demonstration and next experiment

From the project directory:

```powershell
.\scripts\demo-qiv.ps1 -SkipBuild
# Reproduce automated verification:
.\scripts\test-qiv-research.ps1 -SkipBuild
```

Use **Automation: next image**, **zoom in** and **reset zoom**. Take **Human control**, place the pointer over the preview, use Ctrl-wheel and drag the image, then **Return to automation** and verify preview input is rejected. Try **Hide preview**, **Inspect hidden app**, reader/worker recovery and resizing. Keep the preview visible when assessing image changes. Close the viewer normally; **Force close** is available for owned processes. A second explicit demonstration can use `-AllowDuplicate`.

Manual acceptance is pending. The preview is unscaled and large source sizes can be clipped. Capture covers the selected GPU client surface; separate dialogs and menus are not composed. Root replacement, raw input, IME, GPU migration and real TDR/device removal remain unverified.

The next useful experiment is a controlled comparison of **temporary private presentation plus native full-content capture** against **retained hidden GPU sharing**, including changing images, full transaction latency and rendering cost. This finding supports choosing capture per application and visibility need. A separate window station may offer stronger structural separation of shared interaction resources, but has not been implemented or claimed here.

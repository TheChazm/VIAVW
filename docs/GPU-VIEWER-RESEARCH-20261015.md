# VIAVW — Milestone 14: GPU capture in the private viewer

The ordinary portable mpv GPU experiment now runs through the existing multithreaded viewer. Its native window stays hidden on its private desktop during inspection, frame stepping, playback, resize and recovery. The viewer remains responsive during a deliberately stalled GPU reader. Automated ownership handoff preserves the same application and paused media frame. Two complete viewers also passed concurrently with distinct paused frames.

This is an explicit experimental backend for two exact, owned, hash-checked research executables. mpv remains unchanged on disk; its running process is instrumented for capture. Its input uses native Windows messages. The separate D3D fixture additionally enables the previously tested process-local asynchronous modifier shim. This does not establish arbitrary application compatibility, full virtual input devices, or physical human acceptance.

## Foundation and implementation

Before editing, the accepted Milestone 13 source, scripts, documentation and working native/managed binaries were copied to `artifacts/baselines/accepted-dynamic-gpu-20261015`. Its SHA-256 manifest covers 248 files. Existing snapshots remain preserved. No Git operation was performed.

`DemoSession` and `scripts/demo.ps1` accept `--experimental-gpu-bridge mpv|dynamic` / `-ExperimentalGpuBridge`. The default remains native capture. Incompatible rendering-adapter combinations are rejected. The existing GPU session checks the selected executable path/hash, owned source identity and root window before instrumentation; an ordinary LayerProbe executable deliberately selected as mpv was rejected and cleaned up.

`DemoForm` retains one GPU session and reader per layer. Launch, install, capture, resize, recovery and adapter shutdown use its existing asynchronous command semaphore and the layer transaction gate. Graphics commands and native input stay serialized. Captures reuse the reader device and surfaces, replacing size-dependent resources after resize. Inspection never temporarily shows the GPU source window. A single working capture file is overwritten; QA separately retains named verified images.

The experimental toolbar adds frame-step/play-pause controls for mpv, bounded source sizes, and reader/input-worker recovery. Space and function keys reach the source through native key messages. For the native fixture only, viewer chords use its scoped async-state adapter. The ordinary mpv process has no input shim. Attention requests and force-close waits also run off the UI thread. Graceful close stops the adapter/reader before asking the source to close; if a source remains at a save prompt, the viewer retains the existing native capture path.

mpv's media thread can publish a frame after its input handler returns. The experimental mpv path therefore coalesces one preview refresh after a 250 ms delay. It does not continuously refresh during playback. This delay suits the disposable 5 fps media and is not a universal frame-ready signal. The verifier checks real image-encoded frame numbers, including the scheduled previews after automation and simulated human input.

## Verification and findings

The complete reproducible GPU viewer suite is `artifacts/gpu-viewer-20261015-005136`:

| Experiment | Result |
|---|---|
| Native dynamic D3D viewer | 10 checks: hidden GPU pixels, Space, fixture-only scoped Ctrl+K, F5 resize, recovery, ownership and cleanup |
| Ordinary mpv viewer | 34 checks: native stepping/playback, scheduled previews, wrong-owner rejection, hidden inspection, resize, timeout, recovery and cleanup |
| Two ordinary mpv viewers | 34 + 35 checks; independent source PIDs/desktops, different paused media frames, separate ownership, resize and stalled-reader recovery |
| Real PowerShell launcher | 7 checks: visible startup, duplicate refusal, reopen, graceful and force close, and rejection of an unrelated executable |

There are 120 checks across these reports, 78 named GPU verification frames, and three captures of the actual mpv viewer UI. Working capture/preview files account for the remaining PNGs. The concurrent aggregate report also confirms different paused frames and cleanup. Logs identify the actual mpv D3D11 renderer as NVIDIA GeForce RTX 4060 Ti, feature level 12_1.

Early harness runs exposed a non-normalized path comparison, an invalid cross-desktop WM_NULL probe, and a disposed JSON element used during report serialization. Evidence is retained in `gpu-viewer-20261015-004601`, `gpu-viewer-20261015-004635`, and `m14-mpv-launcher`. The fixes normalize the expected helper path, send read-only responsiveness probes through the owned private-desktop worker, and clone JSON before its document is disposed. The initial launcher harness failed before writing its detailed report; its outer failure/cleanup result remains. These were verification defects, and are not claimed as GPU capture failures.

## Resource observations

Measurements below come from the final suite's isolated single-viewer run. They are short activity samples, not throughput guarantees:

- Six playing-frame captures at 640 × 360: approximately **22.7 ms median**, **20.3–67.6 ms range**, including helper capture/PNG output and reading the PNG into the viewer process. UI image decode, publication and test polling add time; the pixel-verification round trips were about 94–97 ms.
- The same six captures with playback and pacing lasted **1.682 s**. All nested QA/viewer/source infrastructure accumulated **171.875 ms CPU**, about **0.43% of the 24-logical-processor machine**. The supervising verifier is outside that process sum. These are coarse process CPU counters; individual zero readings do not establish zero work.
- The paused, hidden-preview 1.502 s sample accumulated no process CPU time at the counter's resolution. No GPU copy occurred during that interval or the separate 0.9 s hidden-playback interval. Hiding the preview does not promise to stop the application's own rendering.
- During playback, private memory was about **112.5 MiB for mpv**, **53.7 MiB for the GPU reader**, and **26.8 MiB for the viewer**. All nested QA/viewer/source processes totalled about 241.5 MiB. The extra outer QA worker is test infrastructure.
- The retained reader reported one device creation and four surface openings after the initial surface and three source-size changes. Its process-local GPU usage was about **7.89 MiB**; source usage varied around **14.2–18.2 MiB** during this interval. Shared resource/driver accounting is not unique physical memory and does not prove absence of leaks.
- Suspending only the identified owned reader's main thread triggered containment after **5.065 s**. Twelve validated UI probes during the stall took **0.18–3.03 ms**. The source survived; the recovery button created a new reader and preserved its paused frame and ownership. Concurrent runs passed the same fault checks independently.

Raw metrics are in `artifacts/m14-performance-summary.json` and the per-viewer reports. Moving work off the UI thread preserves responsiveness; it does not itself reduce total CPU consumption.
The earlier approximately 22% CPU observation remains unexplained; these measurements do not establish its cause or a fix.

## Baseline regression observations

The broader regression effort retained four failures and corrected their verification assumptions:

- `development-20261015-005225`: the Qt viewer's fixed wait read `shortcuts=0`; the later resumed observation read `shortcuts=1`. An unchanged isolated retry passed in `m14-quick-viewer-retry`. `QuickViewerVerification` now waits for actual selection, replacement text and shortcut telemetry before transitioning ownership.
- `development-20261015-005452`, also reproduced in `m14-wpf-hook-retry`: the opted-in WPF software adapter was applied and source telemetry reported the click, but the first capture still contained the earlier green marker. `WpfHookVerification` now polls pixels for at most 2.5 seconds without replaying input. The first image remains saved. The corrected isolated run `m14-wpf-hook-pixel-readiness` obtained orange pixels on capture two; negative rendering controls stayed negative.
- `development-m14-continuation-20261015-005729`: private Chrome presentation/input checks passed, then optional memory sampling threw because an owned browser helper exited between PID enumeration and opening it. The sampler now records an unavailable sample and its error for this exit race, while retaining the functional checks. It does not change process classification or lifecycle behavior.
- `development-m14-final-20261015-005912`: the Edge Unicode verifier read a passive page snapshot between the high and low surrogate WM_CHAR messages of an emoji. .NET rejected that intermediate string. Its bounded wait now retains such raw snapshots and counts them, then waits for complete expected text; persistent incomplete text still times out. Input delivery is unchanged and never replayed. The subsequent Edge text/recovery check passed.

These reinforce a useful distinction: input-handler completion, application-state observation and visual publication can occur at different times. Successful native API delivery is insufficient evidence that the expected pixels are ready. This milestone preserves the failed runs and reports continuations separately; it does not label the interrupted full-suite attempts successful.

All **27 baseline cases** subsequently passed across the selected runs and continuations: native isolation/fault containment, legacy viewer, keyboard/pointer state, Qt Quick GPU viewer, three WPF viewer modes, WPF startup adapter controls, ordinary ILSpy, Chrome/Edge hidden input and presentation, concurrent browsers, reusable offscreen capture, worker recovery, save opportunity and launcher checks. The exact case-to-run mapping is `artifacts/m14-verification-index.json`. The final remainder run is `development-m14-remainder-20261015-010200`. Benchmarks beyond the new GPU viewer activity samples were not repeated. This is combined case coverage, **not an uninterrupted full-suite pass**.

## Final verification and cleanup

The host builds with zero warnings/errors; all 24 PowerShell scripts parse. The final audit found **zero owned experimental process survivors**, no newer unbuilt host sources, and **zero hash mismatches across 12 preserved snapshots / 2,265 files**. The pinned mpv executable and both original/accepted D3D probe copies remain unchanged. The parent startup-hook environment is absent. Evidence: `artifacts/m14-final-audit.json` and `artifacts/gpu-viewer-research-status.json`.

All browser profiles were retained without attempting policy-blocked deletion. There are **123 profiles totalling 3,976.239 MiB (3.883 GiB)**, an increase of 280.361 MiB since the prior audit. Seven new profiles account for 224.103 MiB; reused profiles account for the remainder. The portable mpv package remains 90.467 MiB. Accumulated disposable Y4M media totals 115.366 MiB across 35 files, including earlier milestone evidence. All generated data stays inside VIAVW.

No Git operations, driver installation, privileged changes, security bypass, session enablement, persistent system-wide setting changes, or unrelated project modifications were performed. No demonstration is left running; the tested executable and launcher are ready for manual use.

## Run the demonstration

From PowerShell in `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-mpv.ps1 -SkipBuild
```

Omit `-SkipBuild` to build managed and experimental native binaries. The launcher verifies the pinned portable package, creates a fresh disposable 3.3 MiB media file, and opens the viewer visibly. It does not install or register mpv. The normal duplicate guard applies; `-AllowDuplicate` explicitly permits another viewer.

1. Use **Automation: step frame**. The orange/green image and binary frame index should advance after its delayed preview.
2. Select **Human control**, click the picture to focus its input canvas, and press `.` to step or Space to play/pause. Use **Inspect hidden app** or **Refresh** to see playback progress. There is no continuous preview loop.
3. Select **Return to automation**. Picture typing should be rejected; automation controls should work again in the same application.
4. Try the source-size buttons under automation ownership, hide/show the preview, and use either recovery button. **Hide preview** stops captures; playback can continue.
5. Close the viewer normally. **Force close** terminates its owned application tree and discards source state.

Physical keyboard/mouse behavior, foreground coexistence while another application is used, and comfort of the handoff are pending user verification. All reported Human sender results were generated by automation on private QA desktops.

## Limits and next experiment

The viewer draws source pixels unscaled at the upper left, as in the existing baseline; large source sizes can extend beyond its canvas. Enlarge the viewer or use 480 × 270 / 640 × 360 for the complete image. Captures contain the GPU client image, not every native menu, decoration or popup. A changed source root requires reopening the experimental demo. D3D12, Vulkan, OpenGL, protected output, real device removal/TDR and GPU migration remain unverified. Native messages do not implement raw input, IME, global asynchronous state or arbitrary polled mouse state.

The next useful investigation is a second ordinary D3D11 application with richer mouse interaction, using a separately pinned disposable package and the same ownership checks. Compare its actual hidden capture/input behavior before broadening the allowlist or generalizing injection. A proportional preview with coordinate mapping would also improve handoff for larger sources. Keep the accepted native/private capture paths available while either experiment proceeds.

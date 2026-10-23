# VIAVW — Milestone 13: ordinary private GPU application and changing resources

October 15, 2026. The accepted Milestone 12 source, scripts, documentation and binaries were preserved at `artifacts/baselines/accepted-runtime-adapter-20261015` with 232 SHA-256 entries. The normal private-desktop viewer remains available and does not automatically load the experimental DLL.

## Demonstrated result

**An ordinary portable mpv application rendered through hardware D3D11 on a private Windows desktop, with its native window hidden.** The wrapper recovered its actual GPU back buffer, delivered native window input, preserved the same source process through ownership and worker/reader recovery, and captured resized output. Two mpv instances also worked concurrently on independent desktops/jobs, retaining different paused frames.

The mpv executable is unchanged on disk and hash checked. Its running process is explicitly instrumented for capture. No application plugin, modified mpv source, input API shim, JSON command interface, virtual driver or physical input injection was used. This is a successful ordinary-application experiment within the tested configuration, not universal application transparency.

The package is the [upstream first-party CI build](https://github.com/mpv-player/mpv/releases/tag/git-release), `v0.41.0-dev-g7c4f662c0`, x64 MSVC, built October 14. [mpv's installation page](https://mpv.io/installation/) identifies these CI packages as testing builds. The pinned zip SHA-256 is `58060D3F140AE35C6578BD3606DD13E3A5A5EEF2F78B025C2FD790CA70E1C98C`; the executable SHA-256 is `82BD066203AA8997164D9BB8F6555DA37D905D1BD3CF50A8D7FFDDB589684EC4`. Only `mpv.exe` and its packaged Vulkan loader were extracted under `.deps/mpv-7c4f662c0`. Registration scripts were not extracted or executed. No installer or system configuration was used.

mpv was launched with its ordinary D3D11/BGRA or RGBA options, WARP disabled, audio/OSD disabled and project-local configuration/cache/log paths. Media controls and screensaver inhibition were disabled for the child. Its log identified **NVIDIA GeForce RTX 4060 Ti / feature level 12_1**, and the captured resource's actual DXGI format was checked. The disposable 320×180, 5 fps Y4M video carries alternating colors and a binary frame index; it is media data, not a cooperative application adapter.

## Implementation and evidence

The new `DynamicPresentBridge.dll` is separate from the accepted fixture adapter. It intercepts Present, Present1 and ResizeBuffers only inside exact owned research targets. The helper's dynamic build accepts two fixed paths: the generated dynamic fixture and the pinned project-local mpv. Managed checks also verify executable hashes, root classes, job membership and PID/creation identities. The original D3DProbe-only path retains its byte guard.

Before a requested copy, the adapter checks the actual source device, buffer dimensions and format. It replaces shared textures when these change, while the reader retains its D3D device on the tested GPU. An acknowledgement field keeps a completed texture alive until the reader has copied and released it. No back-buffer reference is retained between callbacks, so normal ResizeBuffers can release its buffers. Input and explicit private resize commands use the existing serialized ownership gate; resize uses no activation or desktop switching.

BGRA output follows the existing readback/PNG path. RGBA initially failed WIC format negotiation; the reader now normalizes channels explicitly into a reusable CPU buffer before encoding. Actual orange/green pixels verify the result. This still performs GPU copies, CPU readback and PNG encoding; it is not zero-copy viewer presentation.

Consolidated evidence: `artifacts/dynamic-gpu-20261015-002352`.

| Experiment | Verified outcome |
| --- | --- |
| Separate dynamic D3D11 fixture | 31 checks; 21 captures with strictly advancing GPU-encoded counters. Present1 and Present both worked. |
| Twelve fixture size changes | 800×600, 480×320, 1024×576 and 620×440; 13 shared/staging generations including the initial size, **one reader device**. |
| Voluntary source device/swap-chain recreation | Same source PID and window; correct fresh pixels, another shared generation, reader device still one. This did not simulate GPU removal/TDR. |
| Ordinary mpv | 27 checks; 19 final captures: exact frame stepping, simulated Human/Automation handoff, pause/play, worker and reader recovery, and three hidden resizes. |
| Two ordinary mpv layers | BGRA and RGBA runs overlapped; 39 captures, different retained paused frames, independent source PIDs/desktops/owners, recovery, resize and cleanup. |

The fixture's binary image counter checks freshness independently of its passive JSON telemetry. mpv verification reads the counter from video pixels and waits for the expected frame without replaying input. Its native `WM_CHAR` period action steps frames; native Space key messages toggle playback. The pinned [upstream window code](https://raw.githubusercontent.com/mpv-player/mpv/7c4f662c0/video/out/w32_common.c) was inspected to understand its asynchronous message handling. Physical human acceptance remains pending; Human sender tests are automated.

## Performance and failures

For the single mpv run, first capture at 640×360 took **160.5 ms**, including lazy reader initialization. Eight paced playback inspections had a **26.3 ms median**, range 25.0–73.0 ms. These timings include capture/PNG processing and pixel reading. Native frame-step readiness needed roughly one 200 ms video interval and bounded visual polling; an additional conservative 220 ms settle separates completed step commands. Message acceptance is not rendering acknowledgement.

During 2.064 s of actual playback and eight inspections, source/worker/reader CPU totaled **156.25 ms**, approximately **0.32% of the 24-logical-processor machine**. The verifier used another 296.88 ms, including image checks, physical sampling and GPU counter collection. mpv private memory was about **114 MiB** and reader private memory about **49.6 MiB**. These small synthetic-media measurements do not predict high-resolution decoding or gaming workloads.

Individual 1.5 s 3D-engine observations were about 0.036% for mpv and 0.032% for its reader. Paused without inspection, the copy counter stayed fixed, but small CPU/GPU observations remained; retaining resources does not prove zero GPU activity. Process-accounted local GPU usage rose from about 14.23 to 22.36 MiB in mpv across resizes, while the reader remained about 7.89 MiB. Driver caches/accounting prevent treating these as unique physical allocations or proof of leaks. The concurrent reports sample the same verifier PID over overlapping intervals; those verifier entries must not be summed. The original 22% observation remains unexplained.

Failures were retained:

- The fixture initially returned `E_ACCESSDENIED` when replacing its flip swap chain. Releasing the old chain and then flushing corrected its deferred-destruction order, consistent with Microsoft's [Flush documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush). No permission change was involved.
- Immediate mpv captures after input showed preceding frames and closely spaced steps could overlap. Waiting for actual image counters fixed verification; input was never blindly retried.
- RGBA's initial PNG negotiation failure was corrected by explicit channel conversion. An unsupported media-control option was corrected after querying the binary's options.
- One earlier run had whole-run foreground/pointer differences, with successful input-desktop preservation and cleanup. Its cause was not established. Later 25 ms read-only change sampling recorded a single initial state and no changes throughout the final mpv runs. This is sampled evidence, not continuous interception or fabricated physical-user acceptance.

## Verification, limits and reproduction

The accepted runtime-adapter suite passed at `artifacts/runtime-adapter-20261015-002419`, including its bounded source/reader stalls, original input restoration, independent layers, Character Map semantic tests and cleanup.

The baseline run `artifacts/development-20261015-002519` passed its earlier checks, including all 19 native checks, but stopped at the existing 30 ms browser-viewer test's text-setup timeout. An isolated retry at `artifacts/m13-fast-viewer-retry` passed **without viewer or test changes**. All eleven remaining cases then passed at `artifacts/development-m13-continuation-20261015-003226`. Across those runs, all 23 reports with an explicit `passed` field passed; the initial failed attempt remains recorded separately in `artifacts/m13-baseline-failure-notes.json`. Its timing cause is unresolved, so this is not described as an uninterrupted successful full-suite run. Longer existing benchmarks were not repeated.

Final audit: **zero owned process survivors**, **2,017 files matching across eleven snapshots**, zero script parse errors and no source newer than its host build. mpv still matches its pinned executable hash. **116 browser profiles remain**, totaling **3,695.88 MiB (3.61 GiB)**; six new regression profiles account for 158.39 MiB, with total growth of 213.59 MiB including reused profiles. The retained mpv package occupies 90.47 MiB; twelve small video fixtures total 39.55 MiB. Nothing was deleted. No Git operations, unrelated project changes, driver installation, privilege changes, session enabling or persistent Windows changes were performed. Verification and audit details are indexed in `artifacts/dynamic-gpu-research-status.json`.

Coverage is limited to this pinned mpv build, synthetic Y4M playback, the selected ordinary D3D11 options, BGRA8/RGBA8 and the tested GPU. The fixture's Present1 tests repaint the full frame; dirty-rectangle/scroll composition, real device loss, adapter migration, HDR/10-bit formats, other graphics APIs, multiple rendering contexts and arbitrary application injection remain unverified. The capture is the client GPU back buffer; unrelated menus, non-client decorations and dialogs may need another capture route. There is no complete virtual keyboard/mouse device or mpv mouse/IME compatibility claim.

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\test-dynamic-gpu.ps1 -SkipBuild
# Rebuild with the installed toolchains and run:
.\scripts\test-dynamic-gpu.ps1
```

The setup step verifies the cached pinned package, downloading only if absent. It never silently selects a newer build; upstream asset retention may affect a fresh download. The runner creates fresh evidence, runs bounded experiments and closes owned processes. Compare `mpv/paused-first-frame.png`, `mpv/native-frame-step.png` and the resized captures; inspect the concurrent BGRA/RGBA paused frames. Both an ordinary mpv image and a resized fixture image were visually inspected during development.

The accepted interactive viewer remains available through `scripts/private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput -InspectionSettleMilliseconds 30`. The new backend is a command-line research demonstration, not yet a selectable viewer backend.

The next useful step is to connect this tested backend to the existing multithreaded viewer, retaining ownership, resource generations and bounded helper recovery, then obtain real human handoff acceptance with mpv. Direct GPU presentation in that viewer could later remove PNG overhead; it has not been implemented or measured.

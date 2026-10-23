# VIAVW — Milestone 12: controlled runtime adapters and alternative paths

October 14, 2026. The accepted private-desktop implementation was preserved at `artifacts/baselines/accepted-capture-timing-20261014`, including working binaries and a 214-entry SHA-256 manifest. Default viewers do not load the new adapter. No Git operations, driver installation, privilege changes, session switching, production data changes or persistent Windows changes were performed.

## Findings across the proposed paths

| Path | Experiment and observed result |
| --- | --- |
| Process-local graphics instrumentation | A new DLL intercepts DXGI `Present` inside the **unchanged project D3DProbe fixture**. The native window remains hidden on its private desktop. Actual changing triangle pixels were recovered without showing that window. |
| Direct GPU resource sharing | The adapter copies the back buffer, on request, into a keyed shared D3D11 texture. A separate reader on Default opens it on the matching GPU adapter. Its device and staging texture are reused. Two independent layers worked concurrently. |
| Process-local input instrumentation | An executable import-slot adapter supplies the high pressed bit of `GetAsyncKeyState` during a serialized owned chord. The source observed virtual Ctrl; the parent physical-state sample did not change. State was cleared afterward and original behavior restored when stopped. |
| Accessibility hybrid | Unmodified Windows Character Map accepted UI Automation `ValuePattern.SetValue`, including while hidden. Its `InvokePattern` Select operation failed. The accepted targeted mouse path still selected the glyph. |
| Separate sessions | A query-only inventory found Windows Core build 26300.9711, current/console session 1, child sessions disabled, and no child session. No session was enabled, created or connected. |
| Virtual display / virtual HID | Documentation and experiment requirements were assessed; no driver was built, installed or loaded. These remain proposals requiring specific approval. |

The graphics fixture's executable is byte-for-byte identical to the preserved accepted executable. Its source has no shared-texture or key-state adapter support. **The running process is intentionally instrumented in memory**, however. This demonstrates a wrapper-side mechanism without rebuilding the source application; it does not yet demonstrate transparent compatibility with a third-party GPU application.

## Implementation and containment

`PresentBridge.dll`, `GpuResearchHost.exe` and their protocol live under `experiments/native`, with outputs in `artifacts/gpu-research-build`. Managed research sessions and verifiers are separate from the accepted viewer paths. The helper is registered as infrastructure in the layer's existing kill-on-close job before receiving any instrumentation command.

The managed entry checks the exact accepted executable bytes, owned PID/creation identity and root class. The native helper independently checks executable path, creation identity, root PID and membership in the named `VIAVW_` desktop. It accepts only the project D3DProbe path, not arbitrary applications. A Character Map test verified rejection before installation. Same-bitness, same-session operation is the only implemented configuration.

Only the source process's shared Present method-table slot and main executable's `GetAsyncKeyState` import slot are replaced. There is no global hook, physical input injection or privileged interception. Source GPU work remains on its own rendering thread. The producer never waits for the consumer's keyed mutex: a busy texture skips that copy. The reader has bounded Present and mutex waits. Interlocked request/completion fields and a keyed mutex coordinate the shared resource. The layer gate serializes input and enforces Human/Automation ownership.

Stopping restores both original slots, drains active callbacks, releases shared resources and closes the mapping. The DLL stays loaded but inactive until the owned source exits; attempting to unload code while another thread might enter it was deliberately avoided. Resource reset and helper restart recreate shared surfaces while preserving the application. A source stall was created by temporarily suspending **only its verified UI thread**, with unconditional resume and handle cleanup.

Microsoft's [shared-resource API](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-opensharedresource) and [keyed-mutex API](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync) informed the protocol. Mutex acquisition is checked against `S_OK` explicitly; positive timeout codes are not treated as success. Loading uses ordinary [CreateRemoteThread](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createremotethread) rights on the owned disposable source, without elevation or privilege changes.

## Actual verification and performance

Consolidated evidence: `artifacts/runtime-adapter-20261014-234729`. An earlier complete run is retained at `artifacts/runtime-adapter-20261014-234141`; the stricter final concurrency check is at `artifacts/m12-concurrent-gpu-bridge-opposite-colors`.

- GPU test: **55 checks**, including **38 pixel-verified captures**, scoped asynchronous Ctrl state, rejection under the wrong owner, simulated human input, worker recovery, reader recovery, resource reset, adapter removal and cleanup.
- Concurrent test: **32 changing captures** from two hidden private sources, independent key state/ownership, and successful continued operation after the peer closed. The final stricter check changes both colors on every round and keeps the two sources opposite, detecting stale or cross-layer images.
- A paused source returned a timeout in **2,003 ms**. Resuming and resetting resources recovered correct pixels in the same application process. Receiver device creation count stayed at one across a surface reset; surface-open/staging counts increased as expected. A separately suspended reader hit the parent deadline in **5,031 ms**; only that reader was killed. Reattachment recovered correct pixels from the surviving source.
- Between capture requests the copy counter stayed fixed while Present calls continued. This prevents unnecessary copies; it does **not** stop the application's own 100 ms rendering timer.
- Character Map: **six checks** passed, including the GPU instrumentation guard, semantic visible/hidden text, simulated ownership/recovery, the accepted mouse alternative and cleanup. The failed semantic Invoke remains explicitly recorded in `invokeEvidence`.

| Capture condition, 620 × 440 | Observed latency |
| --- | --- |
| Adapter installation | 103.3 ms |
| First capture, including lazy graphics initialization | 225.5 ms |
| Next natural Present, 12 changing captures | Median 102.2 ms; range 99.4–104.5 ms |
| Explicit private paint request, 20 changing captures | Median **8.9 ms**; mean 9.1 ms; range 4.3–14.3 ms |

The warm paint-request captures used **one reader device, one shared-resource open and one staging allocation**. PNG encoding was approximately 2.6–3.2 ms. The earlier complete run had a 5.4 ms warm median, illustrating scheduling variation. The helper retains graphics resources while attached, rather than recreating a device for every frame. The paint request posts `WM_PAINT` on a short-lived thread attached to the verified private desktop; this fixture's existing paint handler renders the frame. That behavior is application-dependent. These small-frame results are not a controlled speed comparison with the earlier browser/WGC experiment.

A paced activity phase performed 20 real input/color/capture cycles with 110 ms pauses in 2.663 s. Owned source, worker and reader CPU totaled **390.6 ms**, approximately **0.61% of this 24-logical-processor machine** over the phase. The supervising test harness used another 437.5 ms, including pixel verification, evidence, observation and GPU counter collection. The reader used about **51.3 MiB private / 36.9 MiB working-set memory**; the source was about **62.3 / 42.9 MiB** at that point, versus 51.4 / 41.1 MiB before attachment. These are short process snapshots, not bridge-only allocation totals or a leak guarantee.

During a separate 2.20 s interval without requests, the reader was charged 0 ms CPU at the process timer's granularity and its copy counter did not advance. Source Present calls continued. The reader's sampled 3D engine was 0%; source 3D was about 0.008%. During paced activity the sampled source/reader 3D engines were about 0.052% / 0.174%. These individual 1.5 s engine observations are not whole-GPU utilization figures or proof of zero overhead.

[QueryVideoMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/nf-dxgi1_4-idxgiadapter3-queryvideomemoryinfo) reported stable warm process-accounted local usage around **10.32 MiB source / 8.07 MiB reader**, with non-local usage around 0.92 / 2.07 MiB. Shared allocation accounting must not be summed as unique physical memory or attributed entirely to this adapter. The original 22% observation remains unexplained.

The full accepted regression completed with exit 0 at `artifacts/development-20261014-234220` using `-SkipBuild -SkipBenchmarks`: all 19 native checks and all 23 reports containing an explicit `passed` field passed, alongside keyboard, pointer, recovery and save checks. Default viewer/input paths were unchanged. The later research-only packaging/deadline changes passed the dedicated runner; the final stricter concurrency check also passed. Managed builds had zero warnings/errors, native builds succeeded, scripts parsed without errors and source/build timestamps agree. Longer existing benchmarks were not repeated.

Final audit found **zero owned experimental process survivors**, all **1,785 files** matching across **ten snapshots**, and both original and disposable-work-copy probe executables matching the accepted bytes. **110 retained profiles** occupy **3,482.29 MiB (3.40 GiB)**. Six new regression profiles account for 158.38 MiB; total growth since the previous audit is 211.49 MiB, including changes in reused profiles. No profile deletion or policy bypass was attempted.

## Failures and remaining limits

Early native guard attempts could not validate the foreign thread's desktop from the reader. Explicit enumeration of the known owned desktop fixed the validation without weakening the PID/path/creation checks. Failed reports remain under `artifacts/m12-gpu-bridge-*`. Ordinary invalidation did not accelerate the hidden fixture's natural rendering; an explicit private paint request did.

Character Map exposed a Value pattern and accepted text without focus. Its Select Invoke returned an invalid-operation error after about 2.13 s; no glyph change occurred. This is a provider/application result, not a general UIA limitation. Microsoft documents that [Invoke behavior depends on the provider](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.invokepattern.invoke?view=windowsdesktop-10.0). A subsequent targeted click inserted `!` at the beginning of the text, illustrating that semantic SetValue does not preserve the same caret behavior as typed input. No Copy action or global clipboard write was used.

The graphics adapter currently covers this fixed-size D3D11 flip-sequential Present path only. Resize/device-loss, Present1, D3D9/12, Vulkan, OpenGL, protected content, arbitrary GPU subprocesses, mixed bitness and third-party injection compatibility are unverified. A hidden application that stops producing frames may require an application-specific readiness/render request; a successful DLL load cannot guarantee fresh pixels. No UIA success is claimed for the GPU fixture. The key adapter covers one executable import and the high pressed bit during a bounded chord, not full asynchronous polling lifetimes, raw input, low-bit transitions, IME, independent cursor polling or virtual devices. No new physical-human acceptance is claimed.

## Paths requiring a separate experiment and approval

[Child sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions) are a documented loopback session mechanism, with one active connected child at a time. They require enabling the feature and an RDP ActiveX connection. The local read-only query found them disabled. A future proposal would first identify edition/build support, the exact enable/disable and rollback procedure, logoff containment, resource cost and whether the parent remains undisturbed. No suitability conclusion follows merely from the API being present.

An [indirect display driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview) can supply virtual monitor output and DirectX desktop surfaces. It may provide another presentation route, but per-layer private-desktop composition remains an experimental question. A [VHF HID source driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-) submits device reports through the Windows HID stack and currently requires a kernel-mode source driver. Independent device-to-layer routing would still need to be established. Neither avenue should be assumed to solve focus isolation automatically.

Before any installation, a concrete proposal needs the exact signed package/device scope, administrator requirement, effect on display layout or input, removal/recovery procedure and separate test-machine plan. No test-signing, driver download, installation, Windows security change or device report submission was attempted.

## Reproduce and continue

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\test-runtime-adapter.ps1 -SkipBuild
# To rebuild the new host and adapter with the installed toolchains:
.\scripts\test-runtime-adapter.ps1
```

The runner creates a fresh evidence directory, captures PNGs, exercises recovery and ownership, closes owned applications, and performs the query-only session inventory. The build makes a byte-identical work copy of the preserved accepted D3DProbe under `artifacts/gpu-research-build`; normal native fixture rebuilds are independent. The accepted snapshot is never rebuilt or written. Compare `bridge/hidden-green.png`, `bridge/hidden-orange.png` and `bridge/after-source-stall.png`. Both a GPU frame and the final Character Map image were visually inspected during development.

The accepted interactive demonstration remains:

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput -InspectionSettleMilliseconds 30
```

Use its existing Human/Automation controls and on-demand inspection. The runtime GPU adapter is a command-line research harness, not yet a selectable viewer backend. Physical interaction testing of the new adapters remains pending.

The strongest next experiment is a separate disposable D3D11 variant exercising resize and Present1, followed by an explicit allowlisted ordinary portable GPU application with the same recovery tests. Keep the current private-desktop viewer as the fallback. A future direct shared-texture viewer could avoid PNG readback/encoding, but that improvement has not been implemented or measured.

Final baseline regression, snapshot checks and retained storage totals are indexed in `artifacts/runtime-adapter-research-status.json`. Existing rejected profile deletion remains respected: all retained profiles stay in place.

# Continued development — private GPU capture and reusable resources

An unmodified Chrome application window now operates on a private Windows desktop with native GPU capture and native keyboard/mouse messages. The production demonstration needs no CDP adapter. The automated test enabled Chrome's developer port only for read-only inspection of results. Qt Quick and hardware Direct3D also produce changing native GPU captures on private desktops. This is a stronger continuation of the private-desktop foundation, with application-specific limitations still recorded below.

## Discoveries and compatibility

The original capture used only `PW_CLIENTONLY`. Adding the SDK's `PW_RENDERFULLCONTENT` bit changed the result substantially. The installed Windows SDK defines this flag as `0x2`; [WebRTC's Windows capturer](https://webrtc.googlesource.com/src/+/f89110d67902e787f6745ad2b52f7f09fc808512/modules/desktop_capture/window_capturer_win.cc) uses it for composition-rendered applications. The implementation now exposes an opt-in full-content backend. This was tested through actual pixels and state changes, not just API return values.

| Application/mechanism | Actual result |
|---|---|
| Unmodified Chrome app window on private desktop | Hardware WebGL pixels through full-content native printing; pointer click, typing, Ctrl+A replacement and color-button action through native Windows messages/queue state. Same process through ownership handoff. |
| Qt Quick, normal Direct3D11 renderer | Native GPU preview, text and button/color changes; modifier shortcuts and handoff through the actual private viewer. No software override or grab adapter. |
| Hardware Direct3D11 fixture | Native triangle changes green/orange after normal input; GPU adapter is NVIDIA RTX 4060 Ti. Useful pixels despite private Present reporting DXGI_STATUS_OCCLUDED. |
| WPF fixture | Full-content capture remains blank before and after private focus/capture activation. Native pointer priming enables its normal button, selection drag and wheel scrolling. Cooperative visual capture remains available. |
| Private Chrome with explicit browser adapter | Headful and headless hardware WebGL capture/input work through CDP. This is an additional compatibility route, not needed by the new native demonstration. |
| WGC on private Chrome/WPF/Qt/D3D windows | Still fails with capture item size zero and resource-state errors. Reusing a device does not cure this acquisition failure. |
| Hidden private GPU windows | Full-content printing becomes blank. Prefer hiding the viewer preview while retaining a logically visible private window. Background application state and useful pixels are separate observations. |

Windows capture mechanisms are not interchangeable. Failure of private WGC did not establish that native private GPU capture was impossible. These results establish the tested application paths; they do not establish universal compatibility for their frameworks.

An initial native-input sequence also timed out a CDP request after hiding a privately activated Chrome window; it did not publish its final JSON. The observation is recorded in `artifacts/m4-private-native-input/failure-notes.txt`. Later native input/viewer tests passed with the private source logically visible. Hidden-window behavior still needs application-specific investigation.

## Implementation changes

`CaptureBackend.PrintWindowFullContent` uses flags `1 | 2` inside the existing desktop-local worker. The original default, private jobs, RPC deadlines and cleanup remain intact. `--full-content`/`-FullContent` choose this path explicitly.

`SendQueueMouseSequence` supplies a bounded atomic timeline of mouse messages and private button/modifier state. It validates HWND/job/desktop scope, attaches only private GUI input queues, optionally primes private focus/capture, and restores/releases state in `finally`. Ownership remains serialized for the entire batch. The new `-QueuePointer` viewer option forwards clicks and wheel input through this path. `-RootInput` avoids targeting helper child HWNDs for custom windows such as Chrome.

The WPF test changed actual control state: counter increment, nonempty drag selection and 48 pixels of scrolling. Character Map drag selection and Qt Quick wheel scrolling did not improve. Existing simple Qt/D3D interactions remained functional. No SetCursorPos, SendInput, global hook, injection or physical-device state modification was used. This does not implement arbitrary polling, asynchronous device state, IME, held input across separate calls or full human drag forwarding. Human preview drag remains a separate future UI improvement; the atomic drag API was tested.

`WgcWorker.exe` and `ReusableCaptureClient` retain a D3D device, capture item, staging texture and WIC factory. A randomly named same-user pipe and token handshake connect the client. A nested kill-on-close job contains the capture worker. A timeout terminates only that helper; the application, state and ownership survive. Explicit restart and automatic lazy recreation were verified. Frame pools and sessions close after each request, so the idle worker does not continuously acquire frames.

Retaining/reopening the frame pool itself failed with `E_UNEXPECTED` in a rapid loop. The reliable implementation keeps the expensive resources and creates a fresh pool/session per request. That failure is retained in `artifacts/m4-capture-reuse-first/failure.txt`.

Faster capture exposed another distinction: input handler completion can precede the compositor's new frame. Immediate warm snapshots sometimes retained the previous color. The offscreen Chrome backend now uses a browser-specific animation-frame scheduling barrier after input, with a timer fallback. This worked for all ten changed-frame checks in the final run; it is not a universal application acknowledgement.

Regression also exposed passive fixture report replacement colliding with readers. Readers now allow atomic replacement; transient report replacement errors no longer interrupt the fixture's UI. QA preview telemetry uses atomic publication. The failing private error-dialog screenshot remains separate from successful regression evidence.

## Measurements

The successful combined run uses 24 logical processors and includes fixture/diagnostic overhead.

| Measurement | Observed result |
|---|---|
| Native private Qt Quick capture, 42 samples | Median 12.82 ms, p95 15.23 ms; full RPC median 14.50 ms |
| Native private D3D capture, 42 samples | Median 11.40 ms, p95 15.05 ms; full RPC median 12.23 ms |
| Both private GPU layers, five-second intervals | About 0.47% whole-machine CPU without interaction and 1.70% during interaction/capture, including manager/worker/application diagnostics |
| Cold WGC, same Chrome source, four controls | Median 253.25 ms, maximum 288.59 ms; median helper CPU 187.5 ms |
| Warm WGC, twenty raw requests | Median 21.06 ms, p95 25.60 ms; CPU samples 0–15.625 ms at coarse process-time resolution |
| Updated Chrome frames after scheduling barrier, ten samples | Median 40.93 ms, p95 44.37 ms; zero incorrect color checks |
| Raw immediate snapshots | Eleven retries across twenty input changes; explicitly not counted as first-frame update success |
| Retained worker, eight-second idle interval | 15.625 ms CPU, about 0.008% whole-machine utilization; sampled owned GPU engines zero. An earlier run measured no CPU delta. |
| Retained capture worker memory | About 45.4 MiB working set / 52.8 MiB private bytes |
| Viewer during deliberate five-second PrintWindow stall | 95 UI pings; median 0.020 ms, maximum 0.251 ms |

No resident capture helper/device is needed for the private full-content path. The reusable WGC worker trades retained memory for lower setup CPU/latency in its alternative backend. First capture remains cold. One earlier cold control took 2.26 seconds; that outlier is preserved, not averaged away. Working sets include shared pages, GPU counters are per owned engine, and Windows/DWM work outside the owned processes is not fully attributed. These are bounded activity measurements, not an endurance guarantee or an explanation of the historic 22% Task Manager observation.

## Verification and recovery

The accepted Milestone 3 source/scripts/docs/executables were preserved at `artifacts/baselines/milestone3-20261014`; all 134 recorded hashes still match. No Git operations or unrelated project changes were performed.

The complete automated suite passed in `artifacts/development-20261014-023634`: 19 native isolation/lifecycle checks, legacy viewer, keyboard state, pointer semantics, native private Qt GPU viewer, ordinary native Chrome viewer, reusable offscreen viewer, worker recovery, save opportunity, duplicate/startup/reveal checks and both performance experiments. A final clean build had zero warnings/errors. The extended launcher test in `artifacts/m4-final-script-launcher` also launched the actual new browser demo script without a developer port and verified ready/visible status and graceful cleanup. Launcher tests displayed controllers only on a private QA desktop. Source/application ownership and same-process handoff were verified. New human hardware acceptance remains pending.

Capture reuse tests verified one device and staging texture through repeated shots, buffer replacement on resize to 900×700, explicit worker restart and a deliberately hung helper. The hung helper was contained in about 721 ms; Chrome's PID, text and Human ownership survived. Capture recovered afterward. Native PrintWindow hangs retain the original per-layer containment behavior.

Additional compatibility evidence, including WPF activation and changing native GPU pixels, is in `artifacts/m4-private-gpu-activation-final`. Actual PNGs were visually inspected. Failed attempts and earlier regressions remain separately identifiable. Final process/storage status is recorded in `artifacts/development-status.json`: zero project browser/prototype process survivors, 134 baseline hashes matching, and zero script syntax errors. All 43 identified disposable browser profiles across project artifacts are retained, totaling 901.47 MiB, including earlier milestones. No deletion or policy bypass was attempted. Per-profile sizes are recorded in `artifacts/retained-profile-storage.json`.

## Demonstrations when you return

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild
.\scripts\quick-demo.ps1 -SkipBuild -NativeGpu
.\scripts\wpf-demo.ps1 -SkipBuild -QueuePointer

# Alternative Default/offscreen WGC path, now with reusable resources.
.\scripts\gpu-demo.ps1 -SkipBuild

# Rebuild/verify the complete development suite without foreground disruption.
.\scripts\test-development.ps1
```

For the private Chrome demo, take Human control, click the page's text field, type and use Ctrl+A. Click Change GPU color, then return ownership to automation and try typing in the picture again. Use Automation: send text to continue in the same application. Test ordinary typing in another physical-desktop app while virtual automation runs. Hide preview to stop captures. Close normally; if a remaining process/save prompt keeps the controller open, inspect it and use Force close only for the disposable session.

For WPF, try the counter button and wheel scrolling with QueuePointer. Its picture still uses the explicit fixture adapter. For offscreen WGC, try Restart capture worker and confirm browser text/PID persist. These physical-user checks are instructions, not completed acceptance results.

## Next recommendation

Investigate the WPF rendering path that stays blank after private activation and full-content printing, while expanding tests to another unmodified GPU application and longer mixed-input/recovery runs. Preserve the successful native private route rather than defaulting all applications to offscreen placement. Any driver, system-wide interception or security-boundary bypass remains outside authorization. A more invasive per-process shim should have a specific compatibility/risk proposal before execution.

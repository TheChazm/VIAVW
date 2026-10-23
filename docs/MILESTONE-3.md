# Milestone 3 — independent capture and input research

The useful result is two complementary improvements: ordinary modifier-dependent input now works on the tested private desktops, and an unmodified hardware-rendered Chrome window can be inspected through native Windows Graphics Capture while remaining offscreen and nonactivating. The second result uses Default's compositor and browser-adapted input. It does not solve transparent private-desktop GPU capture.

## Baseline and scope

Before editing, the Milestone 2 source, scripts, documentation, .NET executables and native executables were copied to `artifacts/baselines/milestone2-20261014`. `sha256.json` records the preserved files. Its original executable subsequently passed the 19 native baseline checks. Qt dependencies can be recreated with its pinned setup script; the snapshot does not duplicate `.deps`.

The private desktop, worker recovery, ownership, graceful save opportunity, on-demand capture and multithreaded viewer remain available. No Git operations, unrelated project edits, drivers, global input injection, desktop switches or privileged/persistent Windows configuration changes were performed.

## Experiments and results

| Experiment | Observed result | Meaning |
|---|---|---|
| Share input state only between the private worker and an owned GUI thread | Ctrl+A and Shift+End select text in unmodified Character Map. WPF and Qt Quick recognize Ctrl+A, Shift+End, Ctrl+K and Alt+K. | An application can read useful logical modifier state without a physical desktop key event or application adapter. |
| Direct3D fixture reads key state | Its real Ctrl+K handler runs; GetKeyState observes Ctrl, GetAsyncKeyState remains false. | Queue state and global asynchronous device state are different mechanisms. |
| Two private WPF applications operate concurrently | A's held Ctrl does not cause B's unmodified K to become a shortcut. A rejects B's HWND. A's modifier is released afterward. | The tested state-sharing transaction respects layer boundaries. |
| DWM thumbnail from private source into a Default destination | Registration returns `0x80070057`. | This attempt did not provide a presentation bridge. |
| Cross-process DWM cloak | Private source from Default returns `0x80070006`; owned-job source on Default returns `0x80070005`. | Owning a process through a job does not make this cloak call succeed. No restriction was bypassed. An owner-process cloak experiment remains untested. |
| Startup-hidden Direct3D fixture moved offscreen, shown without activation, captured by WGC | Actual green GPU triangle, then orange after its normal Space key action; NVIDIA hardware; Present succeeds. | Offscreen presentation on Default can preserve a composited GPU surface. Capture required no cooperative grab protocol. |
| Same GPU window with SW_HIDE | WGC rejects it at CreateForWindow. | Hidden and offscreen-visible are materially different states. |
| Unmodified installed Chrome, normal headful mode, disposable profile/local WebGL page | Native WGC includes browser chrome, page text and WebGL color changes. Renderer reports NVIDIA RTX 4060 Ti through ANGLE/Direct3D11. | An ordinary GPU application provides a usable window surface without executable changes. |
| Chrome normal occlusion behavior | First captures show browser chrome with blank page pixels despite a live DOM/WebGL context. | API success and renderer identification alone do not establish useful visual capture. |
| Child-instance `--disable-backgrounding-occluded-windows` | Page and WebGL pixels appear and update. | A launch configuration can address this application's background rendering policy. No persistent Chrome setting was changed. |
| Native WM_CHAR to Chrome's render HWND | No text or DOM input event. CDP key and mouse events work. | Native capture succeeded; transparent native input did not. CDP remains an explicit compatibility adapter. |

Captured Chrome WebGL RGB values differed from the requested canvas colors: a green region measured `(71,180,106)` and orange `(234,136,56)`. Initial narrow RGB assertions rejected real, visually correct scenes. Final verification records actual samples and checks large green/orange regions, state transitions, text and trusted browser events. The exact color-conversion cause was not isolated; bit-exact color reproduction is not claimed.

## Implementation and handoff

`ApplicationLayer.SendQueueStateChord` validates ownership and HWND/job/desktop scope, creates the worker's message queue, attaches only to the private target GUI thread, sets generic and side-specific Ctrl/Shift/Alt state, delivers the chord, restores the attached snapshot and detaches in `finally`. It refuses the input desktop or an unreadable input-desktop identity. The default synchronous variant has no artificial waits. The optional posted research variant uses bounded waits and does not acknowledge application processing.

Microsoft documents that [AttachThreadInput resets keyboard state](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput). This is a bounded chord transaction, not a sustained virtual keyboard with independently held keys. It does not set physical/global state, control GetAsyncKeyState, supply raw input, reproduce IME/AltGr behavior or solve pointer polling and drag capture. Compatibility is established for these controls and handlers, not every application using their frameworks.

The existing viewer routes modifier shortcuts through this path and keeps native waits off its UI thread. Software and GPU/cooperative Qt Quick preview tests exercised Ctrl+A replacement and Ctrl+K, ownership rejection and continued execution of the same application.

`OffscreenBrowserSession` is a separate `ILayerSurface` backend. It starts Chrome suspended, assigns a kill-on-close job, uses a fresh project-local profile and browser-adapted background target creation, and keeps the owned window outside all monitor bounds with nonactivating/tool-window styles. Capture validates the owned HWND and launches a bounded WGC helper on Default. Each request closes the helper and capture session; nothing continuously captures at idle. Input and ownership use a serialized CDP connection.

`GpuBrowserForm` provides a responsive native-window preview, automation text/color actions, attention request, human ownership, return to automation and preview suppression. Human clicks and keys target the page; the captured browser toolbar is inspectable. It currently forwards simple left clicks, text and keyboard shortcuts, not complete mouse gestures or arbitrary Chrome dialogs. The demo is intentionally restricted to its disposable local page.

The GPU viewer test exercised actual button handlers, preview KeyPress handling, CDP Ctrl+A replacement, attention, wrong-owner rejection, resumption and the same browser PID. Its QA controller was transparent and nonactivating. These are automated software-input results. User-origin typing and the naturalness of this new GPU handoff still need manual acceptance.

Offscreen placement is weaker isolation: the app remains on Default and can potentially create or activate other windows. Chrome's background creation and the controlled local page avoided that behavior in these tests. There were zero owned foreground hits in approximately 190 samples at 10 ms intervals; shorter changes are not ruled out. This backend is not a silent fallback for arbitrary private applications.

## Activity and resource observations

Measurements use 24 logical processors and disposable fixtures. The successful combined run recorded:

| Measurement | Result |
|---|---|
| Private synchronous Ctrl+K, five seconds | 160 requests, all counted; median 0.78 ms, p95 2.39 ms, maximum 12.71 ms |
| Native GPU browser captures during color changes | 17 captures; median 241.95 ms, p95 257.62 ms |
| Capture helper CPU per request | Median 187.5 ms; 3,187.5 ms total CPU in 5.17 seconds, about 2.57% of the whole machine |
| Chrome processes, short no-input/active intervals | About 0.77% / 0.26% whole-machine CPU; startup work remains in the short no-input interval |
| Manager with diagnostic sampling | About 0.65% / 0.38% whole-machine CPU in those intervals |
| Chrome memory | Summed working sets about 557 MiB; summed private bytes about 273 MiB |
| Manager memory | About 77 MiB working set / 36 MiB private bytes |
| Chrome 3D engine, one 1.5-second sample | About 0.004% without input / 0.067% during activity; capture-helper engines excluded |

Working-set sums count shared pages more than once. GPU engine percentages are not a whole-GPU utilization claim. Fixture telemetry and diagnostic overhead are included. These short intervals are not an endurance or sustained-idle benchmark, and they do not explain the earlier 22% Task Manager observation.

Native stage timing identifies a concrete cost. One 214 ms helper capture spent about 147 ms creating the D3D device, 49 ms creating the capture item, 6 ms creating the pool/session, 3.5 ms starting/waiting for its first frame, and 8 ms closing/readback/PNG encoding. Repeated cold helper setup dominates this capture path. Keeping a reusable graphics device while opening capture only on request is a well-supported next experiment. The present per-request helper provides simple stall containment and zero persistent capture activity.

## Run and verify

From `C:\Programming\Projects\VIAVW`:

```powershell
# New ordinary GPU application demonstration; the controller is visible.
.\scripts\gpu-demo.ps1 -SkipBuild

# Strong private-desktop baseline, with new modifier forwarding.
.\scripts\demo.ps1 -SkipBuild -Executable C:\Windows\System32\charmap.exe
.\scripts\quick-demo.ps1 -SkipBuild -Software

# Rebuild and run the combined research suite.
.\scripts\test-milestone3.ps1
```

The installed .NET 10 desktop runtime, Chrome path, project-local PySide6 and native Visual C++/Windows SDK toolchain match Milestone 2. The verification script installs nothing. Omit `-SkipBuild` in a demo script to rebuild. For GPU handoff, use Automation actions, take Human control, click the page, type or use Ctrl+A, return ownership, then try typing in the picture again. The same process should continue and reject input while automation owns it. Close the viewer to terminate its disposable browser tree.

Successful combined evidence is `artifacts/milestone3-20261014-010455`: 19 native regressions, modifier compatibility, both private preview modes, alternative presentation, ordinary GPU Chrome capture, GPU viewer handoff and activity/separation checks. Additional native viewer, recovery and save-prompt regressions passed. Final fail-closed input and hardware-renderer checks were reverified in `m3-queue-final-guard` and `m3-gpu-viewer-final`; stage timing is in `m3-browser-stage-timing`. Actual PNGs were visually inspected. Earlier failures remain in separate experiment directories and were not relabeled as successes.

Owned process cleanup passed, including native fault/host-death containment and both GPU session paths. Automatic approval review rejected deletion of disposable browser profiles with the reason "blocked by policy". These project-local cache/profile directories remain alongside the JSON and PNG evidence; the process/file status is recorded in artifacts/m3-cleanup.json. No new human acceptance is asserted.

## Next experiments

1. Reuse a GPU device in an owned worker while retaining on-demand sessions and a bounded shutdown path. Measure warm capture CPU and latency against these cold-helper timings.
2. Test another unmodified GPU application with documented nonactivating/background startup. Keep startup and input semantics explicit; do not assume Chrome's policy applies to other frameworks.
3. Investigate mouse button state and capture on owned private threads, plus richer viewer gestures. Polling, asynchronous state, raw input and IME still need distinct evidence.
4. Compare source/rendered/captured color paths and formats before promising exact GPU color reproduction.

A per-process presentation/input shim could potentially intercept polling or presentation calls, but it adds bitness, initialization, anti-tamper and crash-containment risks. A specific hosting/injection proposal should be discussed before such an invasive experiment; bypassing security boundaries is outside authorization. A driver-backed display/input path would require explicit approval for installation and persistent system changes. Neither was attempted.

Primary references: [AttachThreadInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput), [GetKeyboardState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardstate), [SetKeyboardState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setkeyboardstate), [DWM attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute), [DwmRegisterThumbnail](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail), and [Chromium's occluded-window switch](https://chromium.googlesource.com/chromium/src/+/master/content/public/common/content_switches.cc). Experimental conclusions above come from the saved local evidence, not solely from API documentation.

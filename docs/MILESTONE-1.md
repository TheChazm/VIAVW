# Milestone 1 — Working baseline and evidence

Completed October 13, 2026, America/New_York. Project directory: `C:\Programming\Projects\VIAVW`.

## Outcome

The project builds and runs. Two independent native layers receive targeted text/key/mouse messages on non-input desktops. A real unmodified Windows utility, a disposable ordinary WinForms app, and a disposable Qt Widgets app accept the tested interactions and produce usable captures. A separate Chromium adapter accepts page input and screenshots. The real wrapper viewer demonstrates human/automation handoff without restarting its target.

The demonstrated native mechanism is **desktop isolation plus a control-message adapter**, not a complete graphical pseudoterminal or independent hardware keyboard/mouse. This is a practical working baseline with explicit compatibility boundaries.

Launch:

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\demo.ps1
```

Repeat all available checks:

```powershell
.\scripts\test-all.ps1
```

No Git repository was initialized, and no commits, branches, pushes, merges, or tags were created. No production project was modified.

## Evidence

The final combined run is `artifacts\milestone-20261013-211026`. The JSON files contain actual process IDs, event/control state, timing samples, pointer/focus observations, and cleanup outcomes. Generated evidence is intentionally excluded from future source tracking. Earlier exploratory failures remain in separate artifact directories so they are not mistaken for final test results.

| Test subject | Input and visual mechanism | Actual result | Evidence |
|---|---|---|---|
| Two ordinary WinForms fixtures | HWND-addressed messages + PrintWindow | Separate `LAYER_A_ONLY` / `LAYER_B_ONLY`; KeyPress/KeyDown and Button.Click handlers execute; cross-layer HWND rejected | `native\verification.json`, `native-before.png`, `native-after.png` |
| Unmodified Windows Character Map | `WM_CHAR` into RICHEDIT50W; mouse down/up on Select; PrintWindow | `WIN32_LAYER` becomes `WIN32_LAYER!`; clipboard Copy was not invoked | `charmap\charmap-verification.json`, `charmap-after.png` |
| Qt Widgets 6.10.3 / PySide6 Essentials | Root HWND messages; ordinary Qt focused-widget/raster handling; PrintWindow | `QT_LAYER`; 9 key-press observations; 2 mouse-press observations; 1 clicked handler; handoff yields `QT_LAYERHR` in same PID | `qt\qt-verification.json`, `qt-after.png` |
| Chrome 154.0.8037.98, local page | CDP key/mouse dispatch + Page.captureScreenshot | `BROWSER_LAYER`; trusted DOM input and click events; counter increments; same browser PID/page survives handoff | `chromium\browser-verification.json`, `browser-after.png` |
| Actual wrapper viewer | Viewer UI exercised through private-desktop QA messages; target receives routed human input | Automation text, `HUMAN`, button click, then resumed automation text; target PID 27332 throughout | `viewer\viewer-verification.json`, `viewer-after.png` |

WinForms and Qt fixture counters/telemetry passively observe normal handlers; neither fixture has an input adapter, LayerHost reference, or replacement event path. The viewer test ran the actual UI on a disposable QA desktop so it did not activate a foreground test window on the user's physical desktop.

Notepad was not tested. Character Map was selected as the available ordinary unmodified Win32 application; the experiment did not need a modern packaged/single-instance Notepad launch.

## Launch, execution, and ordinary resources

Each layer creates a private Windows desktop and a kill-on-close job. The manager starts the worker suspended with `lpDesktop=WinSta0\VIAVW_<random-id>`, assigns it to the job, then resumes it. The worker starts the application using normal native process creation. The application's descendants remain in the job under ordinary process creation.

The processes run under the existing user/session with inherited environment and normal installed libraries. A scratch filesystem write/read and an existing Windows file check passed. The applications use ordinary CPU/RAM/runtime resources. No separate OS image, VM, container, emulated system, virtual display driver, or alternate shell was built. Registry/network functionality and GPU workload correctness were not individually tested.

These desktops are UI isolation containers; applications still share the user's filesystem, registry, network access, clipboard/window-station resources, and ordinary OS security context. The wrapper is not a security sandbox for untrusted software.

## Physical desktop independence

The native suite recorded eight before/after foreground/input-desktop snapshots. Foreground HWND and sampled pointer position during targeted input operations remained unchanged. The input desktop remained `Default`. Character Map and Qt also retained foreground focus and pointer equality in the measured runs.

Chromium's three input-operation samples retained the same foreground HWND and physical pointer, with the foreground process outside the layer's job. The final run also retained overall pointer equality. Some earlier runs recorded physical pointer movement across the complete multi-second experiment while the user was free to work on Layer 0. Those observations remain in their JSON; they are not attributed to the adapter or hidden by the final report. Character Map's test separately records operation samples and whole-run observations for the same reason.

The implementation has no `SwitchDesktop`, `SetForegroundWindow`, `SetCursorPos`, `AttachThreadInput`, `SendInput`, or focus-steal-and-restore path. Human interaction with the viewer naturally gives the viewer ordinary Layer 0 focus when the user chooses to inspect it; the private target is still controlled through routed messages.

The user subsequently performed and accepted the physical desktop concurrency, inspection, and handoff workflow. See [user-reported manual acceptance](MANUAL-ACCEPTANCE.md). This is separate from the original automated sampling; no manual-test telemetry, timestamps, or screenshots are inferred.

## Visual output and background operation

Native capture calls `PrintWindow(PW_CLIENTONLY)` into a GDI bitmap in the desktop-local worker, encodes PNG, and returns it over the pipe. The saved frames were visually inspected: the expected text and counter appear in WinForms, Character Map, Qt, and the wrapper viewer. The native desktop object is independent of the physical input desktop; the captured image is application-rendered output, not a copy of a foreground desktop screenshot.

The target window can remain logically visible on its private desktop while completely absent from the physical desktop. Hiding only the wrapper's preview leaves it running with no capture loop. A stronger `SW_HIDE` test kept WinForms/Qt timer workloads running, but WinForms `native-hidden.png` was black. Hidden workload execution and hidden pixel capture must therefore be treated as different capabilities.

Chromium runs headless and uses its own page rendering/screenshot mechanism. Its timer continued between inspections. No capture was requested during the timed hidden intervals.

Windows Graphics Capture, Direct3D, Qt Quick/OpenGL, protected content, minimized windows, occlusion throttling, and GPU costs remain unverified.

## Ownership, inspection, and handoff

`Automation` and `Human` are explicit owners enforced by the host. A handoff is serialized with input, and input from the wrong owner is rejected. Attention requests store a reason; there is no notification service.

Preview capture does not change ownership. The human-control viewer routes simple click/text/navigation messages to the same windows; returning ownership resumes the same application. Native, Qt, browser, and actual-viewer tests retained process and document/page state. No target was restarted or moved to the physical desktop.

Handoff is subject to each adapter's compatibility. This is not transparent delivery of every physical input feature: chords, accelerators, IME/composition, raw input, drag/drop, wheel, right-click menus, full mouse capture semantics, and keyboard-state polling were not implemented/tested.

## Resource and latency measurements

Native measurements from the final combined run, after four seconds of runtime warmup:

| Measurement | Result |
|---|---:|
| Manager working set / private bytes | 45.3 / 15.3 MiB |
| Capturing native worker working set / private bytes | 42.8 / 13.8 MiB |
| Second native worker working set / private bytes | 33.3 / 9.7 MiB |
| Hidden console helper per worker working set / private bytes | 7.94 / 1.17 MiB |
| Test app working set / private bytes | 48.5–52.1 / 12.5–14.6 MiB |
| Idle observation interval | 2,206.3 ms |
| Manager/workers CPU-time increase in that interval | 0 ms at the process counters' resolution |
| 12-character native batch | 14.98 ms |
| Mouse-message round trip, 20 samples | median 0.82 ms; p95 1.03 ms |
| 620 × 330 native capture + PNG + pipe, 6 samples | median 8.29 ms; p95 9.38 ms |
| Chromium 13-character key batch | 12.19 ms |
| Chromium 800 × 450 screenshot round trip | 40.99 ms |
| Chromium job tree including worker/helpers, approximate totals | 581.2 MiB summed working sets; 281.3 MiB private bytes |

The wrapper adds one managed worker and hidden console helper per native layer, plus the manager. A worker's private memory was about 10–14 MiB in this run; the managed runtime still shares OS/runtime pages. Summed working sets overstate unique physical RAM. The app's memory and Chromium's multiprocess/GPU-process allocations are reported separately from native wrapper overhead.

Zero measurable idle CPU is not a zero-cost guarantee. Earlier startup-inclusive samples were higher because runtime startup/tiered compilation had not settled. This is a short local sample, not a sustained benchmark. Event timings include synchronous handling/IPC; text-batch timing includes diagnostics, and capture timings include encoding. They do not measure hardware input latency or input-to-displayed-pixel latency. GPU usage, desktop heap overhead, kernel graphics allocations, long-duration stability, and continuous capture costs were not measured.

## Failure behavior and cleanup

The native suite passed 19 assertions. They cover independent desktops, normal process/filesystem access, child job membership, distinct input destinations, control event paths, cross-layer rejection, visual change, hidden workload, ownership rejection/state retention, hung capture containment, layer shutdown, abrupt host death including a child, release of the private desktop objects, and sampled physical desktop preservation.

A fixture deliberately blocked its print handler. Its capture hit the five-second deadline and only its own worker/application tree was terminated; the other two applications remained alive. A separate host was forcibly killed with no disposal path; its worker, application, console helper, and child exited. Normal layer close also removed the child tree. Final process inspection found no surviving `LayerHost`, `LayerProbe`, or Character Map test processes.

Every layer closes its job, pipe, and manager desktop handle on disposal. Layer close/crash cleanup is intentionally forceful and loses unsaved target state. Ordinary brokered launches, deliberate external-service process creation, Windows shell delegation, and applications designed to escape process-tree ownership are not covered. The wrapper never sends input to a process outside its current job/private desktop, even if that process belongs to the same executable.

## Environment changes and privileges

No administrator rights, persistent Windows configuration changes, driver installation, global hooks, DLL injection, elevation/secure-desktop bypass, VM, or display reconfiguration were used. Build products and optional Qt runtime files are in this project. Standard build/download tools may maintain their ordinary caches; the reusable Qt setup script explicitly disables pip caching. The Qt package installation is local to `.deps/python`, not the system interpreter.

The browser uses a new profile under the ignored artifacts directory and a temporary loopback-only debugging endpoint. It does not reuse the user's profile or change the installed browser's configuration. Existing ordinary OS permissions must allow private desktop/process/job creation; a restricted host/session can fail, which should be reported rather than bypassed.

## Original manual verification procedure

1. Start `scripts\demo.ps1 -SkipBuild` and confirm the default EDIT control is selected.
2. Choose **Run typing test**. Switch to a disposable foreground text editor and type a recognizable paragraph for at least 15 seconds while moving the physical pointer normally.
3. Confirm the foreground application contains only your paragraph, without layer text or interrupted focus. Return to the viewer, stop automation, and refresh. Confirm the background application independently accumulated alphabetic text.
4. Select **Human control**, click inside the pictured edit area, enter a recognizable marker, and click its counter. Confirm the new state in the picture. Click the edit area again, then return to automation and send text. Confirm the original document and PID remain.
5. Hide the preview, continue the typing test, and show it again. Confirm the workload continued without continuous capture. Close the viewer and confirm its target processes disappear.

The original automated suite exercised the real viewer controls on a private QA desktop. The user subsequently reported personally performing the acceptance workflow using direct executable startup; see [MANUAL-ACCEPTANCE.md](MANUAL-ACCEPTANCE.md). The steps above remain useful regression instructions and are not a claim that every individual step was observed with automated telemetry.

## Best next experiment

With the user-reported foreground workflow accepted, expand the corpus using disposable WPF/Direct3D and Qt Quick applications, plus menus, modal dialogs, modifiers, shortcuts, and applications polling key state. Record per-operation success and actual pixels with the same scoped host. Try a Windows Graphics Capture adapter and compare inactive-desktop behavior with PrintWindow before changing the input architecture.

The positive evidence supports a lightweight desktop-local wrapper with explicit native/message and browser adapters. A failed rendering framework or shortcut should motivate a specific additional experiment; it would not invalidate the baseline or justify claiming universal interception. No privileged integration is needed for the next compatibility/capture experiments.

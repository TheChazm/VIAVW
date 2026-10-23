# Viewer timing and input research — October 21, 2026

The normal demo now uses verified mouse-only precise waits and faster owned-popup targeting. A five-repeat Tools/Options comparison reduced median popup-click handling from 106.4 to 26.9 ms; observing the actual Options dialog through the report fell from 187.3 to 110.0 ms. This is a menu improvement, not a demonstrated plain-typing improvement.

This investigation separates input forwarding, actual source-pixel freshness, normal viewer painting and diagnostic reporting. It also tests source-only rendering, read-only editor notifications, wait precision, and an optional mouse optimization. The final packaged executable, regression acceptance and cleanup status are recorded in `artifacts/vs-speed-20261021-evidence.json` and `...-final-audit.json`.

## What the timing measures

The new optional stage trace records timestamps in memory, bounds history to 8,192 entries, and exports after measurement. It distinguishes normal `WM_PAINT` from screenshot/print-triggered drawing. Pixel sampling occurs when the copied bitmap reaches the viewer, before accessibility inspection and report writing. Ordinary demonstration frames do not run this trace or the pixel probe.

The single-character test calibrates a mask from actual `//` and `//W` pixels, excludes near-full-height changed caret columns, resets the baseline, and measures seven individual W submissions per launch. Each tested mask retained 39 glyph pixels and required 30 changed pixels. Complete actual IDE text and the saved file are checked separately. Actual screenshots were inspected. This is a controlled glyph test, not a universal freshness oracle or a test of every character, font or input method.

| Workload and setting | Input forwarding median | Fresh bitmap median | Normal paint-handler end median | Report observed median |
| --- | ---: | ---: | ---: | ---: |
| 20-character burst, instrumented reporting control | 3.4 ms | 82.2 ms | 91.2 ms | 109.2 ms |
| Individual W, current-input control | 4.9 ms | 46.2 ms | 55.4 ms | 77.8 ms |
| Individual W, mouse-only candidate | 3.5 ms | 48.0 ms | 57.0 ms | 77.2 ms |

There are 14 samples per setting. The first and second rows are different workloads, not a before/after optimization. Earlier burst figures must not be interpreted as every key's latency. The mouse candidate does not establish a plain-typing speed improvement. Drawing the scaled image itself took approximately 6.7 ms in these single-character runs. Paint-handler return does not establish buffering/compositor completion or physical scanout. No foreground-session comparison or physical human acceptance was performed.

## Source-only observations

Seven source-only trials removed the viewer and status-report pipeline. Direct shared-DC polling received new glyphs at a median 59.5 ms for 20-character bursts. Another seven trials subscribed to the installed editor's standard UIA TextChanged event on a dedicated private-desktop MTA thread. Callbacks only recorded timestamps; no text reads or mutations occurred in callbacks. They started roughly 2.5–8.6 ms after submission, with 20 notifications spread over approximately 36–49 ms. Fresh pixels arrived at a median 62.3 ms with the observer enabled.

Registration, explicit text verification and PNG exports were outside measured capture loops. The observer was removed on its registration thread, recorded no dropped events, and both runs passed cleanup and physical-desktop monitoring. Notification receipt includes provider/marshalling delay; it is neither an exact source-processing timestamp nor a rendering acknowledgement. These observations narrow the remaining uncertainty without proving one Windows or WPF bottleneck. No application modification or VIAVW-specific application adapter was used.

## Changes investigated

**Capture-origin reuse:** carrying client origin in the capture descriptor removed a later RPC, but a 14-versus-14 comparison did not demonstrate a paint-latency benefit. Its experimental mode is retained separately and is not enabled by the normal demo.

**Asynchronous reporting:** a bounded writer retained one in-flight and one pending automatic snapshot, coalesced obsolete reports, and protected explicit inspection publication. Twelve component checks exercised deliberately stalled writers, failures, required-write ordering, bounded storage and disposal. In Visual Studio, report observation improved from 109.2 to 100.9 ms median, while bitmap and paint medians remained approximately 82 and 91–92 ms. A single 200-character, ten-second pair used 130.2% versus 154.1% of one logical core and sampled 249 versus 271 report publications. CPU covers the owned tree, not the whole machine, and one pair does not establish a general CPU effect. The earlier 22% workstation observation remains unexplained. This backend was not selected for the candidate.

Its 32-check editing suite passed. The mouse/edit/build workflow also completed all 15 mouse checks, but that command returned failure because physical-desktop activity made the strict isolation monitor inconclusive. The input desktop stayed unchanged and owned applications never became foreground; the origin of the other activity was not inferred. That run is not reported as a complete isolation pass.

**Wait precision:** on a private RPC worker, 24 alternating samples per duration measured requested 5 ms sleeps at a median 12.2 ms and 8 ms sleeps at 16.3 ms. Disposable one-shot high-resolution timers measured about 5.4 and 8.5 ms. These are primitive measurements, not compatibility proof. No system timer-resolution setting, periodic timer or spin loop was used. The worker's raw CPU accounting was below its reporting quantum, not proof of zero cost.

**Rejected keyboard shortening:** applying precise waits to keyboard and mouse passed basic editing but failed Ctrl+Tab in the longer real workflow. That failure remains in the G evidence. The candidate restores the verified keyboard `Thread.Sleep` pacing, including the separately preserved 30/50 ms function-key transitions.

**Mouse-only candidate:** one reusable, one-shot timer now optionally handles mouse pacing. It has no idle scheduling loop and survives worker restart through the existing session configuration. A second change omits the read-only text/press geometry lookup for points already classified as owned popups: that lookup cannot change the existing popup warmup decision. Live pointer hit testing and HWND/job/desktop validation remain. Capture cadence, source geometry, report writing and keyboard behavior retain the prior path. No extra input thread is introduced.

The repeated Options samples used the same preserved viewer executable with the optimization flags off/on. Control mouse handling ranged from 106.1–114.4 ms; optimized handling ranged from 25.5–41.3 ms. Native delivery medians were 48.1 and 24.7 ms, and the optimized popup path omitted the redundant geometry query. These are five samples per setting in separate sessions, not randomized or a universal latency guarantee. Other commands vary: the first Tools click was slower in the optimized full mouse run. A separate single sample of context-menu handling dropped from 1527.9 to 35.4 ms; intermittent long stalls are not established as universally eliminated.

## Compatibility, recovery and scope

The H candidate has passed the 15-check mouse/edit/build/handoff workflow, including the Ctrl+Tab case that failed with shortened keyboard waits, and the 32-check editing suite. Its 15 independent-capture checks include precise-mouse session recovery, ownership, worker restart, concurrent capture, separate maps, source origin and bounded stalled-capture containment. Twenty-five mapped-DC and 25 shared-transport checks passed, including exact pixels, popup composition, immutable frames and resource release. The full regression passed all 29 selected workflows and 66 bounded nested-menu paths, including real run/debug, designer and installed-template project creation. Both the optimized and same-build control mouse workflows passed all 15 mouse checks and full editing/build/handoff. The repeated menu comparison passed five real Options/Cancel cycles per setting, with exact document preservation and cleanup/physical monitoring. These checks cover selected behavior, not every Visual Studio command or workload.

The prototype uses the installed Visual Studio 2022 Community with normal packages and the existing isolated software-rendering profile. Its disposable project is an executable, with actual editing, saving, building, running and debugging workflows. Standard accessibility assistance for the previously tested WPF modal controls remains explicitly distinct from native mouse delivery. Native GPU-rendered VS capture, arbitrary extensions/workloads, IME/raw-input polling, docking and foreground-equivalent responsiveness remain unverified.

A short candidate typing workload used 1.11 CPU-seconds over 1.64 seconds (67.7% of one core) and recorded 3.16 GB of summed per-process peak working sets. These are neither unique/simultaneous RAM nor a sustained-typing before/after comparison. Human idle produced four frames over about two seconds; Automation idle produced zero. Each idle sample recorded 15.6 ms of viewer CPU and excludes source/helper CPU. No new GPU-utilization benchmark was collected.

Working binaries and matching source/PDB snapshots for A–H are retained under `artifacts/baselines/`; each check has zero source mismatches and one unavailable SDK virtual source. Failed and unselected experiments remain identifiable. Protected browser profiles were not deleted, and no Git operations or persistent system-wide changes were performed.

## Demonstration and next direction

The established launch command remains:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control** before clicking or typing in the picture. The manifest selects `Speed-20261021h-mouse` with SHA-256 `7D5519424F3C565FBFC346AE22D9E6C7CB461CC8D2C7E8B4EC01206D30A6EEF2`. Append `-PreviousInput` for the preserved AA/DC input comparison or `-PrintCapture` for the L printing build. The public responsiveness wrapper was run successfully against the newly selected backend; all four demonstration preparation modes passed.

The next performance target is the scaled drawing path and the remaining single-character render/capture scheduling interval. The read-only text-event experiment is also a useful basis for investigating functional input acknowledgements. Faster RPC/report acknowledgement alone must not be presented as faster application drawing. Keep the working private-desktop implementation while evaluating those alternatives.

Primary references: [cross-process performance timestamps](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps), [UIA event threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading), [TextChanged events](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.textchangedevent), and [one-shot high-resolution timer creation](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw). Performance and compatibility findings above come from the saved local experiments, not from API documentation.

Final audit: passed. No owned test processes remain; the previous AA/AB/L/B/Release hashes are unchanged. H and I source/PDB checks have zero mismatches, and 125 working project source files match I. All public script modes parsed/prepared and the public responsiveness wrapper passed 32 checks. Current-day experiment/build/snapshot roots occupy approximately 261.6 MiB including hidden files; older browser profiles remain untouched. No generated Git directories were found. See the audit JSON for exact scope and paths.


# VIAVW — Milestone 11: capture visibility and shorter inspection

October 14, 2026. The accepted Milestone 10 source, scripts, documentation and working binaries were preserved at `artifacts/baselines/accepted-hidden-input-20261014`, with 209 SHA-256 entries. The existing private desktop, input, ownership, viewer and capture backends remain available.

## What the experiments established

The initial question was whether Milestone 10's framework visibility pulse could supply native GPU pixels without showing the native window. Installed unchanged Chrome 155.0.8059.40 and Edge 155.0.4283.45 were operated on non-input private desktops, using the existing scoped native input and disposable passive WebGL page. No browser debugging control, source injection, software renderer override, global input or physical desktop capture was used by these mechanism/timing harnesses.

The Chrome matrix tested PrintWindow flags 0–3, WM_PRINT/WM_PRINTCLIENT, redraw plus full-content printing, window-DC copying and the rendering child window. The fully hidden variants produced untouched buffers, blank backgrounds or a failed BitBlt with Win32 error 6. The Intermediate D3D child also failed. The legacy renderer helper was absent while hidden. A first child comparison reused an obsolete handle and was rejected by subtree validation; the corrected harness rediscovers children before use. These results apply to the tested configurations, not every Windows renderer.

Temporarily setting the native `WS_VISIBLE` bit **plus** the framework visibility/activation notifications recovered actual GPU color and a fresh binary frame counter. Either change alone failed. The bit was restored in `finally`; source PID, original style and the accepted inspection path were preserved. This is temporary native visibility, not fully hidden GPU readback. It remains an internal research option and is never selected by the normal viewer.

Microsoft documents window styles, PrintWindow's synchronous behavior and GUI resource queries. Those facilities informed the experiments; the results do not establish undocumented compositor internals. Current Chromium code has a distinct framework visibility callback, consistent with the observed separation. [SetWindowLongPtr](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowlongptrw), [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow), [GetGuiResources](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguiresources), [Chromium window handler](https://raw.githubusercontent.com/chromium/chromium/main/ui/views/win/hwnd_message_handler.cc).

An initial report serializer failed on two names differing only by case, after process cleanup. That was corrected. Early matrix reports also exposed raw counter values from blank white pixels; the final freshness criterion requires both the expected GPU color and a counter strictly above the prior observation and no higher than the subsequent observation. Saved blank images and failed runs remain preserved.

## Timing comparison and implementation choice

Three methods were compared with waits of 0, 10, 30 and 150 ms: ordinary show/capture/hide, presented activation, and the temporary visibility-bit experiment. Six rounds rotated condition order. Every sample first changed the application's text and GPU color through native input. Real captured GPU colors and counters determined success; a successful API return was insufficient.

| Ordinary private inspection | Chrome median | Edge median | Fresh frames, Chrome / Edge |
| --- | --- | --- | --- |
| 0 ms wait | 21.6 ms | 21.1 ms | **4/6 / 5/6** |
| 10 ms wait | 34.6 ms | 36.0 ms | 6/6 / 6/6 |
| 30 ms wait | 69.9 ms | 69.1 ms | 6/6 / 6/6 |
| 150 ms wait | 176.9 ms | 180.3 ms | 6/6 / 6/6 |

Zero wait also failed in the other methods. In Edge, presented activation with no wait produced only one correct color and zero fresh counters out of six. A later Chrome repeat again reproduced zero-wait failures, while its 30 ms ordinary inspection median was 66.1 ms. The visibility-bit approach offered no meaningful latency advantage at equal waits, so no style mutation was added to the viewer.

The implementation now permits a bounded 0–500 ms inspection wait. The existing two-argument `InspectHiddenWindow` method remains present and retains its 150 ms default; an explicit three-argument overload selects a different wait. Worker-side validation repeats the bound. The viewer and scripts accept `--inspection-settle` / `-InspectionSettleMilliseconds`, also defaulting to 150 ms. Only explicit hidden inspection uses this setting; input ownership, ordinary capture and existing presented-input waits retain their behavior.

Thirty milliseconds is the demonstrated optional setting, with more margin than the small 10 ms sample. It is still empirical, not a rendering acknowledgement or a universal deadline. The implementation does not automatically select zero wait or retry input. A caller must assess actual output for its application.

## Verification and resources

Twenty-one simultaneous Chrome/Edge input-and-inspection bursts passed with a 30 ms wait. The tests verified actual text, changed GPU colors, advancing counters, independent desktops/jobs/owners, source preservation during worker recovery and continued peer operation after one source closed. Median complete input, observation and inspection was about **104 ms**, versus approximately 213 ms in the prior 150 ms run. Different runs and scheduling prevent treating this as a controlled throughput guarantee.

The actual multithreaded viewer also passed hidden field replacement, automation rejection under simulated human ownership, separate inspection and return to automation at 30 ms. The saved native screenshot was visually inspected and shows the correct field and GPU color. Physical human acceptance remains pending.

A separate Chrome activity comparison used paired **30/150/150/30 ms** blocks, eight real input-and-capture operations each. All 32 captures had correct fresh pixels. Across 16 operations per condition:

| Wait | Total activity time | Owned CPU time | Animation frames advanced |
| --- | --- | --- | --- |
| 30 ms | 2.20 s | 1,297 ms | 150 |
| 150 ms | 3.95 s | 2,641 ms | 328 |

Shorter presentation reduced rendering time as well as latency in this sample. The two 150 ms blocks themselves differed substantially in CPU cost, so these results are not a general CPU reduction guarantee. Individual owned GPU 3D observations ranged approximately 0.67–1.28% for 30 ms blocks and 1.60–1.73% for 150 ms blocks. Counter sampling may extend beyond a shorter activity block; these are not whole-device utilization figures. CPU includes owned browser/worker processes and passive page instrumentation, excluding the supervising harness.

Worker GDI objects remained at **3**, with successful queries, and private memory stayed around **33.3 MiB** across the four block snapshots. This is a small stability check, not proof against long-term leaks. GPU memory was not measured. Native bitmaps/DCs continue to be released per capture. No new graphics device, persistent capture loop or heavyweight service was introduced; the accepted reusable offscreen WGC backend remains separate. The original 22% CPU observation remains unexplained.

The complete required regression passed at `artifacts/development-20261014-182742` using `-SkipBuild -SkipBenchmarks`. Its 22 top-level reports passed, including accepted native/framework/viewer, ownership, recovery, save and launcher checks plus the 30 ms viewer and concurrent tests. The native core passed all 19 checks. The repeated fast concurrent run passed all 21 fresh captures, with median complete operation approximately 105.4 ms. Posted variants passed this run; their historical scheduling reliability issue remains unresolved. Builds had zero warnings/errors, scripts had zero parse errors, and source/build timestamps agree. Longer existing benchmark suites were not repeated; the new timing/activity measurements are separate.

Final audit: **zero owned experimental process survivors** and all **1,571** recorded files matching across nine preserved snapshots. **104 retained browser profiles** occupy **3,270.79 MiB (3.19 GiB)**. Six new regression profiles account for **153.51 MiB**; total growth since the previous audit is **245.97 MiB**, including changes in reused profiles. Nothing was deleted. Physical acceptance of the new setting remains pending.

## Working demonstration and next experiment

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput -InspectionSettleMilliseconds 30
# Optional: add -Browser edge
```

Use Human control to select/type in a field, return to automation, hide the app window and preview, then use **Replace hidden field**. **Inspect hidden app** should display the result, with `settle 30 ms` in its capture status. Compare with the default 150 ms launch. The native window is temporarily shown only on its non-input private desktop for inspection; this is not fully hidden GPU capture. Close normally. Physical acceptance is pending.

```powershell
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-hidden-gpu artifacts\new-hidden-gpu-comparison
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-inspection-timing artifacts\new-timing-comparison
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-concurrent-fast-inspection artifacts\new-concurrent-check
.\scripts\test-development.ps1 -SkipBuild -SkipBenchmarks
```

Evidence and final verification/cleanup totals are indexed in `artifacts/capture-timing-research-status.json`. Research uses the leased reusable project profiles. No profile deletion or policy bypass was attempted. No Git operations, unrelated project changes, production data modifications, drivers, privilege changes or persistent Windows modifications were made.

The next useful direction is an ordinary GPU application outside Chromium, or an application-specific readiness observation that can replace fixed waits without modifying the app. Preserve the working on-demand private presentation path: these failed hidden readbacks do not establish a universal Windows limit.

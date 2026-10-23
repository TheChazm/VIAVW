# VIAVW — Milestone 10: hidden input and framework visibility

October 14, 2026. The accepted Milestone 9 source, scripts, documentation and working binaries were preserved at `artifacts/baselines/accepted-activation-20261014` before changes, with 205 SHA-256 entries. Earlier snapshots remain preserved. No Git operations were performed.

## Experiments and discovery

Seven comparisons investigated why hidden Chrome accepted text but ignored shortcuts. An initial comparison showed that synthetic size notifications disturbed later input, so each final comparison received a fresh owned process. All reused a designated disposable profile. No debugging input, source hooks, extensions, page model writes or physical input were used.

| Hidden mechanism | Ctrl+A replacement | Accepted presentation afterward |
| --- | --- | --- |
| Activation pulse, sent keys | Failed; text appended | Recovered |
| Activation pulse, posted keys | Failed; text appended | Recovered |
| WM_SHOWWINDOW notification | Failed | Recovered |
| Synthetic minimize/restore size pulse | Failed | Did not recover in checked attempt |
| Size pulse plus activation | Failed; text appended | Did not recover in checked attempt |
| Window-position shown notification | Failed without activation | Recovered |
| Window-position shown notification plus activation | **Selected all and replaced correctly** | Recovered |

The successful combination sent `WM_WINDOWPOSCHANGED` with `SWP_SHOWWINDOW`, followed by the accepted inactive/active activation pair. It did not call `SetWindowPos` or `ShowWindow` to show the source. Windows reported the root hidden; the page reported itself visible and resumed animation. This deliberately separates framework visibility from native visibility. It does not emulate physical input devices.

Microsoft documents the `WINDOWPOS` structure and system-message marshalling. Current Chromium source handles position show/hide flags through a visibility callback and uses a separate size/minimize path. These sources informed the experiment; they are not a trace proving the installed build's exact dispatch. [WM_WINDOWPOSCHANGED](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanged), [SendMessage marshalling](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessage), [Chromium HWND handler](https://raw.githubusercontent.com/chromium/chromium/main/ui/views/win/hwnd_message_handler.cc), [Chromium desktop host](https://raw.githubusercontent.com/chromium/chromium/main/ui/views/widget/desktop_aura/desktop_window_tree_host_win.cc).

## Implementation and compatibility

`ResearchHiddenInteraction` is a separate internal experimental backend. It requires an initially hidden owned top-level source on a non-input VIAVW desktop. It validates all command targets and bounds before notifications, checks input ownership, serializes the transaction, sends native input, and restores a hidden framework notification in `finally`. Final cleanup also hides the source if the application made its native window visible. Existing job/worker deadlines contain failure. Ordinary viewer paths, text-only `SendActivatedInput` and private presentation retain their behavior.

The operation reuses existing private keyboard/button state and supports bounded text, chords and mouse sequences. Zero added settle time worked in the checked runs. Delivery is not an application acknowledgement: verification waits for actual text, selection, events and pixels.

Installed unchanged Chrome 155.0.8059.40 and Edge 155.0.4283.45, using NVIDIA hardware WebGL through D3D11, passed selection replacement, Ctrl+A, Shift+Tab, Space button activation, two-field state, Unicode, twelve cycles, owner rejection, worker recovery and resumed automation. Native field clicks, GPU button clicks and buffered slider dragging passed in both browsers in the final regression. Two independent Chrome/Edge layers passed 21 keyboard-only bursts, fresh GPU colors/counters, simultaneous different owners, peer activity during recovery, and closing one while the other remained usable. Foreign HWNDs were rejected. Handoff used simulated human ownership; physical acceptance is pending.

The first combined input/capture attempt **failed all 21 visual checks**. Input worked, but direct hidden PW3 captures were blank; a saved image was visually inspected. The working path sends captureless input and obtains native pixels separately through the accepted temporary private presentation. Private WGC and default opaque hardware WPF limitations remain unresolved.

An ordinary installed Character Map plus passive native EDIT/WinForms, layered WPF and Qt Quick fixtures passed hidden selection of `DEF` in `ABCDEFGHIJK`, then replacement yielding `ABCXYZGHIJK`. All eight direct/notification comparisons passed. These controls already accepted the existing messages without visibility notifications. WPF reported tier 2 and Qt Quick Direct3D11. This establishes those controls/configurations, not every application in those frameworks.

The multithreaded viewer gains an opt-in **Replace hidden field** action through `--experimental-hidden-input` / `-ExperimentalHiddenInput`. It sends Ctrl+A and toolbar text to the retained field. Hidden preview requests no capture; visible preview triggers separate inspection. Actual viewer controls on a private QA desktop passed replacement and owner rejection, and its screenshot shows the correct field and GPU color. The old controls remain available.

## Measurements and limits

The focused Chrome run measured 26 captureless shortcut/text activity transactions: median **9.46 ms**, maximum **12.90 ms** API time. Separate mixed input calls had median approximately **5.9 ms**. Input plus checked visual inspection had median **216.9 ms** over 25 samples; native capture rendering median **8.02 ms**. The initial concurrent run measured median **6.70 ms** input and **213.2 ms** including observation/inspection; worker recovery took about **130 ms**. These are local samples with warm reusable profiles, not latency guarantees.

Across 26 shortcut/text transactions in 4.12 seconds, owned CPU time was **2,609 ms**, about **2.64%** of this 24-logical-processor machine. An individual owned GPU 3D sample was **0.42%**. Animation advanced by 52 frames: even short pulses resume rendering. After settling, a 2.51-second idle interval used **15.6 ms CPU**, about **0.026%**, with unchanged animation and zero sampled GPU 3D activity. CPU includes browser/worker processes and passive test instrumentation, excluding the supervising harness. Engine percentages are not summed into device utilization. GPU memory was not measured.

This option improves input without native presentation; it does not eliminate rendering cost. Existing hidden text remains a useful lighter option when its focus is valid. The original 22% CPU observation remains unexplained. No continuous capture loop or new wrapper graphics device was added; reusable offscreen WGC remains separate.

The final repeat measured Chrome/Edge activity-call medians **12.71 / 13.50 ms**, with inspected operations **218.4 / 223.4 ms**. Over approximately four seconds, 22/24 transactions used **2,984 / 1,953 ms CPU** (about **3.09 / 1.95%**), with GPU 3D samples **0.48 / 0.57%**. Subsequent idle samples used **15.6 / 31.3 ms CPU** (about **0.026 / 0.052%**), unchanged animation counters and zero sampled 3D activity. Differing counts, scheduling and warmup prevent a controlled efficiency comparison. Initial concurrent memory observations were Chrome **362 MiB working set / 189 MiB private**, Edge **445 / 240 MiB**, and workers together **93 / 34 MiB**; shared working-set pages prevent treating these sums as unique RAM.

Synthetic size pulses remain unsuccessful research and are never selected by the viewer. Framework/native visibility disagreement can affect timers, focus, accessibility and application behavior. Raw input, asynchronous physical-state polling, complete IME behavior and continuous held virtual devices remain unsupported. Buffered dragging is a bounded message sequence.

## Demonstration and evidence

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput
# Optional: add -Browser edge
```

Take **Human control**, click the field and type some text. Return to automation, **Hide app window**, then **Hide preview**. Change the toolbar text and press **Replace hidden field**. **Inspect hidden app** should show the replacement. Taking Human control should reject that automation action; returning ownership should restore it without restarting the source. Close normally. Physical verification remains pending.

```powershell
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-hidden-shortcuts artifacts\new-comparison
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-hidden-frameworks artifacts\new-framework-comparison
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-notification-browser artifacts\new-notification-check
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-concurrent-browser-notification artifacts\new-concurrent-check
.\scripts\test-development.ps1 -SkipBuild -SkipBenchmarks
```

Useful evidence is indexed in `artifacts/hidden-input-research-status.json`, including retained shared-process and blank-capture failures. Research commands reuse leased disposable profiles; warm state is a confounder. Manual demos retain separate profiles. No profile deletion or policy bypass was attempted.

The complete required regression passed at `artifacts/development-20261014-152408` with `-SkipBuild -SkipBenchmarks`: accepted core/framework/viewer/lifecycle checks plus both notification browser tests, concurrent notification input and the optional viewer action. Builds completed without warnings/errors; scripts parse cleanly. Longer existing benchmarks were not repeated. All **1,362** recorded files matched across eight snapshots; this audit also includes the historical Milestone 2 snapshot omitted from the prior audit. The final project process audit found **zero owned experimental survivors**. Existing default opaque WPF negative controls and historical posted-modifier scheduling failures remain accurately documented.

The storage audit identifies **98 retained browser profiles**, totaling **3,024.82 MiB (2.95 GiB)**. Six newly created regression profiles account for **158.37 MiB**; total growth since the previous audit is **262.89 MiB**, including changes in reused profiles. No data was deleted. No unrelated project or production data, persistent system setting, driver, privilege boundary or Git state was modified. Physical interaction with the new action remains pending.

Next: investigate source-specific rendering/readback while native visibility stays hidden, or an ordinary GPU application outside Chromium. Preserve framework-specific choices rather than applying synthetic visibility to every app. Longer mixed-input/recovery runs and acknowledged processing would strengthen the new option before broader viewer routing.

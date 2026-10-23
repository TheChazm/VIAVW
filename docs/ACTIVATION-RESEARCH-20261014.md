# Retained selection, private activation and hidden text

Unchanged Chrome and Edge can now resume at their existing field, caret and selection without repeating a mouse click. An opt-in inactive/active window-notification pair restored the application's keyboard readiness. Plain text also works while their source HWNDs remain hidden; shortcuts still require temporary private presentation in the tested configurations.

The accepted private-presentation implementation was preserved first in `artifacts/baselines/accepted-private-presentation-20261014`, with 197 hashes and working binaries. Its original click/input/capture/hide path remains available. No Git operations were performed.

## Investigation and results

The earlier work showed that ordinary Chrome ignored text after hide/show despite retaining a Windows focus HWND. The first comparison tested the existing `ShowWindow` path, `SetWindowPos` hide/show without activation, private queue activation/focus and targeted activation notifications. All used one designated disposable Chrome profile rather than creating a profile for each trial.

| Mechanism after hiding | Text resumed in this Chrome configuration |
|---|---|
| Show without activation | No |
| Hide/show with `SetWindowPos` and no activation changes | No |
| Attached private `SetActiveWindow` / `SetFocus` | No |
| Clear/reassign private active and focus windows | No |
| Single active, click-active or inactive notification | No |
| Targeted inactive → active notification pair | Yes |
| The same pair while the HWND stays hidden | Yes, for text |

Before and after these operations, the observed Windows active/focus HWNDs were usually the same source root. The page's focus state and actual text behavior nevertheless differed. **A native focus handle is insufficient evidence of application readiness.** The successful pair restores tested framework behavior through ordinary window messages; it does not make the source the physical foreground application or create a virtual device.

Microsoft defines the [WM_ACTIVATE parameters](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-activate). Current upstream Chromium separately processes [activation notifications](https://github.com/chromium/chromium/blob/main/ui/views/win/hwnd_message_handler.cc) and [native-focus callbacks](https://github.com/chromium/chromium/blob/main/ui/views/widget/desktop_aura/desktop_window_tree_host_win.cc). That separation is consistent with the observations; it is not a trace proving the exact internal path in these installed builds. Chromium's alternative hide operation inspired a useful comparison but did not restore input here.

## Implementation and scope

`ApplicationLayer.PresentForInput(..., notifyActivation: true)` adds an optional `WM_ACTIVATE / WA_INACTIVE` then `WA_ACTIVE` pair before its existing bounded input transaction. The default remains false. It retains per-layer serialization, explicit sender ownership, private-desktop/job/subtree validation, native input, optional PrintWindow pixels and visibility restoration. `ActivationNotified` records delivered notifications, not application acknowledgement.

`ApplicationLayer.SendActivatedInput` sends the same notifications and **text commands only**, without requesting show or capture. It uses the same owned-source validation and command gate. A hidden source remains hidden; an initially visible source remains visible. If application behavior shows an initially hidden root, cleanup restores its hidden state. Chord and pointer commands are rejected before notifications in both manager and worker.

This restriction follows an actual failure. A hidden Ctrl+A produced no expected page selection; subsequent text was inserted into the existing field instead. `WM_CHAR` and key messages therefore have different demonstrated behavior in this configuration. Chords use the presented path, where Ctrl+A, Shift navigation, Tab and Space worked. No silent click replay, input retry, software renderer override, source hook, browser extension or source model write was added. Raw input, asynchronous device polling and full IME composition remain outside the demonstrated scope.

The multithreaded viewer adds **Send without clicking**. With preview enabled, it temporarily presents the source, restores logical activation, types at the retained selection, captures and restores visibility. With **Hide preview**, it uses the text-only API without source presentation. **Inspect hidden app** remains a separate on-demand visual operation. **Click and send privately** preserves the accepted explicit-click alternative. All these input actions still require automation ownership; human picture input and handoff are unchanged.

## Demonstrated compatibility

Installed Chrome **155.0.8059.40** and Edge **155.0.4283.45** both passed:

- Replacement of a selection at positions 3–6 in `ABCDEFGHIJK`, yielding native pixels showing `ABCXYZGHIJK`.
- Insertion at the retained caret, followed by Ctrl+A replacement through private presentation.
- Preservation of a second field while the first retained different text.
- Greek, Chinese and surrogate-pair emoji text while hidden, without IME emulation.
- Twelve repeated hidden text cycles, captureless text, ownership rejection, attention/handoff and worker recovery with the same source process.
- Keyboard focus navigation and a Space-activated GPU color button through temporary presentation.
- Correct GPU colors and advancing binary frame counters in native captures, with no additional mouse-down events after field selection.

The core browser harnesses use ordinary disposable pages that report state passively. They never send browser debugging commands. The actual viewer QA uses CDP exclusively to read source state; it operates wrapper controls through native messages on a private QA desktop. Its toolbar setup uses the wrapper's own native EDIT selection control. The viewer test confirmed retained-selection replacement and text insertion with preview hidden.

Chrome and Edge also ran simultaneously on distinct private desktops, jobs and profiles. Twenty-one checked keyboard-only activation bursts passed across the two layers. A retained simulated human ownership while B automated; A's worker restarted in about 147 ms while B continued. Closing A preserved usable B, and both closed gracefully. These are two products from the Chromium family, not proof of unrelated framework compatibility.

## Performance and resources

The first presented-path runs had median complete burst calls of **341 ms for Chrome / 344 ms for Edge**, including the two 150 ms settle intervals. Native capture rendering itself had medians of **9.24 / 9.99 ms**. Concurrent burst medians were approximately **342 ms for automation / 359 ms for simulated human ownership**. The fixed intervals remain empirical delays rather than processing acknowledgements.

Hidden text-only calls had medians of **4.21 ms** over 48 Chrome samples and **5.83 ms** over 47 Edge samples. These measure API round trips; the harness separately awaited actual page state. A later visual inspection took the usual temporary-show settle and capture time. Hidden text plus separate inspection had median checked operation times of about **216 / 228 ms** in those runs, with presented shortcut operations included in the sample set.

During separate four-second input-only intervals, Chrome accepted 28 commands and Edge 27 while their animation counters remained unchanged. Owned CPU time was about **1,109 / 1,063 ms**, approximately **1.13 / 1.07%** of this 24-logical-processor machine. Individual owned GPU 3D samples were **0.28 / 0.45%**. Stopping this animation does not eliminate all browser, input, caret, background or observation work. The page's passive instrumentation is included in source cost; the supervising harness is excluded. GPU engine samples are not summed into whole-device utilization.

The final repeat measured activation-call medians of **3.24 / 3.97 ms**. Once hidden focus was established, 29 ordinary text calls per browser worked without another notification or inspection between them: median API calls were **0.72 / 1.24 ms**, with **422 / 328 ms** owned CPU time over about four seconds and unchanged animation counters. The corresponding preceding repeated-activation phases used **922 / 781 ms** CPU time for 28 commands. Sequential order/warmup and differing command counts confound resource comparisons; this is a promising state-reuse observation, not a controlled efficiency guarantee. No automatic readiness cache was added, because Windows focus handles alone cannot validate that policy.

In the initial concurrent run, Chrome used about **482 MiB summed working set / 243 MiB private memory**, Edge **733 / 376 MiB**, and the two workers together **93 / 35 MiB**. Summed working sets include shared pages. GPU memory was not measured. Source processes and GPU contexts remain resident; no wrapper capture device or continuous rendering/capture loop was introduced. The original 22% CPU observation remains unexplained.

## Failures, verification and cleanup

The first hidden-key test failed on Ctrl+A; its reports preserve the inserted text. The text-only API now rejects that unsupported command instead of pretending that sending a message establishes compatibility. The initial full regression also stopped on a WPF observer race: its "before" text was empty, while the later text and selection were correctly `M3_SELECTION`. The harness now waits for the actual seed before testing selection, without resending input or weakening expected outcomes.

One long mechanism comparison had a pointer mismatch while its session last-input tick advanced. Those samples cannot identify the actor. Earlier and later focused Chrome, Edge and concurrent checks preserved sampled focus/pointer; hidden-input verification also samples each short input transaction. No physical cursor-writing or global-input API was added, and the mismatch remains recorded.

The complete required regression passed at `artifacts/development-20261014-135301`, using `-SkipBuild -SkipBenchmarks`. It includes all accepted core/viewer/framework/lifecycle checks plus activation-viewer, hidden Chrome/Edge text and concurrent activation tests. Longer existing benchmark suites were not repeated; new activity measurements are separate. Posted variants also passed this run, while their previously observed scheduling failures remain unresolved. Results and preserved failures are indexed in `artifacts/activation-research-status.json`.

Final audit: zero owned experimental app survivors, zero script parse errors, and all **1,035** recorded files matching across six preserved snapshots. New research profiles are fixed, project-owned disposable profiles protected by a per-profile lease. They are retained and reused, with warm-state effects disclosed. The expanded audit identifies **92** retained profiles totaling **2,761.93 MiB (2.70 GiB)**. Eleven profiles created during this turn account for about **348 MiB**, including five reusable research profiles and existing fresh-profile regression paths. Discovery through Chrome/Edge `Local State` files also found one historical 17.49 MiB demo root omitted by the earlier directory-name audit. No deletion policy was bypassed, unrelated project/data changed, Git operation performed, driver installed or persistent/privileged Windows modification made. Physical human acceptance of the new actions is pending.

## Working demonstration

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -VisibilityResearch
# The same private native path with installed Edge:
.\scripts\private-browser-demo.ps1 -SkipBuild -VisibilityResearch -Browser edge
```

Take **Human control**, select a field and part of its text, then return to automation. **Hide app window**, enter replacement text in the viewer toolbar and use **Send without clicking**. The original selection should be replaced, preserving the source PID. Then **Hide preview**, send more text and use **Inspect hidden app** when you want pixels. **Show app window** restores ordinary picture interaction. Close the viewer normally.

```powershell
.\scripts\test-development.ps1 -SkipBuild -SkipBenchmarks
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-browser-keyboard artifacts\fresh-activation-comparison
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-hidden-browser-keyboard artifacts\fresh-hidden-text
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-concurrent-browser-activation artifacts\fresh-concurrent-activation
```

The next useful experiment is hidden shortcut dispatch or a framework outside Chromium, with actual event/capture verification. Preserve explicit text activation and on-demand presentation as compatibility options; do not promote them to universal keyboard/device virtualization. Acknowledged application state, notification side effects and safe readiness reuse deserve further investigation.

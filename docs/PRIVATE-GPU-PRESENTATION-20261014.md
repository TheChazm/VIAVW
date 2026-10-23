# Private GPU presentation, input bursts and hidden execution

Later [activation research](ACTIVATION-RESEARCH-20261014.md) supersedes this milestone's click-restoration requirement for the tested Chrome/Edge activation path. The results below remain the original accepted experiment; its explicit-click API is preserved.

Unchanged installed Chrome now has a demonstrated **show → native input → native GPU capture → hide** transaction on a private Windows desktop. Two independent Chrome layers passed concurrent checked bursts, separate ownership and recovery. The source remains hidden between transactions, retains its process and WebGL context, and resumes for occasional interaction without switching the physical desktop.

The accepted concurrent/inspection implementation was preserved first at `artifacts/baselines/accepted-concurrent-inspection-20261014`, with 192 file hashes and working binaries. No Git operations were performed.

## What the experiments established

Chrome 155.0.8059.40 ran its normal hardware renderer with disposable profiles and the existing instance-only `--disable-backgrounding-occluded-windows` flag. A local WebGL page supplied passive state, visibility, input-event and animation observations. Native scoped messages/queue state supplied every application interaction; PrintWindow full-content supplied every source image. The research harness used no browser debugging protocol, extension, injected source code, software renderer override or pixel export. The test page is disposable instrumentation; Chrome's executable is unchanged.

The page draws alternating GPU colors and a binary frame counter. Eight hide/inspect/resume cycles verified both the requested color and an advancing counter in actual captured pixels. Hidden inspection restored source visibility afterward. The PNGs were inspected. Native Ctrl+A replacement and an ordinary HTML range drag also worked. The range reported pointer down/move/up and capture gain/loss through Chrome's event handling; this does not establish complete virtual pointer-device behavior.

While actually hidden with `SW_HIDE`, Chrome's animation counter stopped. Direct full-content capture returned the magenta diagnostic buffer, and targeted click/text did not change the page. Temporary private presentation obtained fresh GPU pixels. This extends the previously demonstrated ILSpy/WPF inspection mechanism to this ordinary GPU browser configuration.

The accepted launch flag matters. In a comparison without it, the HWND was visible but Chrome reported the page hidden, ran zero animation frames, ignored the baseline input and captured a white client surface. That failed run closed gracefully and is retained at `artifacts/m8-gpu-native-occlusion`. It establishes a failure for this installation/configuration, not every Chromium application or Windows rendering framework. Chrome documents background animation/timer behavior in its [background-tabs discussion](https://developer.chrome.com/blog/background_tabs); current local behavior was measured directly.

## Input needs an actual interaction

Showing Chrome alone did not restore text handling. Text-only and captureless text-only bursts failed. Root focus, the previously reported focus HWND, and clearing/reassigning private focus also failed to restore it. Windows' [SetFocus](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setfocus) result is therefore insufficient evidence of browser keyboard readiness. A renderer-child experiment stopped because that child was absent from the hidden window enumeration; cached children cannot be assumed available across visibility changes.

A burst that includes an explicit native click before typing works, including without capture. Failed focus variants were removed from the implementation, with their reports preserved. The wrapper does not silently replay arbitrary clicks or claim a complete virtual keyboard. The caller must choose meaningful native interactions and verify their effects.

## Implementation

`ApplicationLayer.PresentForInput` accepts at most eight text/chord/mouse commands, 4,096 total UTF-16 units and 32 total mouse steps. Every down must have a corresponding up; the final button mask is released. The existing layer gate holds ownership and serialization for the entire transaction. The worker validates all HWNDs, their job/desktop and membership in the presented source subtree before changing visibility. It checks visibility and revalidates each target before delivery. The source must be an unowned top-level window on a non-input VIAVW desktop.

If initially hidden, the worker shows the source privately, waits, delivers synchronous native input, optionally captures, and hides it in `finally`. An initially visible source remains visible. The default empirical settle interval is 150 ms before hidden input and 150 ms afterward. It is not an application acknowledgement. The API exposes a bounded 0–500 ms interval for research and optional capture; ordinary ownership rejection and the existing RPC fault-containment deadline remain in force. Input is not automatically retried.

The multithreaded viewer adds **Click and send privately**. It repeats the last picture click selected under human control, sends the toolbar text there under automation ownership, publishes the returned image and restores visibility. It does not reuse a drag as a new click. Native rendering/encoding timing remains available; the reported frame time includes the whole transaction. Normal input, inspection, ownership, shutdown and duplicate handling remain available. No resident capture loop or new wrapper D3D device/helper was introduced.

## Two independent GPU layers

`artifacts/m8-concurrent-browser-bursts` contains 21 verified bursts across two private Chrome layers. A used simulated human ownership while B continued automating. Each result checked its own text, GPU color, advancing pixel counter, preserved source PID and restored hidden state. Foreign HWND and non-owner requests were rejected before presentation.

Restarting A's worker took about 130 ms; A retained its hidden source, text and owner while B continued six checked bursts. Human-owned A input then worked, followed by return to automation and rejection of the former owner. Closing A preserved an independently usable B; both closed gracefully. Sampled physical foreground, pointer, input desktop and session last-input tick remained unchanged. These are automated ownership simulations; physical human acceptance remains pending.

## Performance and resources

The single-layer active-rendering experiment used six-second samples after a settle interval:

| Phase | New animation frames | Owned CPU time | Sampled owned GPU 3D engine |
|---|---:|---:|---:|
| Initially visible | 635 | 1,547 ms | 0.65% |
| Initially hidden | 0 | 1,391 ms | 0% |
| Reshown | 631 | 656 ms | 1.64% |
| Hidden again | 0 | 31 ms | 0% |

Hiding demonstrably stopped animation and almost eliminated this measured GPU workload. The warm CPU comparison is useful, but the first hidden sample still had substantial startup/background work. This is one instrumented workload, not a guaranteed saving for every app or an explanation of the earlier 22% observation. GPU values are individual 1.5-second engine samples, not summed whole-device utilization. Windows naturally reported the display off; no power state or policy was changed.

In that run, visible capture took about 24 ms round trip, with 17 ms native rendering and 4 ms encoding. Eight hidden inspections took 213–271 ms, including the 150 ms show settle. Default input bursts took about 522–527 ms in the interval comparison. Concurrent bursts had median round trips of about 506 ms for automation and 515 ms for simulated human ownership, including both settle intervals. Single 0/25/50 ms trials succeeded, but did not consistently become faster; they do not justify removing the conservative default.

One Chrome tree used approximately 413 MiB summed working set / 215 MiB private memory, plus a 40/12 MiB worker. Two trees used about 960/483 MiB combined, with about 92/34 MiB for two workers. Shared pages make working-set sums imperfect total-RAM measurements. GPU memory was not measured. Hiding preserves resident processes/context rather than reclaiming their memory; no WebGL context loss was observed.

## Verification, failures and cleanup

The full required regression passed at `artifacts/development-20261014-125622`, using `-SkipBuild -SkipBenchmarks`. It covers core isolation/fault containment, existing viewers, synchronous modifiers, pointer state, Qt GPU, three WPF modes, unchanged startup-adapter controls, ILSpy, ordinary Chrome, private input bursts, simultaneous browsers, reusable offscreen capture, worker recovery, helpers created during restart, save prompts and launcher/duplicate checks. Long existing benchmark suites were not repeated; the visibility/resource experiment above was measured separately. The new viewer control is exercised through the actual wrapper on a private QA desktop; that harness uses CDP solely to read source state. Wrapper textbox setup uses its own native EDIT selection control, not a Chrome input adapter. Posted modifier variants happened to pass this run; their previously observed scheduling failures remain unresolved. The viewer still uses synchronous chords.

An initial color assertion was too exact: requested orange 242/140/40 appeared as 234/136/56. The saved image was correct; subsequent checks use disjoint green/orange ranges and retain measured RGB values. A first regression stopped because the wrapper textbox's targeted Ctrl+A did not replace its content; the corrected QA setup selects its native EDIT text before typing. These failures, the occlusion comparison and focus investigations remain recorded rather than discarded.

Final regression, baseline hashes, owned-process cleanup and retained-profile storage are recorded in `artifacts/private-gpu-presentation-status.json`. The final audit found zero owned experimental app survivors, zero script parse errors and all 838 recorded files matching across five snapshots. All 80 identified disposable browser profiles are retained, totaling **2,396.37 MiB (2.34 GiB)**, up about 847 MiB from the accepted audit. Repeated fresh-profile trials generated most of that growth; future repeated runs should consider designated reusable disposable profiles with explicitly documented warm-state effects. No policy-blocked deletion was retried. No unrelated projects, production data, Git state, drivers, persistent system settings or privileged interception were changed.

## Try the demonstration

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -VisibilityResearch
```

1. Take **Human control**, click the page's text field and type. Try dragging the range control.
2. Return to automation, then **Hide app window**. Use **Inspect hidden app** to request a fresh frame while leaving the source hidden afterward.
3. Edit the viewer's toolbar text and press **Click and send privately**. It deliberately repeats the previously selected field click and inserts the text at that click position. Inspect again; the source PID should be unchanged.
4. **Show app window**, take human control and continue normally. Close the viewer normally when finished.

**Hide preview** suppresses optional burst capture; it does not stop the app. **Hide app window** is what paused this page's animation. Keep capture hidden for background work and request it when useful. Source visibility can affect application timers and behavior, so visibility suspension is not transparent for every workload.

Reproduction commands:

```powershell
.\scripts\test-development.ps1 -SkipBuild -SkipBenchmarks
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --research-private-browser-visibility artifacts\fresh-gpu-visibility
.\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --test-concurrent-browser-bursts artifacts\fresh-browser-bursts
```

The next promising experiment is a private activation/input-readiness mechanism that resumes keyboard interaction without re-clicking, plus a second ordinary GPU application. Keep hidden inspection and finite bursts as explicit compatibility options while that investigation proceeds. WGC private-desktop acquisition, opaque hardware WPF, asynchronous device polling, raw input/IME and continuous independent devices remain unresolved.

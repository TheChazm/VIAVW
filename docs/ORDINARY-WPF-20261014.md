# Ordinary WPF compatibility, viewer gestures and display-off capture

VIAVW now has an ordinary WPF demonstration using the official portable ILSpy 10.1 release. Its executable and DLLs remain unchanged. The accepted process-local .NET software rendering adapter supplies the compatibility preference; native PrintWindow supplies the pixels. This expands evidence beyond our own WPF fixture, while leaving default opaque hardware WPF capture unresolved.

Before development, the accepted implementation was copied to `artifacts/baselines/accepted-wpf-20261014`, with 179 SHA-256 records. No Git operations were performed.

## What was tested

The [official v10.1 release](https://github.com/icsharpcode/ILSpy/releases/tag/v10.1) is a .NET 10 WPF application. The runtime-dependent x64 ZIP was checked against the release digest `6528aa7d8a36e757afd50bf09338608a6029adcee7cd9392f7b541d3be566669`. Package, origin record and license are retained under `.deps/ILSpy-10.1.0.8386-x64` (about 14 MiB including extracted files). No installer was used.

Every launch uses ILSpy's documented `--newinstance` and an explicit configuration inside project artifacts. Update checks and previous assembly loading are disabled in that disposable configuration. The application only opens metadata from our disposable WpfProbe DLL, plus normal framework references; it does not run that assembly. Configuration isolation was checked against the [release source](https://github.com/icsharpcode/ILSpy/blob/v10.1/ILSpy/ILSpySettingsFilePathProvider.cs).

| Experiment | Actual result |
|---|---|
| ILSpy, default renderer, private desktop | PrintWindow client/full-content captures are white. Read-only accessibility still exposes the document. |
| Same release binaries, software startup adapter | Native client/full-content captures show real menus, search results and decompiled code. All 36 package application files retain their hashes. |
| Targeted input to ordinary ILSpy | Search text, Ctrl+A replacement, Down/Enter navigation, wheel scrolling and code selection work. Scroll percentage changes from 0 to 13.40; 6,108 sampled code pixels change. |
| Human/automation ownership | Same source PID survives the handoff. Human ownership accepts input; returning to automation rejects further viewer input. Direct automation resumes correctly in the separate layer test. |
| Actual multithreaded viewer | The same ILSpy interactions work through its picture, including buffered drag selection. Tests use a second private QA desktop. Physical-user acceptance remains pending. |

Accessibility is used only to observe resulting values, text, scroll percentage and selection. Input tests do not call UIA SetValue, Invoke, Select or SetFocus. The observer validates the owned HWND/job/desktop, limits traversal to owned processes, and bounds node count, depth, text lengths and elapsed work. Individual provider calls still rely on the existing RPC deadline for containment. [Microsoft's tree navigation documentation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/navigate-among-ui-automation-elements-with-treewalker) explains this observation mechanism.

## Implementation changes

The viewer's existing `-QueuePointer` option now buffers up to 29 pointer positions and delivers one serialized mouse sequence on release. This supplies functional text selection without holding button state across separate UI events. It preserves the start point and recent path when the buffer fills. Capture loss, preview hiding and ownership changes cancel pending gestures; an ownership generation also rejects an outdated queued gesture. Source feedback appears after release, so this is not continuous dragging or a complete virtual mouse device.

WPF viewer tests cover selection and cancellation in software, layered GPU and unchanged-baseline/startup-adapter modes. Launch, capture and source input retain the existing worker/thread architecture and ownership gate.

Native captures now report PrintWindow time separately from PNG encoding. The WPF benchmark records these stages and passive display/presence notifications for every first capture, plus frame freshness and retries. A dedicated hidden message-pump thread receives [session power notifications](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids). It never shows/activates its window, requests display wake or changes power policy, and unregisters/stops after the benchmark.

## Performance observations

The first instrumented run captured all 60 changed frames correctly with zero freshness retries while Windows reported session display **off** and presence **inactive**. Foreground and physical input tick stayed unchanged. The layered WPF process also had a positive 3D-engine counter sample, about 0.63%. This demonstrates useful private GPU capture while the display is reported off; it does not establish every locked/disconnected-session behavior.

| Native WPF fixture mode | Median capture | Median PrintWindow | Median PNG encoding |
|---|---:|---:|---:|
| Software, client flags 1 | 6.34 ms | 2.94 ms | 2.35 ms |
| Layered, full-content/client flags 3 | 14.61 ms | 9.57 ms | 3.46 ms |

Owned processes including test diagnostics used about 0.43–0.52% whole-machine CPU at idle and 1.32–1.48% during changed-frame activity on 24 logical processors. WPF source working sets were approximately 104 MiB software / 118 MiB layered. GPU engine percentages are short per-process samples, not whole-GPU utilization or GPU memory measurements.

A final repeat passed another 60 changed frames with zero retries and display still reported off: software/client median 6.18 ms and layered/full-content median 13.30 ms. Owned idle CPU was 0.57–0.73%, active CPU 1.23–1.53%. These measurements include diagnostics and do not show a resource optimization attributable to this change.

Ordinary ILSpy captures after input measured approximately 7–11 ms in the final standalone comparison. The earlier 130–140 ms layered latency and the user's original 22% CPU observation remain unexplained. There was no observed on/off transition in this run, so display state cannot be claimed as their cause.

## Failures and corrections

Two observer failures were preserved: inactive elements can have infinite/empty bounds, now recorded as null; AvalonEdit does not implement TextPattern.GetVisibleRanges, now recorded as unavailable. Its ScrollPattern and actual changed pixels provide scrolling evidence instead.

The first viewer harness read reports before startup/input completion. It now waits session readiness, then actual application state and ownership publication; a newer preview alone does not acknowledge a particular input.

Ordinary app startup also exposed the adapter's four-second deadline. Host traces reached CLR loading before the hook reported entry. A controlled repeat required **7.66 seconds** to finish startup, while the adapter itself took **32 ms**; source CPU during that wait averaged about **0.89%** of the whole machine. The deadline is now twelve seconds, still bounded and asynchronous. Reports publish atomically, and explicit request/progress/wait diagnostics distinguish startup from adapter work. [Microsoft documents the scoped host trace](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_host_trace). No global environment settings were changed.

Two early QA runs did not confirm graceful close within ten seconds; forced owned-tree cleanup succeeded. Instrumented and later regression runs closed gracefully. That earlier close behavior has no confirmed cause or claimed fix. Full failure references are in `artifacts/m6-failure-notes.json`.

## Demonstration and verification

From the project root:

```powershell
.\scripts\ilspy-demo.ps1 -SkipBuild
```

Allow up to the existing 25-second launcher deadline. Take **Human control**, click ILSpy's magnifying glass, type `ProbeWindow`, then Down/Enter. Close the search pane using its X; scroll and drag code to select it. Selection appears on release. **Return to automation** makes the picture read-only. **Hide preview** stops captures. Close the viewer normally; if it preserves a still-running source, inspect the status and retry closing. **Force close** remains available for this disposable session.

Automated commands are `--research-ilspy`, `--test-ilspy-viewer`, `--test-ilspy-launcher` (optional `trace`), and `--benchmark-native-wpf`, each with a fresh project artifact directory. `scripts/test-development.ps1` includes ILSpy checks when the pinned package is present. QA accessibility reporting is explicitly enabled only by the test harness, not by the normal demonstration.

The initial full regression at `artifacts/development-20261014-100408` passed core isolation, legacy viewer, keyboard/pointer, private Qt GPU, three WPF modes, adapter controls, ordinary ILSpy/Chrome, reusable GPU viewer, recovery and save tests; it stopped at the short ILSpy startup deadline in the launcher suite. After the correction, final adapter controls, ILSpy viewer, the complete launcher suite and all three WPF gesture modes passed under `artifacts/m6-completed-*`; the repeat benchmark passed too. The launcher checked the actual ILSpy script, visible startup, normal close, duplicate refusal, hidden/reveal behavior and responsive UI during a hung capture. Every retained baseline hash matches.

Final process cleanup and storage are recorded in `artifacts/ordinary-wpf-status.json`. No owned experimental application remains running. All 57 identified disposable browser profiles are retained: 1,496.14 MiB (about 1.46 GiB), up approximately 71 MiB during these regressions. Profile deletion was not attempted. No persistent Windows changes, drivers, security bypass, production data edits or Git operations occurred.

The next useful experiment is repeated concurrent ordinary WPF layers with mixed gestures and recovery, followed by comparing capture timing across naturally observed display-on/off states. The hardware opaque WPF route remains worth investigating separately. The software adapter is a useful compatibility option, not evidence of universal external-only GPU capture or physical input emulation.

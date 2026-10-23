# New Project loading screen and live preview

This is the historical M28 report. The subsequent [interactive latency revision](INTERACTIVE-LATENCY-20261016.md) replaces live PNG transport and whole-IDE per-click scans while preserving the asynchronous preview behavior tested here.

The reported New Project screen was reproduced in the preserved M27 viewer. Its frame version stayed at 11 for twenty seconds, and the initial/loading screenshots had identical SHA256 hashes. A fresh capture then showed the installed templates without repeating the New Project command. This establishes a stale presentation failure; the IDE's internal task sequence was not instrumented.

## Repair

The previous watcher detected window/caption/popup changes, which cannot establish that content inside an existing window has finished changing. The revised default VS viewer follows content for three seconds after commands/window transitions and keeps a low-rate live picture while Human owns a visible preview. The main window uses the same policy, so asynchronous work there can also become visible. The settling cadence is about 150 ms; the live cadence afterwards is 500 ms. Input-triggered refreshes remain immediate/coalesced and serialized off the UI thread.

Hide preview and Automation ownership suspend the live picture. The minimized viewer also suspends its live heartbeat. Explicit inspection remains available, and short startup/command settling work remains bounded. Full accessibility diagnostics are still requested separately through **Inspect details**; automatic frames do not run those scans.

Testing beyond the loading screen found two pointer issues. Early capture release did not activate Next, and it did not change the selected template. Text/button/list-item geometry is now obtained with a bounded, read-only UIA property query. The tested button and list-item presses keep the temporary private mouse capture through their transaction. Menus retain their existing early-release policy. No UIA Invoke, selection/value writes, application command IDs or injected application code perform these actions.

## Actual application checks

The installed VS 2022 private-desktop workflow now checks:

- Open New Project through the actual viewer, then leave it untouched for twenty seconds. The saved automatic preview shows loaded templates without Refresh.
- Hide and resume preview; return ownership to Automation and back to Human. Capture counters confirm the live picture pauses and resumes.
- Mouse-select the non-default **Blazor Web App** template, click **Next**, and verify configuration identifies that template.
- Cancel and retain the same IDE process and unchanged original source document.

New Project coverage ends at configuration and cancellation; project generation/restoration through this wizard remains untested. The existing disposable solution editing/build/menu workflow is verified separately. Physical human acceptance of this revision is pending.

The preserved negative runs remain under `artifacts/vs-m28-control`, `vs-m28-refresh` and `vs-new-project-20261016-201615`: stale preview, Next not activating, and button-only support missing template selection. The intermediate successful non-default-template run is `artifacts/vs-new-project-20261016-201736`. The final seven-check run is `artifacts/vs-new-project-20261016-202326/viewer/new-project.json`. All 13 existing mouse/menu checks, editing, IDE build, pointer recovery and handoff passed in `artifacts/vs-m28-mouse/vs-workflow.json`. Final runs, cleanup, hashes and baseline integrity are indexed in `artifacts/m28-verification-index.json`.

The updated responsiveness run (`artifacts/vs-responsiveness-20261016-202003/viewer/responsiveness.json`) passed ordinary/repeated/word/selection Backspace, Delete, exact saves, input rejection and same-process handoff. Eight single-character frame-report samples ranged from 78–139 ms, median **109 ms**. This remains substantially faster than the preserved M26 median of 963 ms; it is not physical input-to-monitor timing or a foreground-speed comparison.

In a two-second visible-Human main-window sample, four automatic frames were published and the viewer used 78.125 CPU ms (about 3.9% of one core). The matching Automation-idle sample published no frames and recorded zero viewer CPU ms at process-timer resolution. These figures omit source/helper CPU and GPU. An earlier twenty-second dialog sample measured 1,062.5 viewer CPU ms and 953.125 main-IDE CPU ms, including template loading; helper CPU and GPU were not sampled. Live inspection has a measurable cost, while background ownership can remain quiet.

## Run

Close the older viewer and run the rebuilt default:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
# Repeatable private-desktop check:
& "C:\Programming\Projects\VIAVW\scripts\test-visualstudio-new-project.ps1" -SkipBuild
```

Choose **Human control**, then File → New → Project. Templates should become visible automatically. The software-rendering experimental profile remains required. General virtual-device input and native VS GPU capture remain unresolved. The M27 source/executable snapshot is retained, and no Git operations, global input hooks, drivers, protected-profile deletion or unrelated-project edits were performed.

The final release build has zero warnings/errors. Audits found no surviving test IDE/wrapper processes, no script parse errors, and zero changed files in the accepted M26/M27 snapshots. This run retained about 22.9 MiB of evidence; the existing experimental VS profile totals about 145.6 MiB, including earlier activity. `artifacts/m28-snapshot.json` identifies the new source/executable snapshot and its SHA256 manifest.

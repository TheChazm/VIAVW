# Milestone 19 — Modeless windows, native menus and scaled input

October 15, 2026. **One qIV layer can now expose its main image, ordinary Help window, Directory panel, modal picker and native system menu through the viewer.** The main GPU session stays bound while auxiliary windows use native capture. Oversized previews shrink to fit, with pointer coordinates mapped back to the source.

## Investigation and implementation

Pinned **QuickImageViewer 3.2.0.452** supplies useful ordinary windows without modifying its executable. Its F1 Help and F6 Directory panels coexist with an enabled main window. Both returned readable native captures. The dedicated configuration, two disposable images, process-local cursor guard and optional GPU instrumentation remain the accepted mechanisms.

qIV's Ctrl+N implementation launches another executable without its dedicated configuration arguments. That route was **not run**: it could enter the application's normal registration/settings path. This milestone demonstrates multiple ordinary windows in one process, not multiple document processes or universal window compatibility.

`DemoForm` adds a **Window** chooser for the controlled qIV demo. It retains an explicit modeless selection, follows modal activation when the main window becomes disabled, and returns to the main image when the selected panel hides. Owner chains, process/job membership and private-desktop checks remain enforced. Modal interaction temporarily disables window choice. Native menus use validated GUI-thread menu ownership rather than treating the popup HWND as the keyboard destination. More than one visible menu popup is left ambiguous instead of guessed.

`InputCanvas` now shrinks oversized qIV previews. Clicks, wheel positions and buffered drag paths use the actual drawn rectangle to recover source coordinates. Background margins reject clicks; resize or ownership changes cancel buffered gestures. Other viewers retain their existing unscaled path. Demonstrated mappings include:

| Source capture | Drawn preview | Result |
| --- | --- | --- |
| Help, 664×801 | 528×638 | Click search and type a filter |
| Directory, 1920×120 | 1050×65 | Click the second thumbnail and verify the main image |
| Picker, 944×501 | 578×307 | Fill the filename field and open the selected image |

The smaller picker test resized only an owned viewer window to 780×620. **Windows display DPI was not changed.** This verifies preview scaling and coordinate routing; mixed-DPI monitor movement still needs separate verification.

## Native menu findings

Posted Alt+O and a posted click highlighted the picker's Organize control but did not produce its menu in these trials. The cause remains unisolated; this does not establish a general DirectUI or Windows menu limit.

Private queued **Alt+Space** did open the picker's ordinary system menu. Native Down/Up changed the actual highlighted item; rejected non-owner input left it unchanged. Read-only observations use [GUITHREADINFO](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-guithreadinfo), [MN_GETHMENU](https://learn.microsoft.com/windows/win32/winmsg/mn-gethmenu) and bounded item/rectangle queries. Captured pixels also showed the selected Move item. API success alone was not counted as interaction success.

Posted Escape removed the popup while GUI flags still reported menu tracking. Targeted `WM_CANCELMODE` cleared the remaining state. The viewer therefore handles Escape through its existing owned-menu cancellation operation, retaining the picker. No foreground change, desktop switch, physical pointer action, hook or driver was used.

**Demonstrated menu interaction is Up/Down and cancellation.** Pointer invocation, Enter activation, arbitrary command execution and submenu chains are not promoted. The viewer directs humans to arrows and Escape. Its menu picture is a separate view, not a composition of the application's windows.

## Actual viewer and ownership results

Both native-startup and GPU-startup workflows passed Help searching, scaled Directory selection, a picker above the modeless windows, native menu navigation, worker recovery with a menu open, owner changes, non-owner rejection, and scaled filename entry. The original source PID and main HWND survived. GPU reader/mapping identities also survived auxiliary-window interaction.

Two independent viewers held native menus open simultaneously on distinct private desktops, one using native startup and one GPU sharing. Each completed its own window/input sequence. Resizing an armed viewer drag released capture without changing the source image's measured fiducial position.

Human senders in these tests are automated. **Physical human acceptance remains pending.** The existing private-desktop implementation remains available.

## Snapshot publication failure and correction

An initial concurrent run restored the main picture but left its passive QA JSON showing Help; newer main-window metadata remained in a temporary file. The old publication path swallowed filesystem failures. The exact original error was not logged, so its precise cause is not proven.

Controlled tests subsequently reproduced access-denied results from rename-overwrite while a reader held the target, transient missing-name observations during replacement, and a fragile GDI+ filename reader during a later regression. The saved PNG was valid when inspected afterward. These were evidence-publication/read failures, not demonstrated source-image corruption.

`SnapshotFiles` now uses replacement of complete temporary files, short sharing-conflict retries, bounded read retries and image decoding from a shared byte snapshot. Access-denied replacement errors are retried only when an independent non-writing open confirms a sharing conflict. Permissions are never changed. Persistent or unrelated errors propagate, and viewer publication errors are logged instead of silently hiding stale reports. [ReplaceFile documentation](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew) describes its access requirements and failure cases; no durability or cross-file PNG/JSON transaction guarantee is inferred.

Fault tests passed temporary-reader recovery, bounded persistent-lock failure with the old document retained, 20 concurrent JSON replacements, 20 image replacements with valid decoding, and immediate propagation of an unrelated missing-directory error. Retry loops are bounded; filesystem I/O itself still follows Windows behavior.

Other retained failures include the initial assumption that Escape fully exited menu mode, a harness lookup using the wrong case for the WinForms ComboBox class, and early direct runs that reached their graceful deadline while the picker remained open. The direct harness now waits for picker closure, with an ordinary Cancel-button fallback if it remains open. In the final run Escape completed closure during that wait. Failed traces remain under `artifacts/m19-*` and `artifacts/qiv-window-research-20261015-103432`; none were erased or counted as passing acceptance.

## Measurements and verification

Primary evidence: `artifacts/qiv-window-research-20261015-104704`. The five-case suite passed **151 assertions and 68 named viewer frames**. Publication tests observed 299 complete JSON reads and 20 successful image decodes during their respective replacement loops. A persistent reader lock failed after 149.3 ms; unrelated filesystem failure propagated in 0.18 ms.

Single-viewer active workflows measured **3.35% machine CPU over 6.20 s** with GPU startup and **3.21% over 6.42 s** with native startup. These include the QA supervisor and pacing, while the replaced worker is excluded by creation identity. They do not rank backend efficiency. Concurrent reports include a shared supervisor and must not be added as independent layer totals.

Source private memory was approximately 101–105 MiB; the GPU reader approximately 51 MiB. Targeted viewer pings after the workflows returned within 0.334 ms across 32 samples. GPU resumption retained one reader device, one shared-resource opening and one staging creation at 640×360. Process GPU-memory counters remain allocation observations, not whole-device utilization. The historical 22% CPU observation remains unexplained.

Across the four viewer runs, median capture transaction times were 21.6 ms for Help, 10.8 ms for Directory, 8.5 ms for the menu and 23.2 ms for the picker. These cover native rendering/encoding transactions, not full input-to-verified-picture latency. The test mixes single and concurrent workflows and is not a general capture benchmark.

New and regression results are indexed in `artifacts/m19-verification-index.json`; final process, storage, build and baseline checks are in `artifacts/m19-final-audit.json`. Accepted M18 code and executables were saved before edits at `artifacts/baselines/accepted-dialog-observation-20261015`, with hashes. No Git operation, installation, privileged change or profile-deletion attempt was performed.

All **20 regression cases passed**, including prior dialogs/readiness, guard/native startup, mpv/D3D viewers, WPF/Qt fixtures, ownership, recovery and save handling. All final direct/viewer workflows closed gracefully; the new suite produced no publication-error logs. Final audit found **zero owned process survivors and zero mismatches across 3,349 files in 17 baselines**, with no parse/build-freshness issues and unchanged pinned executables. Browser profiles remain **123 / 3,976.239 MiB**, with none added or deleted. Retained large inputs total **48 PNGs / 20.314 MiB** and retained Y4M media **74 files / 243.916 MiB**, including earlier runs.

## Demonstration and next experiment

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\demo-qiv.ps1 -SkipBuild
# Guard-only/native startup also works:
.\scripts\demo-qiv.ps1 -NativeStartup -SkipBuild
# Reproduce this milestone:
.\scripts\test-qiv-windows.ps1 -SkipBuild
```

Toggle **Help** and **Directory**, then select them in the **Window** list. Take **Human control**, click Help's search field and type; select Directory and click a thumbnail. Select the main image and return to automation before opening the file dialog. Choose **Automation: window menu**, take Human control, try Up/Down and Escape. Resize the viewer and try the filename field in its smaller picture. Close the viewer afterward.

Next, investigate explicit selection of several document windows and native menu command/submenu behavior in another disposable ordinary application. Preserve the current mixed capture approach and verify DPI transitions separately; current evidence does not establish universal input devices, composed desktops or arbitrary GPU-root replacement.

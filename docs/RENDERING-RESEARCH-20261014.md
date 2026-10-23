# WPF rendering paths and process-local compatibility research

The unchanged WPF executable from the accepted snapshot now produces native private-desktop captures through an opt-in .NET startup adapter that selects software rendering. No executable/DLL patch or cooperative pixel-export API is involved. Separately, WPF's own layered-window mode produced changing native GPU pixels on a private desktop. The ordinary opaque hardware WPF path remains blank.

The accepted Chrome/Qt/D3D implementation remains available. It was preserved in `artifacts/baselines/accepted-private-gpu-20261014` before these experiments; its 147 recorded hashes are checked separately from current development.

## What the experiments established

| Experiment | Observed behavior |
|---|---|
| Default opaque WPF fixture | Blank native client pixels with PrintWindow flags 0–3, direct print messages, window DC copying and explicit repainting. Normal control state still changes. |
| WPF process software preference | Native PrintWindow client capture and window DC copying show the actual controls and changed marker. Direct print messages still leave the diagnostic magenta buffer untouched. |
| WPF `AllowsTransparency` / layered fixture | Full-content PrintWindow flags 2/3 show changing native pixels. Owned WPF 3D-engine activity was measured; the render preference remains Default. Original PrintWindow flags 0/1 remain blank. |
| Layered plus software preference | Native full-content captures work. This diagnostic variant is not a separate universal compatibility claim. |
| External opaque layered style | WPF removes the requested style; no useful pixels. Source text survives. |
| Ordinary or layered wrapper-owned parent | Reparenting and restoration preserve the tested source text but do not recover useful opaque WPF pixels. These experiments remain internal research operations. |
| Unchanged accepted WPF binary + startup software adapter | Default control is blank, selected adapter gives green/orange native pixels, deliberately mismatched target remains blank and unapplied. Input and same-process ownership handoff work. |
| Installed Firefox 158, fresh private profiles | Native capture eventually shows the browser, page and first-run terms dialog. That dialog blocks page input, and WebGL reports disabled. The native input/GPU experiment is incomplete. Default and software-profile trials are preserved. |

The fixture software/layered switches are application changes used to isolate mechanisms. The startup adapter is different: the same preserved executable and DLL run in each control, with their hashes unchanged. It is still explicitly **in-process compatibility code**, not external-only GPU capture. It removes the need to add a VIAVW rendering flag to that tested executable, at the cost of software rendering.

The WPF source handles `WM_STYLECHANGING` by removing externally requested layered composition when its own per-pixel-opacity mode is absent. That matches the experiment. Its HWND message paths did not reveal a generic supplied-DC printing implementation. These source observations help explain the results; they do not prove that every WPF application will behave identically. [WPF HwndTarget source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs), [Microsoft WM_PRINTCLIENT contract](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-printclient).

No Firefox terms were accepted on the user's behalf. Its blocked page clicks are not evidence that Firefox generally rejects native virtual input. WebGL's disabled result also needs separate diagnosis after first-run setup; its cause was not established.

## Implementation and boundaries

`WindowCaptureResearch` adds internal, bounded capture/repaint/style/parent experiments. Worker validation checks HWND/job/desktop ownership. Presentation mutations require a non-input `VIAVW_` desktop and serialized automation ownership. Parent experiments match DPI context, restore the source parent/style/bounds, and avoid destroying a parent while the source remains its child. They are not enabled by normal demonstrations.

`WpfRenderHook` uses the documented .NET startup-hook contract and the public `RenderOptions.ProcessRenderMode` preference. The wrapper supplies environment variables only when launching the selected owned process. Exact executable matching and project/artifact scope guards limit the prototype. The hook clears its inheritance variables within that process, ignores mismatched targets, and catches preference-setting failures. Viewer startup verifies the adapter's PID and applied result before reporting ready. [Runtime startup-hook contract and caveats](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md), [Microsoft process render preference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode?view=windowsdesktop-10.0).

The current adapter is limited to disposable framework-dependent .NET 10 Windows Desktop applications within this project. Legacy .NET Framework, other runtime versions, self-contained/trimmed/AOT applications and independent third-party WPF applications have not been verified. Applications may override the preference later; software rendering can change performance and feature availability. This trusted startup DLL executes inside the application. It is an explicit option, not the default capture backend, and does not install a global hook or change registry/system environment settings.

The existing multithreaded viewer and ownership gate are retained. QA preview publication now includes a generation number. Tests wait for published images and compare actual editor-region pixels, rather than equating control telemetry with a fresh image. Desktop-local client-origin queries support those checks. A disposable loopback page server supplies passive Firefox observations and has no browser control endpoint.

## Measurements and freshness

Each activity benchmark used 12 changed-frame controls per capture mode. Both runs passed all final color checks.

| Measurement | Earlier run | Later repeat |
|---|---:|---:|
| Software WPF, original PrintWindow client median | 5.66 ms | 7.12 ms |
| Layered WPF, full-content whole-window median | 139.15 ms | 16.91 ms |
| Layered WPF, full-content client median | 129.67 ms | 16.90 ms |
| Layered full-content client p95 | 220.99 ms | 21.19 ms |
| Software owned/test CPU, five-second idle | 0.62% whole machine | 0.65% |
| Layered owned/test CPU, five-second idle | 0.39% whole machine | 0.68% |
| Software activity CPU | 1.14% whole machine | 1.49% |
| Layered activity CPU | 0.85% whole machine | 1.09% |

The large full-content timing difference was observed before any optimization of that capture mechanism. Its cause remains unknown. These results should not be reduced to a single stable latency claim. The later measurement records session input timestamps; those changed over the entire benchmark, without establishing causation. No display/power state was forced.

The layered application's sampled owned 3D-engine activity was about 0.48% and 0.67% in the two runs. The software source had no positive owned GPU-engine sample. These are short per-engine intervals, not total GPU utilization. Earlier source working sets were about 104.5 MiB software / 121.2 MiB layered, with 50.5 / 64.7 MiB private bytes. CPU includes passive fixture updates and test diagnostics, and excludes unattributed Windows/DWM work. The historic 22% observation remains unexplained.

One immediate changed-frame control required a retry in the earlier benchmark. Early viewer evidence also retained old text despite updated control state. The revised check required up to two additional refreshes in one run; the final three-mode viewer run needed none. The final PNGs show both resumed automation text and the changed/scrolled controls. There is still no generic application/compositor acknowledgement; refresh again if an inspection appears behind the latest action.

## Verification, failures and cleanup

The expanded regression suite passed in `artifacts/development-20261014-084931`, including all 19 native checks, legacy viewer, modifiers, pointer state, private GPU Qt/Chrome, all three WPF viewer modes, startup-adapter controls, reusable offscreen capture, recovery, save opportunity and launcher/duplicate checks. Final adapter-result validation was followed by successful targeted viewer and actual launcher runs in `artifacts/m5-wpf-final-viewer` and `artifacts/m5-final-launcher`. Builds completed with zero warnings/errors.

Capture/style evidence is in `artifacts/m5-wpf-capture-final`; performance runs are in `artifacts/m5-wpf-native-performance-first` and `artifacts/m5-wpf-native-performance-repeat`. Human/automation routing, Ctrl+A, Ctrl+K, clicking, 48 pixels of wheel scrolling, wrong-owner rejection and continued use of the same PID were verified through the actual viewer on private QA desktops. Physical-user acceptance is pending.

Failures remain recorded: applying external layered attributes to an already per-pixel layered WPF window disrupted an early aggregate trial; later experiments used fresh default sources per mutation. Initial viewer assertions wrongly assumed clicking placed the caret at the end. A coordinate query from the wrong desktop failed and was moved into the validated desktop-local worker. The first hook guard rejected a valid path because of a duplicate trailing separator. All were corrected or isolated; the original evidence remains.

One core run in `artifacts/development-20261014-084436` failed a strict pointer sample: `(1759,924)` became `(1758,925)` during a routed click, with additional movement between sampled actions. Its cause was not established. No cursor-setting/global-input API was introduced. The subsequent strict run passed, including unchanged foreground, pointer and last-session-input timestamps across its routed-command samples. The failed check was retained rather than weakened. Read-only input-time diagnostics were added; they do not identify the source of an input event. [Microsoft GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo).

Final process, storage and baseline verification is recorded in `artifacts/rendering-research-status.json`: zero experimental process survivors, all 147 accepted-snapshot and 134 Milestone 3 hashes matching, and zero PowerShell syntax errors. All 53 identified disposable browser profiles across project artifacts are retained, totaling 1,425.00 MiB; this includes prior milestones. Per-profile storage is in `artifacts/m5-retained-profile-storage.json`. No deletion attempts, Git operations, unrelated project edits, drivers, privileged changes or persistent system configuration were performed.

## Demonstrations

From `C:\Programming\Projects\VIAVW`:

```powershell
# Unchanged accepted WPF application, opt-in process-local software adapter.
.\scripts\wpf-hook-demo.ps1 -SkipBuild

# Diagnostic fixture alternatives; neither uses the pixel-export adapter.
.\scripts\wpf-demo.ps1 -SkipBuild -Software
.\scripts\wpf-demo.ps1 -SkipBuild -Layered

# Existing ordinary Chrome private GPU demonstration remains available.
.\scripts\private-browser-demo.ps1 -SkipBuild

# Current regression suite, without repeating benchmarks.
.\scripts\test-development.ps1 -SkipBenchmarks
```

Take Human control, click the editor, type, try Ctrl+A and Ctrl+K, increment the counter and wheel-scroll the lower editor. Return to automation and verify picture typing is rejected; use Automation: send text to continue in the same app. A click positions the caret where clicked, so resumed text may appear inside an existing string. Refresh again if necessary and enlarge the viewer for its native-size picture. Close the disposable controller normally. Do not treat these instructions as completed physical-user acceptance.

For Firefox, first-run consent remains a manual decision. Its existing test profiles and screenshots are retained; the research command is intentionally not a required passing regression test.

## Next direction

Test the process-local fallback against another safe, unmodified modern WPF application and assess features that software rendering cannot preserve. Investigate capture timing under recorded display/power conditions without changing the user's physical display. The opaque hardware path and Firefox's post-setup WebGL/input behavior remain worthwhile separate investigations. Prefer these bounded comparisons before considering drivers, system-wide interception or changes to security boundaries.

# Visual Studio workflow research — 17 October 2026

Seven selected automated suites passed against the installed Visual Studio 2022 Community 17.14.36203.30. The runnable demo, verified launcher manifest and [evidence index](../artifacts/m30-verification-index.json) are available. Physical human acceptance of this revision remains pending.

## Verified scope

| Check | Result |
| --- | --- |
| IDE workflows, including 13 top-level menu open/dismiss cases | 29/29 passed |
| Nested menu paths, bounded to depth three | 66/66 opened and observed; leaf commands were not executed by this inventory |
| Mouse interaction, including actual selection deletion/save/undo | 15/15 passed |
| Rapid editing, deletion, ordering and ownership | 32/32 passed |
| Pixel transport, resize, isolation, failure recovery and release | 25/25 passed |
| Worker restart during a real disposable helper spawn | 3/3 passed; input resumed and state/ownership survived |
| Actual captured glyph measurements | Seven copied-mode and seven direct-mode bursts; text and final saved file checked |

The [selected-suite result](../artifacts/vs-m30-final-selected-verification.json) records each invocation. The [complete workflow report](../artifacts/vs-m30-final-complete-3/end-to-end.json) includes actual build/run/debug, designer and new-project results. This does not certify every Visual Studio command, extension or workload.

## What changed

The old demo built a class library. Building that project was valid, but F5 could not start it. The new demo is a disposable .NET Framework 4.8 Windows executable, with source files, debugging symbols and a small form. The installed, unmodified Visual Studio 2022 Community executable remains the compatibility target.

The viewer now offers an explicit window selector for the IDE, its owned windows and applications launched into the same layer. Every destination remains restricted to the layer's job and private desktop. Closing a debuggee or dialog between enumeration and capture requests another presentation observation; it never replays input.

Function keys now reach the source application. Debugger experiments verified F9, F5, menu stepping, F10, continue and Shift+F5. Simply posting an unmodified function key did not reproduce the required behavior. The successful tested combination uses the private input queue's key state and ordinary key messages. The experimental F10 system-message variant failed and was removed from the default path.

A persistent connection to the selected private GUI thread replaces repeated input-queue attachment for this viewer. Commands explicitly establish their idle/key state. A held thread handle protects against thread-ID reuse; switching to another GUI thread releases the previous connection, and shutdown closes the connection and handle. A deliberately seeded Shift state in the disposable QA queue is included in the visual latency test. This does not emulate Raw Input or `GetAsyncKeyState`, and does not attach to the physical input desktop.

Adjacent text waiting behind a command is batched into bounded literal-character requests. Navigation, shortcuts, pointer actions, ownership changes and other commands end a batch. This reduces round trips while preserving character order and the existing per-character acknowledgement count. Text, Enter and deletion still require actual editor/file verification.

## Capture and timing

The live viewer can render directly into its reusable mapped pixel buffer. The reader retains its own bitmap, so a later capture or mapping resize cannot overwrite the image being painted. The earlier copied-buffer implementation remains available as a comparison. Exported screenshots remain PNG.

The new visual benchmark waits for new glyph pixels in a small region beyond an initial two-character comment, then separately checks the actual editor text and saved file. The final caret lies outside the measured region. Seven small bursts are measured; this is captured visual content, not physical display scanout or a universal latency guarantee.

Early runs exposed a material qualification to the earlier approximately 60 ms figures: forwarding and publishing a frame can finish while that frame still contains old text. Actual glyph feedback in the initial comparison ranged from roughly 125 to 500 ms. Faster polling alone did not establish foreground-equivalent speed. Direct rendering, bounded refreshes after input, batching and input-state experiments are measured against that stronger criterion.

The final paired comparison used the same viewer build and seven 20-character bursts per mode:

| Captured glyph feedback | Median | Range |
| --- | ---: | ---: |
| Copied pixel buffer | 234 ms | 154–249 ms |
| Direct mapped pixels, default | 156 ms | 108–203 ms |

These small samples do **not** establish a stable latency advantage for direct mapping: the preceding paired run measured medians of 154 ms copied and 172 ms direct. Direct mapping removes a pixel-copy step and passes transport checks, but source rendering, capture scheduling and input serialization still matter. This revision does not meet native-speed acceptance. An earlier diagnostic-heavy editing run recorded command-report waits up to 1.05 seconds despite correct text and ordering. The final five immediate Backspace/typing trials measured 72–108 ms, but do not rule out those outliers under other conditions. Some standard accessibility modal actions took approximately four seconds. Their focus/action costs need separate profiling.

During 3.05 seconds of measured typing activity, the sampled owned process tree used 2.94 seconds of CPU time: 96.4% of one core, **not** 96.4% of the workstation. The preceding run measured 43.4% of one core; these short samples include IDE/helper work and do not establish lower total CPU consumption. The sum of owned processes' peak working-set counters was 2.96 GiB, including the IDE, helpers and QA viewer; those peaks need not have occurred together. A two-second Human idle sample produced four frames and 109 ms of viewer-process CPU; Automation idle produced no frames and zero measured viewer-process CPU. Those idle CPU counters exclude the worker and IDE. The earlier reported 22% workstation utilization remains unexplained.

After the full workflow, the IDE itself held about 1,479 MiB of working set. Three owned LayerHost processes held 332 MiB combined; this includes an additional QA worker, so deployed wrapper overhead was not isolated by that sample. WebView2, Roslyn and other IDE helpers account for further memory. The test tree's total memory is not the wrapper's memory alone.

Explicit repaint and private activation experiments are retained separately. They must not be described as successful speed improvements merely because their API calls returned. CPU observations which include accessibility scans and PNG evidence are distinguished from idle or sustained typing costs. GPU usage is not independently instrumented by these tests.

## Input routes and evidence

The harness sends input through the real viewer canvas on an outer private desktop. Its accessibility observations are passive. The viewer additionally has a generic standard-accessibility input route for eligible WPF modal controls: Invoke, Toggle, SelectionItem.Select and dropdown expansion, with an attempted private control focus. Dropdown assistance applies to the arrow region, preserving editable text-field clicks. These actions are recorded separately from native mouse packets. A failed action is not retried natively because its effect may be uncertain. Native text editing, gestures and menu input remain available; the native-only modal comparison has its own viewer entry point.

No Visual Studio plug-in, injected code, driver, global hook, desktop switch or physical input injection is used. Application transparency here means the installed IDE remains unmodified; it does not mean every input uses a physical-device-equivalent path.

The menu inventory distinguishes finding/opening a menu from executing its commands. It visits bounded visible popup paths, records enabled states, and does not execute Git, account, installation, publishing or external-service commands. Popup HWND accessibility providers sometimes expose only the first rows, while the general IDE snapshot can fill its 200-node bound before reaching the last menu items. Explicit inspection now adds a bounded query for visible menu items. Combining those observations with the actual topmost popup bounds and excluding the underlying menu bar avoids mistaking a sibling or covered menu for a child. Ordinary live frames do not make that additional query.

Failed runs are retained. They exposed premature submenu observations, underlying menu-bar entries overlapping tall popup bounds, remembered wizard options disabling fields, and search-history suggestions being mistaken for template cards. The generated project initially inherited VIAVW's own build properties and failed with CS8630; disposable project roots now isolate those properties. Native modal input did not reliably operate all WPF controls, so the successful assisted route is reported explicitly. Repaint and private activation experiments did not establish a general speed improvement and remain outside the default path.

The combined menu/project run also exposed an input-context dependency: three Escapes closed a three-level popup but left the Extensions menu bar focused. The subsequent New Project shortcut did not open the wizard. The driver now sends an additional Escape and verifies that menu focus has ended before issuing the shortcut; it does not retry New Project blindly. Menu discovery and the later project workflow remain separate assertions.

The workflow suite covers editing and deletion, undo/redo, exact saves, file switching, Find, Go To Line, build/run/debug, child-window interaction, top-level menus, Options, clean/rebuild, Configuration Manager, tool windows, compilation failure/repair, handoff, form creation/design/code switching, and project creation/building. The evidence index records the final outcomes, including failures. This is substantial selected workflow coverage, not exhaustive acceptance of every Visual Studio feature or installed extension.

## Demonstration and repeatable tests

Open the new verified build with:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**. Ctrl+Shift+B builds; Ctrl+F5 runs without debugging. Use **Window** or **Next window** to inspect and control the running form, then return to the IDE. Open `Program.cs`, go to line 12, toggle a breakpoint with F9 and press F5. F10 steps and Shift+F5 stops. To try the designer, use Project → Add Form, then F7 and Shift+F7 switch between that form's code and designer. Returning to automation rejects picture input. The previous viewer is left open; this command launches the new versioned build.

```powershell
& "C:\Programming\Projects\VIAVW\scripts\test-visualstudio-end-to-end.ps1" -SkipBuild
```

`-CoreOnly` omits the extended menu/project/designer experiments and the separate drag/wheel mouse regression. The full mouse test validates selection by deleting it, saving the exact changed text, undoing and saving the original again; it does not rely solely on an accessibility selection string. The scripts create fresh disposable solutions and reuse only a validated experimental profile under an exclusive named lease. Normal launches build into a fresh versioned directory; they do not overwrite an open viewer's binaries. The previously running user session is left alone.

The viewer remained frozen while the external test driver added the checked exit from menu mode. The manifest validates both assemblies. Their exact sources and binaries are preserved separately in `artifacts/baselines/verified-vs-workflows-20261017`; portable-PDB document checksums verify the saved source. Use that snapshot's `RECOVERY.md` when restoring. The launcher was verified with `-SkipBuild -PrepareOnly`; this did not open another foreground viewer.

## Limits and recovery

Native GPU capture on private desktops remains unresolved; this verified profile uses Visual Studio's built-in software rendering. General docking/dragging, all workloads, debugger targets, IME, global clipboard operations and hardware-polled input are not universally verified. Human acceptance of this revision remains pending; automated canvas interaction is not a substitute for it.

Owned test trees are closed gracefully where possible, then their owned jobs are disposed. Per-run reports distinguish graceful exit from final process cleanup. Experimental profiles and useful failed evidence are retained. The earlier browser-profile deletion restriction is respected. No Git operations or unrelated project changes are part of this work.

The [final audit](../artifacts/m30-final-audit.json) found zero remaining owned experimental processes. The pre-existing viewer/IDE and Notepad++ were still present, and the original Release assembly retained its previous SHA-256. Retained M30 artifact directories occupy approximately 562 MiB; the experimental Visual Studio profile occupies approximately 136 MiB, with source/binary snapshots additional. The older approximately 322 MB of policy-blocked browser profiles were retained without another deletion attempt.

A concurrently running IDE can lock its personal registry hive. Such files are reported as unavailable for hashing; equality of an unavailable marker is not proof that the hive was unchanged. Readable settings and physical-desktop observations are reported separately.

The next experiment should separate viewer queueing, input forwarding, source rendering and capture in the latency trace, then test independent capture/observation scheduling while keeping input ownership serialized. Modal focus and action costs deserve separate timing before more optimization. Private-desktop GPU/WPF capture remains a research direction; the working software-rendered baseline and these workflows provide a regression target for it.

## Primary references

- [Microsoft: run a C# application](https://learn.microsoft.com/en-us/visualstudio/get-started/csharp/run-program?view=vs-2022)
- [Microsoft: navigate with the debugger](https://learn.microsoft.com/en-us/visualstudio/debugger/navigating-through-code-with-the-debugger?view=vs-2022)
- [Microsoft: experimental instances](https://learn.microsoft.com/en-us/visualstudio/extensibility/the-experimental-instance)
- [Microsoft: visual experience options](https://learn.microsoft.com/en-us/visualstudio/ide/configure-visual-experience-options?view=vs-2022)
- [Microsoft: AttachThreadInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput)
- [Microsoft: GetKeyboardState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardstate)

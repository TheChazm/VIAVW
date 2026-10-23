# Private desktop mouse and menu workflow

The installed Visual Studio 2022 experiment now completes an ordinary mouse/menu workflow through the VIAVW viewer. The implementation remains a reusable private-desktop input/presentation option; the core changes contain no Visual Studio command IDs or menu-name mappings. The IDE still needs the existing experimental software-rendering profile. Physical human acceptance of this revision remains pending.

## What changed

The previous mouse path acquired temporary native capture and released it after the complete click. A controlled comparison failed to leave the tested menu open with that policy, and succeeded twice when capture initialized the mouse source with a move, then was released **before button-down** so the application could acquire capture itself. This supports a capture-lifetime/cancellation explanation; the IDE's internal event sequence was not instrumented.

That policy alone was insufficient for text controls. Editor clicks which must collapse an existing selection, and wheel events, retain capture for their transaction. Menu clicks and hover use the earlier release. A right-button release is posted because the tested context menu can run a modal dispatcher inside its handler; synchronously waiting for that handler caused a timeout even though the menu opened. Posted delivery is not treated as proof that the application processed the event.

WPF popup HWNDs can have no native owner. The new presentation path identifies visible, captionless framework popup windows on the **same application GUI thread**, validates their exact job/private desktop, and composites them in native z order over the source frame. It routes pointer input to the application's current capture HWND or the matching popup. It bounds popup count and capture dimensions, and records clipping rather than claiming arbitrary desktop composition. A coalesced hover path operates while popups are visible; hiding the preview suspends it.

Raw drag messages did not reliably select text in the IDE. Completed text drags instead use an ordinary anchor click followed by Shift-click at the endpoint. Read-only UIA text bounds identify suitable controls; no UIA selection, value, editor-model or application-code writes manufacture the result. Bounds from the published frame are reused. This is a functional text-selection translation, **not general drag-and-drop or virtual hardware**. The application remains unmodified.

The viewer continues to serialize commands off its UI thread and enforce Human/Automation ownership. A forwarding sequence counter lets tests wait for the wrapper to finish input; tests then independently check actual application state. An earlier frame-version-only check could inspect stale state. Another false failure came from comparing a 3,099-character selection against observation text capped at 2,048 characters; bounded range-endpoint comparisons now establish whole-document selection without increasing text limits.

## Demonstrated workflow

The installed **Visual Studio Community 2022 17.14.36203.30**, normal packages, inherited environment and retained software-rendering profile passed:

- Tools → Options → Cancel, including readable popup/dialog capture.
- Returning ownership to Automation with Tools still open, rejecting a Human mouse command, then restoring Human ownership and using that same menu.
- File → Open by hovering its nested submenu, opening a disposable second source file, and clicking the first document tab.
- Selecting a proper source substring by dragging, and wheel scrolling a 97-line document down and back with verified visible text.
- Right-click context menu → View Code, preserving the document and returning editor focus.
- Caret insertion which preserves the surrounding document, followed by two-file editing/saving, Ctrl+Tab, close/reopen and IDE build.
- Clicking back from Output into code after the build, saving another edit, and preserving the same IDE across ownership handoff.

`artifacts/vs-m26-text-policy-viewer/vs-workflow.json` records the first complete positive workflow. The final repeat, including ownership while a menu is open, is under `artifacts/visual-studio-verification-20261015-215800`. The final audit/index records regression outcomes, capture observations, snapshot hashes and cleanup.

Earlier failures remain under `vs-m26-*`: old capture policy, explicit UIA Invoke of Tools, unmodified drag delivery, premature observations, context-menu synchronous release and the too-short selection observation. An initial Notepad++ regression overlapped IDE testing and missed Save As; its evidence and cleanup are retained separately from the final sequential regression. No universal framework conclusion is drawn from these failures.

The final sequential suites passed all three VS cases, all five Notepad++ cases (including its 31-step document workflow), all five qIV window/menu cases, and five native argument checks. A preceding Notepad++ run completed all 31 functional checks but failed strict foreground equality: PID 25160 became foreground without a sampled input-tick change. It was outside that run's recorded job, had exited before inspection, and was not identified. No owned window became foreground, and pointer/confinement/desktop checks passed. The failure remains recorded; the final repeat passed the strict physical-desktop check.

In the final VS viewer run, 22 main-frame observations had a median capture/PNG cost of **43.0 ms** (37.6–63.4 ms); seven popup observations had a median of **57.5 ms** (48.5–71.7 ms). These are different UI states, not a paired benchmark, and exclude viewer UIA observation/dispatch. The observed build shortcut took **3.52 seconds** including forwarding/observation; the IDE's Output reported about 0.81 seconds. CPU/GPU usage was not newly benchmarked. An early popup scan unnecessarily read all window texts; restricting it to same-thread visible candidate HWNDs removed that observed enumeration cost. No broader performance improvement is claimed.

## Run and scope

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
# Automated direct and actual-viewer workflows:
& "C:\Programming\Projects\VIAVW\scripts\test-visualstudio.ps1" -SkipBuild
```

The demo opens `Greeting.cs` and maximizes the viewer. Choose **Human control**, then use the pictured editor, document tabs and menus. Text selection appears when the drag is released. Try Tools → Options and cancel; File → Open supports the printed disposable paths. After a build, click the code editor to resume typing. Ctrl+O remains available as a recovery route. Return to Automation makes picture input read-only. Save before closing; Force close remains available for the owned disposable process tree if helpers outlast graceful shutdown.

This does not establish every IDE command, debugging, arbitrary window dragging/docking, cross-application drag/drop, mixed-DPI physical use, Raw Input or GetAsyncKeyState emulation. Native GPU capture remains unresolved. Successful runs retain the checked normal IDE settings and physical-desktop isolation. No Git operations, drivers, privilege changes, system-wide interception or deletion-policy bypasses were used; profiles and failed evidence remain retained.

The next research step is to try this opt-in capture/input policy with another independently developed WPF application, preserving the ordinary IDE workflow as a regression. Relevant primary references include the [WPF mouse provider](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndMouseInputProvider.cs), [mouse device implementation](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Input/MouseDevice.cs), and [queue keyboard state documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate). Reference source explains plausible mechanisms; the application results above are experimentally observed.

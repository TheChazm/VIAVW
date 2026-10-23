# Visual Studio: human mouse feedback and narrower pointer evidence

Follow-up: the [private mouse/menu workflow](PRIVATE-MOUSE-MENUS-20261015.md) now verifies menus, nested hover, translated text selection, wheel scrolling, context commands and mouse focus recovery through the viewer. This report preserves the earlier failure and narrower evidence; physical acceptance of the new revision remains pending.

The user accepted the earlier research milestone but reported that mouse input after taking Human control was still unusable. **General human mouse usability has not passed acceptance.** The launch instruction, “click the picture, then type,” overstated what the demonstrated keyboard workflow supported.

The retained session, `artifacts/visual-studio-demo-575e4960522242b0a00d8cef54aaa8de/viewer`, shows Human ownership, a private-desktop IDE, no loaded source editor, and Output focus. This explains why typing had no editable code destination in that captured state; it does not establish why every attempted click failed or describe the entire session.

## Changes

- The normal-package VS viewer passes disposable `Greeting.cs` to the IDE at startup alongside the solution, and starts maximized for readability.
- Console instructions and the Human control hint explain the keyboard workflow and incomplete mouse support. Ctrl+O remains the recovery route when the intended editor is absent or focus moves elsewhere.
- A separate `--research-vs-pointer` harness tests one variant in a fresh installed IDE process and disposable project. An optional posted mouse sequence is available for research; the working viewer retains synchronous delivery.
- The full viewer regression now checks startup editor focus and actual caret placement: select the document, click its first line, insert a unique comment, save, and require the entire original text to survive with the comment inserted away from position zero. It then replaces that temporary source through ordinary UI input and runs the two-file/build/handoff workflow.

## Evidence

`artifacts/vs-m25-isolated-pointer` contains four installed Visual Studio 2022 runs using the existing experimental software-rendering profile:

| Delivery | Temporary private capture | Caret insertion preserved source |
| --- | --- | --- |
| Synchronous | Yes | Yes, offset 15 |
| Synchronous | No | No; marker replaced selected document |
| Posted | Yes | Yes, offset 15 |
| Posted | No | No; marker replaced selected document |

All four completed their observations and containment checks; two pointer outcomes were negative. This supports retaining temporary capture for this opened-editor state. Posting alone did not help. It does not establish pointer focus recovery from Output, menus, dragging, right clicks, or arbitrary controls.

The public [WPF mouse provider source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndMouseInputProvider.cs) checks capture or cursor/window correspondence before accepting some initial input. That offers a plausible mechanism, inferred from reference source rather than a trace of the IDE's loaded framework.

The first combined harness timed out reopening a document before pointer testing. Its retry demonstrated caret placement but failed a subsequent restore/save. Both reports and disposable files remain under `vs-m25-pointer-matrix*`. The first expanded viewer attempt failed the select-all precondition after redundantly reopening the startup document. Successful individual calls therefore remain insufficient evidence of reliable workflows.

`artifacts/vs-m25-viewer-retry` passed startup focus, viewer caret insertion, two-file editing/saving, Ctrl+Tab, close/reopen, IDE build with compiled-constant inspection, post-build Ctrl+O recovery, another edit, and ownership handoff. `artifacts/vs-m25-maximized-viewer` repeated that expanded workflow with the final maximized viewer. The ordinary installed Notepad++ workflow passed all 31 checks in `artifacts/vs-m25-notepad-regression`.

Final outcomes and snapshot verification are indexed in `artifacts/m25-verification-index.json`. Completed runs recorded no owned survivors, sampled physical-desktop interference or changes to checked normal VS settings. No profiles were deleted. The previous working snapshot remains intact. No Git operations, instrumentation, drivers or system-wide changes were used. No new CPU/GPU performance improvement is claimed.

## Demonstration

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

The viewer should open maximized with `Greeting.cs` visible. Choose **Human control**, focus the picture and try the open editor. Ctrl+O with a printed source path is the verified keyboard recovery route. Ctrl+S saves, Ctrl+Tab switches open documents and Ctrl+Shift+B builds. General mouse usability remains incomplete; this revised experience still needs physical human testing.

Next: test pointer transitions between code, Output and menus, checking actual focus and document state before typing. Preserve the opened-editor caret test and ordinary keyboard workflows as regressions.

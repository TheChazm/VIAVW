# Visual Studio: key release, ordinary builds and focus recovery

The installed **Visual Studio Community 2022 17.14.36203.30** now passes a larger workflow through the actual VIAVW viewer: edit two code files, save, switch with Ctrl+Tab, close/reopen, build through the IDE, return to code through Ctrl+O, edit again and verify ownership handoff. It retains the [software-rendering experimental profile](VISUAL-STUDIO-PRIVATE-WORKFLOW-20261015.md). The IDE binaries remain unchanged; no injected code, DTE/editor model writes, physical input injection or desktop switching were used.

Native GPU capture, general pointer focus, Tools menu navigation and debugging remain incomplete or untested. The user subsequently reported mouse input after Human handoff was unusable; broad human usability has not passed. The [mouse follow-up](VISUAL-STUDIO-MOUSE-20261015.md) records that feedback, controlled caret tests and the revised startup demonstration.

## Key-release finding

The accepted input path posted KeyUp while the shared thread keyboard state still said the key was down, then cleared that state afterward. The new opt-in path clears the released key and its side-specific modifier before delivering KeyUp. Original state restoration and private-desktop/job validation remain in place.

| Same installed IDE and experimental profile | Ctrl+Tab result |
| --- | --- |
| Original ordering | Failed to select the first edited document |
| State cleared before KeyUp | Switched successfully; remaining 17-check workflow passed |
| Original ordering repeated | Failed again |
| New ordering repeated in final suite | Passed again |

The actual viewer also verified Ctrl+Tab using the new ordering. This is evidence for the tested message/state timing issue, not full device emulation. Microsoft distinguishes [queue keyboard state](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate) from hardware-level asynchronous state; [attached private threads can share keyboard state](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput). VIAVW still does not virtualize GetAsyncKeyState or Raw Input. Held-modifier behavior across arbitrary sequences is not established by these atomic chord tests.

`SendQueueStateChord` adds `releaseStateBeforeKeyUp`, defaulting to false. Only the dedicated normal-package Visual Studio viewer opts in. Other applications retain their accepted release behavior.

## Building inside the IDE

Ctrl+Shift+B reached Visual Studio, but the first attempt displayed **Skipped Build**, with no assembly produced. Adding explicit Debug/Release project groups alone did not resolve it. Correcting the generated solution's platform mapping from `Debug|AnyCPU` to the canonical `Debug|Any CPU` display name allowed the IDE build to complete. The ordinary C# project itself retains `AnyCPU` configuration identifiers.

This was a disposable solution setup issue. The earlier external MSBuild check had missed the IDE-specific skip, which is why the ordinary workflow matters. Microsoft documents [configuration-related skipped projects](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-exclude-projects-from-a-build?view=visualstudio) and [Ctrl+Shift+B](https://learn.microsoft.com/en-us/visualstudio/ide/default-keyboard-shortcuts-in-visual-studio?view=visualstudio).

Both direct and viewer tests now verify:

- No assembly exists before the single build shortcut.
- The IDE produces the library in that fresh solution's output directory.
- Passive PE metadata inspection finds **both source constants entered through the UI**.
- The viewer's read-only Output observation reports **1 succeeded, 0 failed, 0 skipped**; the completed output is captured visually.

The harness does not invoke an external build command or execute the generated assembly. The final direct case passed 19 checks.

## Focus and observation failures retained

Building can move logical keyboard focus into the read-only Output pane. A stricter handoff check exposed that the earlier successful build test did not prove that subsequent rejected text was aimed at an editable destination.

The pointer attempt and Shift+Esc did not restore the required code-editor focus in their tested runs. Ctrl+F6 experiments exposed another problem: a newly published preview could describe an older document while the switch was still completing. One post-build edit landed in the **other disposable file**, and exact-content verification failed. Those files and reports are retained.

The viewer's bounded change watch now notices a changed window **title** even when the HWND stays the same. It continues to skip unchanged windows. The tests check intended title, actual editor contents and focus rather than treating a preview-version increment as application acknowledgement.

The verified post-build recovery uses ordinary **Ctrl+O to reopen the intended file**, which restores code focus. The viewer then saves a further edit and verifies exact application/disk content. Only after proving that editable destination does it return to automation, reject picture input, restore Human ownership and save again to confirm preservation of the same IDE and document.

Alt+T/O still failed with both release orderings. Explicit system-character delivery was also unsuccessful, including synchronous, focus-primed and posted variants. These remain isolated research modes; the working viewer does not select them. The earlier supported `Tools.Options` startup command remains available for scoped graphics settings. No universal Windows limitation is inferred from these failures.

## Verification and measurements

The final suite, `artifacts/visual-studio-verification-20261015-193305`, passed all three cases: direct release-state workflow, direct IDE build, and the full viewer workflow. `artifacts/vs-m24-open-viewer` independently passed the stronger viewer path. Earlier incomplete passes and stricter failures remain available under `vs-m24-*` and the earlier verification directory.

In the final direct release-state case, eight subsequent main-window captures measured a median near **38.6 ms**, range **31.8–40.6 ms**, including PNG encoding. A 9.72-second activity interval used **2.83%** whole-machine CPU on 24 logical processors; the private worker accounted for **0.19%**. A short 2.07-second idle interval measured **0.35%**. All 16 process creation identities matched across those intervals. Private bytes were about **2,022 MiB**, with **2,732 MiB** summed working sets.

The direct observed build took about **764 ms**; two full-viewer observations took about **561 ms** and **1.31 seconds**. These include dispatch/observation and test pacing, and are not a compiler benchmark. CPU covers the owned worker, IDE and helpers, excluding parent controller and DWM; summed working sets double-count shared pages. GPU utilization/memory were not measured. No CPU optimization or explanation of the historic 22% observation is claimed.

Detailed evidence is in `artifacts/m24-performance.json`, the final suite's `runs.json`, its direct `ide-build.json` and viewer `vs-workflow.json`. The release build and existing Notepad++/qIV regressions are recorded in `artifacts/m24-verification-index.json` and the final audit. No Git operations, drivers, global hooks, privileged changes or deletion-policy bypasses were performed.

## Demonstration and next direction

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-visualstudio.ps1 -SkipBuild
# Automated positive cases on private QA desktops:
.\scripts\test-visualstudio.ps1 -SkipBuild
```

An existing IDE instance is refused. The demo creates fresh project data and prints file paths. Maximize the viewer, choose **Human control**, use Ctrl+O for those files, edit/save, switch with Ctrl+Tab and build with Ctrl+Shift+B. After building, use Ctrl+O again for the desired code file before typing. **Return to automation** rejects picture input. Save before closing; if helpers outlast graceful shutdown, inspect the state and use **Force close** for the owned disposable tree.

All completed experiments record cleanup and sampled physical-desktop checks. The IDE can exit gracefully while helpers remain until job disposal; this is still a lifecycle qualification. The pre-existing user Notepad++ session is protected by ownership boundaries. The final audit records observed survivors and snapshot hashes.

No profiles were deleted. The reused VS experimental profile occupies approximately **140.8 MiB**, compared with 140.7 MiB previously. M24 VS experiment/verification directories occupy about **38.5 MiB**. The separate project-local retained-profile inventory remains roughly **4,055 MiB**; shared caches and ordinary temp files are outside these scoped counts.

The next useful investigation is dependable pointer/logical-focus and menu behavior in the installed IDE, using these keyboard/build workflows as regressions. Keep native GPU presentation as a separate experiment and retain the software capture fallback. Debugging and running generated applications need their own process/desktop ownership verification before being claimed.

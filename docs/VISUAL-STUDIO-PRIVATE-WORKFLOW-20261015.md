# Installed Visual Studio: private editing and viewer demonstration

Follow-up: [input and IDE-build research](VISUAL-STUDIO-INPUT-BUILD-20261015.md) adds working Ctrl+Tab release timing, a full viewer build workflow and stricter post-build focus checks. The measurements and earlier failed configuration below remain historical evidence.

The workstation's **Visual Studio Community 2022 17.14.36203.30** now supports a useful private-desktop editing workflow with **normal package loading and its built-in software-rendering option**. The installed binaries are unchanged. No runtime injection, DTE/editor model commands, clipboard writes, physical input injection or desktop switching were used.

This is a compatibility configuration, not successful native GPU capture or complete keyboard/mouse emulation. Visual Studio 2026 remains untested. Physical human acceptance remains pending.

## What changed the result

The [initial experiment](VISUAL-STUDIO-RESEARCH-20261015.md) combined a separate experimental profile, Safe Mode, redirected child environment paths and private-desktop/job containment. The follow-up retained that same experimental profile and containment while comparing startup conditions:

| Experiment | Observed result |
| --- | --- |
| Safe Mode, inherited environment | Project warning and COM `E_FAIL` opening code persisted |
| Normal package loading, inherited environment | Code opened; ordinary selection, typing and Ctrl+S saved exact content; main capture remained white |
| Repeat Safe Mode after that success | Project/editor failure returned |
| Normal package loading, automatic/hardware graphics disabled through Options | Main IDE became readable through native PrintWindow; later launches retained readable output |

This isolates a strong configuration dependency in the tested profile. It does **not** establish that Safe Mode always breaks Visual Studio, that job containment is irrelevant to every IDE feature, or that private desktops support every Visual Studio service. No normal-desktop control was launched, preserving the physical workspace.

Microsoft documents [Safe Mode's restricted package loading](https://learn.microsoft.com/en-us/visualstudio/ide/reference/safemode-devenv-exe?view=vs-2022) and the [hardware/software graphics option](https://learn.microsoft.com/en-us/visualstudio/ide/configure-visual-experience-options?view=visualstudio). Options initially reported hardware rendering. The experimental profile now has **Automatically adjust visual experience** off and **Use hardware graphics acceleration if available** off. Read-only UIA toggle observations confirmed both changes; native pointer messages and the ordinary OK button applied them. Options was opened using the documented [`/Command Tools.Options` startup facility](https://learn.microsoft.com/en-us/visualstudio/ide/reference/command-devenv-exe?view=vs-2022).

## Verified workflows and remaining input issues

The final direct run passed **17 checks**, using a fresh disposable .NET Framework 4.8 library solution:

- Open each of two C# files through the ordinary file picker; verify the actual editor's loaded text against disk.
- Select, replace, enter a new line and save exact independent contents through ordinary input.
- Switch back using Ctrl+F6, close the active document and reopen its saved contents.
- Transfer ownership, reject automation while Human owns input, save an automated Human-sender edit, return ownership and reject Human input.
- Preserve the same IDE process throughout; inspect readable source images and verify cleanup.

The saved two-file project also compiled successfully with the installed MSBuild. That is an independent output-validity check; building through the IDE and debugging were **not** tested.

Actual viewer tests separately opened a code file, selected/typed/saved through the picture, returned ownership to automation, rejected further picture text and preserved the same application/document. The final viewer path passed twice, including with newly prepared demo files. Screenshots were visually reviewed for actual IDE/code output. Automated Human senders do not constitute physical-human acceptance.

Retained failures matter:

- Sending multiline text only as `WM_CHAR` omitted the line breaks. Explicit Enter key events preserved them.
- Ctrl+Tab did not verify a document switch; Ctrl+F6 worked. Both are distinct [Visual Studio navigation commands](https://learn.microsoft.com/en-us/visualstudio/ide/default-keyboard-shortcuts-in-visual-studio?view=visualstudio).
- Alt+T/O and the initial pointer-menu attempt did not complete Options navigation. Those routes remain incomplete; no general menu compatibility is claimed.
- Early viewer tests sampled the old main window before the posted shortcut's dialog became visible. Input traces showed the owned Open File dialog and disabled owner. A bounded window-change watch now follows posted input without replaying it or capturing unchanged windows continuously. State-based test waits verify the actual dialog/document.

## Implementation and measurement

`VisualStudioPrivateVerification` adds validated startup comparisons, fresh disposable solutions, read-only editor/toggle checks, native interaction, capture evidence, process identities and resource snapshots. The original restrictive Safe Mode experiments remain available. A dedicated normal-package viewer path inherits the ordinary environment and redirects only its activity-log argument into that run's output. Normal-profile settings hashes stayed unchanged in all follow-up runs; the previously disclosed initial incident has not been erased or represented as restored.

The common viewer change is limited to its opt-in posted-shortcut path: it watches for late window changes for three seconds after relevant input. Existing command serialization, ownership, UI-thread responsiveness and job containment remain intact. Optional input diagnostics are available internally and disabled in the final viewer tests. The passive accessibility observer now reports checkbox toggle state.

One verified run on **24 logical processors** measured:

| Observation | Result |
| --- | --- |
| Seven subsequent main-window captures, including PNG encoding | Median **38.2 ms**, range **34.9–40.9 ms** |
| First main capture | **88.3 ms** |
| Owned worker + IDE/helpers during 8.70 seconds of workflow activity | **3.09%** whole-machine CPU; worker share **0.13%** |
| Short 2.08-second idle interval | **0.34%** whole-machine CPU |
| Owned process tree near completion | **2,666 MiB** summed working set; **2,044 MiB** private bytes |
| Private input worker's working set | **80.8 MiB** |

CPU comparisons use matching process creation identities; all 15 identities remained stable across those measured intervals. Activity includes test pacing, UIA, capture and dialogs. The parent controller/viewer and DWM are excluded. Summed working sets double-count shared pages. GPU utilization/memory were not measured. These are short observations, not an endurance benchmark or an explanation of the earlier 22% observation. Visual Studio's substantial application/helper footprint remains material when considering many layers.

## Demonstration and reproducibility

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-visualstudio.ps1 -SkipBuild
```

This prepares fresh documents and uses the retained, verified experimental software-rendering profile. An existing IDE instance is refused. Maximize the viewer for readable code, choose **Human control**, click the picture and use Ctrl+O with the source paths printed by the script. Edit/save, open the second file and switch with Ctrl+F6. **Return to automation** makes picture input read-only. **Hide preview** suspends capture while the application stays alive. Human typing, mouse selection, menu behavior and this presentation's comfort still need the user's manual assessment.

Closing requests graceful shutdown. The final direct run confirmed that the IDE itself exited, but helpers remained beyond the grace period; disposing the owned job removed them. The viewer can therefore remain open after the close request; inspect any prompt, then use **Force close** for that disposable tree when appropriate. No claim of fully graceful helper shutdown is made.

For automated reproduction, use the release `LayerHost.exe` with:

```text
--research-vs-private fresh-output validated-launch.json workflow
--research-vs-private fresh-output validated-launch.json options-command
--research-vs-private fresh-output validated-launch.json software-options
--test-vs-private-viewer fresh-output validated-launch.json
```

Use `artifacts/vs-m23-software-options/launch.json` as the verified profile descriptor. The new commands explicitly select normal package loading after validating that descriptor; older commands retain their Safe Mode defaults. If the experimental profile is removed or reconfigured, repeat the software-options experiment before expecting readable capture.

## Evidence, regressions and cleanup

- Startup comparisons: `vs-m23-inherited-safe`, `vs-m23-inherited-standard`, `vs-m23-safe-recheck` under `artifacts`.
- Rendering change: `artifacts/vs-m23-software-options`.
- Final two-file workflow: `artifacts/vs-m23-workflow-verified/private-workflow.json`.
- Viewer: `artifacts/vs-m23-viewer-final` and `artifacts/vs-m23-demo-viewer-verified`.
- Performance: `artifacts/m23-performance.json`; saved-project build: `artifacts/m23-saved-project-build.json`.
- Final audits: `artifacts/m23-final-audit.json`, `artifacts/m23-vs-audit.json`.

The five-case Notepad++ suite passed, including its 31-check ordinary document workflow. The five-case qIV window suite passed. All five native argument-parser checks passed. The release build has zero warnings/errors. Pre-change snapshots and retained failures remain available; no Git operations were performed.

All 18 completed Visual Studio reports recorded zero owned survivors and passing sampled physical-desktop checks. The final audit found no remaining IDE/ServiceHub/PerfWatson processes, and the pre-existing user Notepad++ PID 40008 was preserved. No drivers, global hooks, privileged changes or policy bypasses were used.

No profiles were deleted. The project-local retained-profile inventory remains approximately **4,055 MiB**. The reused Visual Studio experimental profile in per-user AppData grew from **28.3 to 140.7 MiB**; Visual Studio research output directories add approximately **20.8 MiB** separately. These figures are scoped inventories, not a complete accounting of Visual Studio's shared caches or ordinary temporary files.

The next useful experiment is reliable stateful document/menu input and an IDE-driven build on the disposable solution. Keep the software path as a regression and demonstration while investigating opaque hardware-rendered capture separately. The evidence supports adaptable capture/input choices beneath a shared session and ownership foundation.

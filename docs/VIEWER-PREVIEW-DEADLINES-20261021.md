# VIAVW preview scheduling and practical latency

Development report for October 21, 2026. The faster verified viewer is selected by the normal demonstration. Physical human acceptance remains pending.

The selected viewer substantially improves typing previews in the installed Visual Studio 2022 Community application, using normal package loading and the existing isolated software-rendering profile on a private Windows desktop. It preserves the earlier executable. The measurements support a practical latency frontier for this capture path, rather than a claim of foreground-native equivalence or a universal Windows limit.

## Implementation

Input now requests an initial preview through a cancellable, one-shot high-resolution timer. A sweep of initial deadlines from 0 to 16 ms favored 4 ms; capturing immediately often caught stale application pixels. Follow-up captures briefly run at approximately 16 ms intervals. The dispatcher sleeps while idle, coalesces deadlines, queues at most one UI callback, and falls back to the existing UI timer on failure. It makes no system timer-resolution or priority changes.

The canvas paints disjoint changed regions separately while retaining the original global scaling. Actual painted-canvas comparisons verify this behavior. Automatic status-file writes use a bounded background publisher, and the viewer reuses the captured client origin. Explicit inspections retain ordered publication. Application input remains serialized and subject to ownership checks.

Nonfunction shortcuts use precise steps with a 24 ms modifier hold. F9 and F10 use the faster posted path; application transitions retain their earlier pacing. The normal viewer enables neither pixel probes nor timing traces.

## Measured performance

Two sessions per setting, seven individually submitted characters per session, ran in control / candidate / candidate / control order. Each trial checked a calibrated glyph mask in the captured source bitmap, actual editor text, and the saved file.

| Endpoint | Previous viewer median | Candidate median |
| --- | ---: | ---: |
| Input forwarded | 2.09 ms | 2.99 ms |
| Fresh bitmap available | 42.26 ms | 19.19 ms |
| Normal paint handler completed | 49.11 ms | 22.25 ms |
| Paint handler wall cost | 4.50 ms | 0.95 ms |

Candidate paint completion ranged from 19.35 to 43.55 ms; control ranged from 24.31 to 56.58 ms. These are small samples. Paint-handler return is not physical monitor scanout. The improvement comes mainly after input forwarding. A separate seven-character source-only private-IDE experiment, without the viewer, reached fresh source pixels at 22.99 ms median. Different capture and scheduling conditions prevent subtracting these measurements to calculate exact wrapper overhead. It was not a foreground-session comparison.

The sustained visual check also passed twice per setting: 48 glyphs per mode submitted at 50 ms intervals without per-character acknowledgement waits. Every new glyph was checked against a calibrated position mask, followed by exact editor and save checks. Median bitmap-time upper bounds were 43.87 ms for control and 14.91 ms for candidate; normal paint-time upper bounds were 50.82 and 18.79 ms. The candidate paint bounds ranged from 13.95 to 79.66 ms. These use the first report-observed fresh frame and can miss an earlier painted frame, especially when an unchanged frame is reported after its content was already painted. They are conservative bounds rather than exact per-character display latency. Pixel export and tracing add diagnostic overhead to both modes.

The corrected 200-character workload ran twice per setting in control / candidate / candidate / control order, checking exact editor text and saved files after approximately ten seconds of typing. The previous incremental-painting default sampled 256/235 frame publications and consumed 12.78/12.83 CPU-seconds. Candidate sampled 613/590 publications and consumed 18.06/20.09 CPU-seconds. The more fluid preview costs additional CPU during typing. These are owned-tree observations including Visual Studio, the viewer and test helpers, rather than whole-machine utilization or isolated renderer CPU costs. No GPU utilization counters were collected. An earlier CPU comparison mistakenly selected the older full-repaint mode; those results remain retained with that classification.

The editing regression observed about 3.02 GB peak aggregate working set across the owned IDE, viewer and helpers. During its two-second idle checks, viewer CPU rounded to zero; Human mode published four frames, and Automation mode published none. These short checks do not establish long-term resource bounds.

## Compatibility and verification

The candidate passed 32 editing checks, 15 mouse checks, exact saves, build, and automated human/automation handoff. These include clicking a code line then immediately sending Enter and text, Backspace followed immediately by text, selection, deletion, undo/redo, and file switching. Physical human acceptance remains pending.

The complete installed-IDE regression passed 29 selected workflows and 66 menu paths, including executable build/run, breakpoints/step/continue/stop debugging, Options, tool windows, error correction, Windows Forms designer transitions and New Project creation. The menu inventory opens bounded submenu paths; it does not execute every leaf command. The capture/resource suites passed 18 deadline checks, 12 publisher checks, 15 independent-capture checks, 25 mapped-DC checks and 25 transfer checks. Ten actual painted-canvas cases matched a full redraw exactly. The deadline lifetime test created/cancelled/disposed 64 threads; handles went from 257 to 449 before diagnostic managed reclamation, then returned to 257. Production never forces garbage collection. The ordinary-application tests use the installed IDE and disposable runnable solutions. Small dedicated fixtures test renderer equality, queue bounds and resource lifetime, rather than standing in for application compatibility.

## Experiments and remaining limitations

A warmed native-stage benchmark found approximately 5.99 ms in the complete source-window copy, versus 0.0046 ms acquiring its DC and 0.0055 ms releasing it. Retaining a source DC would save little while complicating lifecycle handling, so that change was rejected. Destination DIB and transfer resources remain reusable. With painting already below 1 ms, replacing the drawing stack alone offers limited further savings.

A synchronous shortcut-delivery experiment failed exact text reset and was rejected. Posting every function key made the debugger experiment work but lost immediate typing after F7 switched from designer to code. That policy was rejected. The selected policy passed the strict F7-then-typing check with transition pacing retained. A separate test restoration bug used multiline character input where real Enter events were required; restoration now uses verified Undo operations. Both failures remain in the evidence. The new paced-visual harness also initially rejected a valid preserved path because of a duplicated directory separator; that QA-only guard bug was corrected before the successful visual runs.

Secure-desktop interruptions stopped affected experiments safely; those runs are retained separately from clean repetitions. One full-regression capture logged an invalid-handle copy error during a closing-window transition and continued to subsequent workflows. All 29 workflows and the independent-capture recovery suite subsequently passed.

Drags still deliver their completed path on release; live drag feedback is not implemented. Some modal presses use standard accessibility assistance. This is functional private message input, not virtual-device emulation. Native GPU capture for this private-desktop Visual Studio configuration remains unresolved.

## Demonstration and recovery

Start the normal demonstration with:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**, click a code line, then try Enter, rapid typing, Backspace, selection, Ctrl+S, Ctrl+Tab and menus. Ctrl+Shift+B builds; Ctrl+F5 runs; F9/F5/F10/Shift+F5 exercise debugging. Return to Automation should reject picture input while preserving the same application. Close the viewer and resolve any disposable save prompt.

`-PreviousInput` selects the preserved previous incremental-painting viewer; `-PrintCapture` retains the printing comparison. Keep preserved executables at their recorded build-directory depth so project discovery works.

The selected executable is `C:\Programming\Projects\VIAVW\src\LayerHost\bin\Speed-20261021aa-fast\net10.0-windows\LayerHost.exe`. Its source/PDB snapshot is `artifacts/baselines/speed-fast-20261021aa`. The working source adds only a standalone paced-visual QA command and verifier, preserved separately in `artifacts/baselines/speed-paced-visual-20261021ad`.

The public demo preparation checks passed for the selected viewer, previous viewer, printing comparison and experimental alias. The public responsiveness script then passed all 32 checks with the selected executable. The final audit passed all 19 checks: preserved binary hashes, source/PDB correspondence, script parsing, verification results and cleanup. No owned test process or project LayerHost process remained. The retained experiment, build, snapshot and demo directories measured 302.55 MiB for this phase; this is separate from the previously reported browser profiles, which remain retained. The executable snapshot matched all 135 available PDB source documents; the standalone QA snapshot matched 136. Each PDB also references one unavailable SDK-generated virtual document. No Git operations, drivers, privileged changes, global input injection or input-desktop switching were performed.

The next useful step is physical comparison of the verified demo with normal foreground editing. Larger latency improvements likely require a different source-render/capture mechanism; private-desktop GPU capture and live pointer-gesture semantics should remain separate controlled research paths while this implementation stays available.

The machine-readable verification index is `artifacts/vs-speed-20261021-fast-evidence.json`; the final audit is `artifacts/vs-speed-20261021-fast-final-audit.json`.

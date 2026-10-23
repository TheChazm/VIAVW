# Incremental preview painting research — 2026-10-21

The normal demo now selects the verified Q viewer with incremental preview painting. The prior H viewer remains available with `-PreviousInput`. Fourteen individual-character samples per setting showed a modest paint-completion improvement, from 53.3 to 47.4 ms median. Physical human acceptance of this change remains pending.

## Investigation and decision so far

The current viewer scales a 1920 × 1032 source into a 1598 × 859 preview. Changing the drawing API alone did not help. A native `StretchDIBits` HALFTONE experiment took 10.18 ms median per scaled draw against 6.48 ms for GDI+ (80 alternating observations per setting). GDI+ opaque-copy mode reached 6.08 ms, a small microbenchmark difference without demonstrated end-to-end benefit. Neither alternative was selected.

The more useful result is incremental repainting. The experimental viewer still captures complete immutable images, compares every source byte, and identifies changed 64 × 32 pixel tiles. It repaints their mapped regions, with extra coverage around scaling boundaries. Identical frames do not request a canvas repaint. A new image size, unsupported pixel format or first frame requests a complete repaint. Resize and exposure still use the current full image. Input coordinates, source-window dimensions, keyboard pacing and capture scheduling are unchanged.

The comparison runs on the existing viewer thread with bounded image storage. It introduces no worker thread, timer loop, persistent image history, global hook or graphics-driver change. The normal asynchronous capture and serialized ownership/input architecture remain intact.

## Measurements

Two sessions per setting, seven individually submitted characters per session, ran in control / incremental / incremental / control order. Actual editor content, saved files and a calibrated source-glyph mask were checked separately.

| Endpoint | Complete repaint | Incremental repaint |
| --- | ---: | ---: |
| Fresh source bitmap available, median | 44.28 ms | 40.56 ms |
| Fresh normal paint handler completed, median | 53.34 ms | 47.41 ms |
| Paint handler wall cost, median | 6.59 ms | 4.55 ms |
| Fresh report observed, median | 69.32 ms | 61.49 ms |

Normal paint completion ranged from 22.42–66.02 ms for control and 41.79–48.58 ms for incremental. These are small samples; individual control events can be faster. Some build/fixture research overlapped the first pair, while the reverse-order repeat pair ran without those activities. The bitmap endpoint follows source capture and transfer, not the application's internal render timestamp. Paint completion is a WM_PAINT return, not compositor presentation or physical scanout. Report observation is a separate, later endpoint. These results do not establish foreground-native responsiveness.

A controlled small-edit offscreen comparison-plus-draw microbenchmark fell from 6.41 to 0.90 ms median (80 alternating observations per setting). The real viewer's smaller improvement above is the relevant interaction result.

In one paired 200-character workload at 50 ms intervals, CPU time across the owned viewer/IDE/helper tree was 13,843.75 vs 13,593.75 ms over 10,013.34 vs 9,985.51 ms wall time: 138.25% vs 136.13% of one core. Sampled preview publications were 249 vs 278. Neither the small CPU difference nor publication counts establish a universal efficiency or visual-frame-rate gain. Explicit later UIA/PNG verification is excluded from this measurement. No GPU counters were collected.

## Visual correctness and failures

The initial strict offscreen equality check failed at the scaled image edge: differences were at most two RGB levels, caused by the clipped vs complete GDI+ scaling paths. The evidence retains those failures. The later full-output check explicitly permits a maximum two-level RGB difference; it does not claim exact equality. Thirty-nine checks cover edge edits, scattered edits, large changes, fractional scaling, identical frames and fallback conditions.

A separate owned private-desktop fixture exercised the actual double-buffered InputCanvas. It read the already-painted client DC before asking for a full redraw, rather than using PrintWindow to repaint the image during inspection. All ten cases matched the complete repaint exactly, including text changes/removal, separated damage regions, window resize and source resize. Twenty normal WM_PAINT completions were observed. Owned-process cleanup and physical-desktop monitoring passed.

The first native-drawing resource check compared cold and warm graphics state and reported a two-handle increase. With the baseline taken after both drawing paths warmed up, the count stayed at five throughout the alternating benchmark. The cold/warm failure is retained; it was not evidence of unbounded per-frame handle growth.

## Compatibility already verified

The incremental installed Visual Studio 2022 experiment passed all 32 editing/responsiveness checks: immediate click/Enter/text ordering, repeated Backspace, Ctrl+Backspace, selection deletion, Delete, exact saves, paced typing and same-process ownership handoff. Human-preview captures continued; automation-idle captures stopped after bounded watches.

The complete mouse workflow also passed its 15 checks, including Tools/Options, modal cancellation, nested menu/file picker, document tabs, drag selection/deletion/undo, scrolling, context View Code, build and restored editor focus. Actual saved text and application behavior were checked. This is the installed IDE with normal packages in the established disposable software-rendering profile, not a substitute editor.

The extended Q regression passed 29 workflows and 66 nested-menu paths: exact editing/saves, file navigation, build and compiled constants, runnable GUI child controls, breakpoint/debug/step/continue/stop, 13 top-level menus, Options cancellation, tool windows, compilation failure/repair, handoff, Add Form designer/code switching and New Project using an installed executable template. Nested inventory is limited to three levels; leaf commands are not executed. Git, account operations, extension installation and external deployment are excluded. All 15 independent capture/ownership/restart/stall-containment checks also passed. This remains selected workflow coverage, not every Visual Studio feature. Native GPU capture of this private-desktop IDE remains unresolved.

## Evidence and preserved versions

- `artifacts/vs-speed-20261021-damage-latency.json`: combined timing distributions.
- `artifacts/vs-speed-20261021-o-verification.json`: six completed interaction/timing stages.
- `artifacts/vs-speed-20261021-q-verification.json`: six completed extended, paced-typing, drawing and lifecycle stages.
- `artifacts/vs-speed-20261021-k-paint/paint-research.json`: drawing alternatives and warm handle counts.
- `artifacts/vs-speed-20261021-o-damage/damage-verification.json`: output tolerance and microbenchmark.
- `artifacts/vs-speed-20261021-p-canvas/canvas-verification.json`: initial actual painted-window comparison; the selected Q binary repeated it successfully in `artifacts/vs-speed-20261021-q-canvas`.
- `artifacts/baselines/speed-damage-suite-20261021q`: executable, matching source, project props and PDB validation.

O and Q share the same viewer/input/capture implementation. Their source differences are the additional canvas fixture and test dispatch/extended-suite plumbing, recorded in `artifacts/vs-speed-20261021-damage-source-delta.json`. Q's 133 accessible PDB documents match its saved sources; one SDK virtual document is unavailable. The prior H executable and its source snapshot remain unchanged.

Microsoft references used for the rejected native experiment: [StretchDIBits](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-stretchdibits), [SetStretchBltMode](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setstretchbltmode), [SetBrushOrgEx](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setbrushorgex).

The next useful investigation is the remaining source-render/capture scheduling interval. Further drawing-only gains cannot remove that interval. Any replacement should be compared against actual fresh pixels and ordinary editing/menu behavior rather than faster status reports alone.


## Run and compare

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**, then click the editor and try Enter, rapid typing, Backspace, selection, scrolling, menus and project operations. The disposable project is a runnable executable; F5/Ctrl+F5 can start it. To compare with the preceding H viewer and its complete preview repaints, close the viewer and use:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild -PreviousInput
```

`-PrintCapture` retains the earlier printing viewer. The prior AA metadata is retained as `olderInput*` in the manifest. The demo validates executable paths and assembly hashes; an occupied experimental profile is left alone. No human acceptance is inferred from automated private-desktop input.

The deployed assembly is `src/LayerHost/bin/Speed-20261021q-damage-suite/net10.0-windows/LayerHost.dll`, SHA256 `A6E9AA8505EE2862F0A7B6AF2151E11A216960952146DBD633FC4B636B33C875`. Normal mode is `--run-vs-damage-viewer`, without pixel probes or timing traces. The three public scripts recognize this mode and retain prior modes.


## Verification, resources and cleanup

The final audit passed all 17 checks. All recorded owned experimental processes exited, the selected executable and earlier H/AA/AB/L/B/Release assembly hashes matched, current project sources matched Q's saved source, and all three public scripts parsed. Four public PrepareOnly modes passed (default, prior H, printing, legacy experimental alias). The actual public responsiveness script passed 32 checks against the selected Q assembly. The full extended and mouse CLI suites had already passed; the complete public end-to-end wrapper was not redundantly rerun.

The short editing workload recorded 1,250 ms owned-tree CPU over 1,526.54 ms wall time. Its summed per-process peak working sets were about 3.26 GB; this includes Visual Studio and helpers and is neither simultaneous nor unique physical memory. It is not the wrapper's private memory usage. In approximately two-second idle samples the viewer used 31.25 ms CPU while Human mode captured four previews, then 0 ms at the accounting resolution while Automation mode captured none. These small samples do not establish zero CPU use, stop source application rendering, or replace the paired sustained workload above.

This continuation's experiment, executable, snapshot and public-demo trees occupy about 136 MiB. Older experimental evidence and protected browser profiles remain retained; no profile deletion or policy bypass was attempted. No browsers were launched. No Git operations, drivers, global input injection, input-desktop switching or unrelated-project changes were performed.

The machine-readable final audit is `artifacts/vs-speed-20261021-damage-final-audit.json`. The evidence index is `artifacts/vs-speed-20261021-damage-evidence.json`; public-wrapper records are `artifacts/vs-speed-20261021-damage-public-smoke.json` and `artifacts/vs-speed-20261021-damage-public-responsive.json`. Recovery instructions and the final scripts/manifest/report are saved with the Q baseline. Physical manual acceptance remains pending.

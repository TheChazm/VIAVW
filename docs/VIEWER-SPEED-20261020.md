# Viewer speed research — October 20, 2026

Two controlled experiments used the installed Visual Studio 2022 Community, normal packages, the existing isolated software-rendering profile, disposable runnable projects, and private desktops. Neither produced a sufficient improvement to replace the working viewer. The normal executable, manifest, and launch script remain unchanged. Experimental source and binaries are preserved separately; the working source was restored to the verified AB snapshot (117 project source checks, zero mismatches).

## Results

| Experiment | Control | Variant | Decision |
| --- | --- | --- | --- |
| First capture after input: glyph feedback median | 79.0 ms, 16 ms delay | 85.5 ms, zero delay | Retain 16 ms |
| First capture: sustained typing CPU | 134.0% of one logical core | 187.0% | No reason to adopt zero delay |
| Smaller source: glyph feedback median | 79.0 ms | 79.2 ms | No visible-feedback improvement established |
| Smaller source: capture median | 8.9 ms | 7.3 ms | Useful saving, insufficient by itself |
| Smaller source: refresh median | 21.0 ms | 15.0 ms | Useful saving, insufficient by itself |
| Smaller source: sustained typing CPU | 149.5% of one logical core | 143.9% | Small difference; no general CPU claim |

Each glyph comparison contains 14 samples per setting, from two independent IDE launches. The probe detects new captured glyph pixels partway into a 20-character burst; complete editor contents and the final saved file are checked separately. It does not measure physical scanout or every character. Each CPU value comes from one approximately ten-second, 200-character workload at 50 ms intervals, covers the owned IDE/viewer/helper tree, and excludes subsequent explicit accessibility/PNG verification. The small sample does not establish a universal performance result. No foreground-session comparison was performed.

The zero-delay experiment alternated settings. Full-size runs preceded compact runs to prevent remembered window bounds contaminating the controls; the size comparison was not randomized. Do not combine its absolute values with earlier-day measurements as a causal speed improvement.

## Compatibility and failures

All four glyph runs and both sustained workloads passed in each experiment, including exact actual IDE text, saved-file contents, physical desktop monitoring and process cleanup. The compact view also passed the editing/responsiveness suite, including immediate click/Enter/typing, Backspace, Ctrl+Backspace, selection deletion, save, human/automation ownership rejection and state-preserving handoff.

The compact source was 1588 x 854 client pixels, versus 1920 x 1032: 31.6% fewer pixels and one-to-one display in the tested viewer. However, its first Tools-menu click failed. Two further fresh runs reproduced that failure, including one with private activation after resizing. A diagnostic trace recorded image point (477,17), the intended owned root as destination, and identical delivered client coordinates. This demonstrates forwarding, not successful menu operation. The cause remains unresolved; restored WPF/custom-frame interaction is a hypothesis, not a proven platform limitation. No UIA invocation was substituted to disguise the failure. The complete debug/designer/66-menu regression was not run for this rejected variant.

Visual Studio saved the compact layout in the disposable experimental profile. Consequently a recheck with the untouched production binary also reproduced the menu failure at that size. A separate private system-menu Maximize experiment restored 1920 x 1032. Its helper reported failure because the process tree did not fully close within the graceful deadline; disposal cleaned the owned tree. A subsequent fresh production launch verifies the maximized layout persisted and rechecks the full mouse workflow. Its final acceptance and cleanup result are recorded in the evidence index.

The working default retains the original input ownership, multithreaded capture, resource reuse and shutdown behavior. Neither timing nor compact sizing was enabled. Human acceptance, native GPU-rendered VS capture, arbitrary extensions/workloads, docking and foreground-equivalent fluidity remain unverified. Earlier protected browser profiles were untouched.

## Reproduction and evidence

Run the normal verified demonstration:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**, then click the editor. `-PrintCapture` retains the previous printing comparison. The rejected experimental settings are not selected by this script.

The compact research executable can reproduce its limited test separately using a fresh fixture and `--test-vs-frame-compact-dc output setup viewerExe`; the zero-delay executable uses `--test-vs-frame-immediate-dc`. The exact commands, fixtures, measurements, failure images and source/PDB checks are retained under:

- `artifacts/vs-speed-20261020-a-verify.ps1` and `...-a-verification.json`
- `artifacts/vs-speed-20261020-b-verify.ps1` and `...-b-verification.json` (seven successes, then a recorded mouse failure)
- `artifacts/vs-speed-20261020-{b-compact-mouse,c-mouse,d-mouse}`
- `artifacts/vs-speed-20261020-e-recovery`
- `artifacts/vs-speed-20261020-baseline-mouse` (remembered compact layout failure)
- `artifacts/vs-speed-20261020-baseline-restored` (fresh full-size production recheck)
- `artifacts/vs-speed-20261020-evidence.json` and `...-final-audit.json`
- `artifacts/baselines/speed-{immediate-dc-20261020a,compact-dc-20261020b,compact-active-dc-20261020c,compact-pointer-trace-20261020d,layout-recovery-20261020e}`

All five snapshots match their accessible compiled PDB source checks (120 each, or 121 for the recovery helper; one SDK virtual source unavailable). Copy preserved binaries back into the normal `src/LayerHost/bin/<name>/net10.0-windows` directory depth to run; do not execute them directly inside a snapshot.

The restore/no-activation operation used in the compact experiment follows the documented [ShowWindow semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow). The observed menu failure is experimental evidence, not a claim made by that documentation.

The next useful experiment is to trace source input processing, render readiness, and frame publication as separate stages. Forwarding took a few milliseconds and reducing frame cost did not materially reduce glyph feedback. More capture threads or earlier copies have no evidence-supported benefit yet. Investigate the restored-window menu case separately, with the maximized baseline preserved and disposable profile layout explicitly reset between conditions.

Final audit: all 15 baseline mouse checks and the full editing/build/handoff workflow passed; physical desktop monitoring passed; no LayerHost, devenv or disposable-app survivors remained. Three packaged viewer/verifier hashes matched and three public scripts parsed. The pre-existing Notepad++ PID 40008 was preserved. New experiment/build/snapshot directories occupy approximately 89.8 MiB, including hidden files, excluding earlier retained browser profiles. Default and printing PrepareOnly script checks passed. No Git operations or persistent system-wide changes were performed.

# VIAVW interaction fluidity — 22 October 2026

This investigation follows the instruction to improve actual use, rather than stopping at faster command submission. Work remains within VIAVW. CharlieAI, foreground applications, physical input and Git are unchanged.

## Selected executable and final results

The normal Visual Studio demo selects **Fluid-20261022u**, mode `--run-vs-prepared-viewer`. The previous AA viewer remains available with `-PreviousInput`; all earlier source/binary snapshots remain preserved. The normal agent API and installed-app research selections are unchanged.

- Passed: 29 Visual Studio workflows, 66 bounded menu paths, 15 mouse cases, 32 responsiveness cases, 33 installed Notepad++/Visual Studio protocol cases, 7 initial-read recovery cases, 26 passive-isolation cases, 22 console inspection cases, 13 real 7-Zip console workflow cases, and 20 transport/workspace policy tests.
- Final prepared first click: **17.60 ms** forwarding, including **0.83 ms** live geometry validation. Preparation completed in **172.48 ms** during startup. The earlier prepared run measured 16.94 ms first-click forwarding. The unprepared comparison run measured 260.83 ms; these are separate launches, not a controlled universal ratio.
- Seven calibrated actual W glyph trials: median forwarding **1.34 ms**, fresh bitmap **19.21 ms**, normal paint-handler completion **22.76 ms** (range **16.98–27.75 ms**). Exact application text and saved file passed; the glyph mask excludes caret columns. The slower median report time, 46.44 ms, includes sampling and report publication.
- Steady typing paint time is comparable to the earlier accepted viewer. No reliable further steady-state improvement was demonstrated by this round. The main selected speed improvement moves cold pointer work into startup when readiness permits.
- All final application workflow cleanup and physical-desktop monitor checks passed. A final process audit is linked below. Physical human acceptance and physical scanout latency remain unmeasured.

## Findings and implementation

The large first editor click cost was largely cold UI Automation work. In an unprepared run it spent 233.6 ms resolving editor geometry and 260.8 ms forwarding the pointer. A prepared run spent 0.81 ms resolving current geometry and 16.94 ms forwarding the first click. Those are separate launches, not a controlled speed ratio. Subsequent click geometry already took about 1–3 ms.

The prepared viewer waits for a read-only, owned, visible editor observation, then runs the existing pointer lookup during startup. It retains the live AutomationElement, not an unvalidated rectangle. Every actual click checks current process, visibility, enabled state, bounds and control eligibility. Input is serialized and ownership enforcement remains in place. Preparation is opportunistic: a user or test can send input before readiness collection finishes, in which case preparation is skipped and the normal first-click path remains. Preparation is skipped after any attempted input. It can add startup work and does not make Visual Studio load instantly.

Preparation has a 350 ms provider budget. Before any input attempt only, an exceeded budget restarts the exact owned worker and preserves the application; it does not replay input. An injected stalled read verified recovery, unchanged application/peer processes and HWND/focus, functioning actual Copy dialog input, rejection of preparation after input, and graceful close. A failure to restart the worker can still close the layer. This is not general recovery for all provider-assisted input or capture operations.

The first title-only preparation attempt timed out while the IDE was loading; its worker recovered and all 32 editing checks still passed. Waiting for actual editor readiness allowed preparation to complete. Startup, readiness, preparation and first-click timings are recorded separately.

Passive inspection remains in a separate disposable helper. Explicit viewer inspection now collects each requested ticket before switching targets; ordinary pixel refresh and input do not run this collection. The broad workflow exposed starvation when general/editor queries alternated while pending. A second regression showed that selecting only the first visible editor omitted Build output. Explicit inspections therefore request up to 16 visible matching editor controls; startup readiness still requests one. Incomplete/failed reads remain marked incomplete. Normal automation can use nonblocking `inspect-begin` / `inspect-poll` and explicitly stale evidence is rejected by the client.

An optional automation input validation mode retains the active root's complete native child scan, excluding other roots. A 33-case installed Notepad++/Visual Studio protocol run passed in that mode. A more aggressive focus-only barrier failed actual file opening and was rejected. The default remains the full window scan; no reliable speed advantage has been established for the optional mode.

## Other experiments

| Experiment | Actual result | Decision |
| --- | --- | --- |
| Follow-up preview 8 vs 16 ms, alternating order | 14 glyphs/mode: median normal paint 21.03 vs 23.34 ms; worst 45.38 vs 36.13 ms. Two 200-character workloads produced similar frame counts. | Keep 16 ms; small/confounded samples do not establish a consistent improvement. |
| Immediately paint the changed region | Alternating runs overlapped: control medians 17.21 and 15.09 ms; immediate 15.95 and 15.82 ms. | Keep normal painting. |
| Scoped MSAA hit test | Real VS reported generic client role, not the text editor; the read did not change source or focus. | Not an editor input backend. |
| Native child HWND as UIA scope | Observed VS profile still needed the main root; cold lookup was not faster. | Keep as opt-in research. |
| MTA input worker / explicit editor-ID hint | About 124–212 ms cold lookups in separate runs, with no consistent major improvement. | Keep default STA and ordinary live role traversal. |
| Exact-process Windows accessibility events | Real 7-Zip dialogs and VS glyph changes produced events without physical input. VS events appeared about 2–3 ms after submission; fresh pixels followed later. 16 subscribe/unsubscribe cycles reclaimed native subscriptions; managed thread handles returned after diagnostic GC. | Useful hints for future research, not rendering or input acknowledgements. No default hook. |

For the two 200-character workloads, the owned IDE/viewer/helper tree consumed 21.83 CPU-seconds in 9.98 wall-seconds at 16 ms follow-up, and 19.64 CPU-seconds in 9.99 wall-seconds at 8 ms. These include Visual Studio and multiple cores; they are not machine-wide CPU percentages or isolated wrapper costs. One sample per setting, run in sequence, cannot establish an efficiency improvement. No new GPU utilization claim is made.

Changed-region comparison, capture deadline scheduling and existing keyboard transition pacing remain in use. Reducing waits without validating real file/dialog transitions previously lost edits; no such shortcut is selected.

## Verification and limits

The ordinary application is the locally installed Visual Studio 2022 with an isolated experimental profile and software rendering. Tests send input through the real viewer canvas on another private desktop, inspect actual code and pixels, save disposable files, and build/run/debug a disposable executable. This does not establish support for every Visual Studio feature, extension or workload.

Physical human acceptance and a foreground input-to-physical-display comparison remain pending. Normal `paint-end` measurements describe the viewer's paint return, not physical scanout. UIA reports and test polling can be much slower than visible feedback; report timestamps must not be called screen latency. Some modal/provider paths still have a worker timeout that closes the owned layer. Drag selection is delivered on release. Native GPU capture for private-desktop Visual Studio remains unresolved.

The isolated inspection helper deliberately stops a stalled provider after 750 ms; cold helper connection has a separate 1500 ms budget. Explicit detailed inspection is heavier than ordinary interaction. Installed-application tests are serialized when they share a leased VS profile: a concurrent protocol run was correctly refused while that profile was in use, rather than opening a conflicting IDE instance.

## Demonstration

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**, click a code line, press Enter, type, use Backspace, and switch files. Build with Ctrl+Shift+B, run with Ctrl+F5, debug with F5/F9/F10 and Shift+F5. Inspect details is an explicit slower diagnostic. Return to automation rejects picture input. Close the viewer for graceful shutdown; inspect any save prompt before forcing disposal.

The final audit passed **53 checks**, including selected/snapshot binary integrity, launcher hashes, retained agent selections, application evidence, client policy tests and process cleanup. It is recorded in `artifacts/fluid-20261022-final-audit.json`. No owned Fluid experiment process remained. New scoped evidence, builds and snapshots occupy approximately **293 MiB**; the earlier approximately **322 MB** of browser profiles remain retained. Prior accepted binaries and their source/PDB snapshots are retained. Browser profiles previously blocked from deletion remain untouched. No human acceptance is inferred from automated use.

## Next research direction

The next substantial reliability improvement is isolating remaining provider-assisted pointer/input classification from the application worker, while preserving live target validation, serialized ownership and the no-replay rule. Passive-read isolation already demonstrates why that separation matters. This needs controlled fault tests rather than simply increasing timeouts. Private-desktop GPU presentation remains a separate research avenue; these results do not justify privileged interception or driver changes.

For current use, compare ordinary editing and menus in the prepared viewer against the preserved prior viewer. The evidence supports frame-scale feedback in the measured editing path, not an absolute Windows speed ceiling or a completed foreground-native comparison.

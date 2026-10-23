# Faster private Visual Studio interaction

This revision targets clicking a code line, pressing Enter and typing in the installed Visual Studio 2022 application. The M28 source/executable snapshot remains intact. The existing software-rendering experimental profile is still required; native VS GPU capture is unresolved.

## Changes

- Live preview pixels now travel through a reusable, per-layer shared-memory mapping. Native PrintWindow and popup composition remain the renderer. The viewer copies validated BGRA pixels directly into its image instead of encoding PNG, sending base64 JSON and decoding PNG for every frame. Exported captures retain their PNG path. Mapping size is bounded, grows when necessary, and handles close with worker recovery or layer disposal.
- Pointer classification follows current UIA branches under the click instead of enumerating every text/button/list control in the IDE. Tab headers need special treatment because their document descendants extend outside the header rectangle. One previously found element can be retained, but its current bounds, process, enabled/offscreen state and control capabilities are read again on every hit. A stale rectangle alone is never accepted. UIA performs no input, focus, selection or application-model writes.
- The responsive viewer checks pending frames every 10 ms instead of 25 ms, with existing coalescing and serialization. Its visible-Human idle cadence remains 500 ms; Automation/hidden preview retain their existing quiet policy. This is not a promised frame rate.
- Printable text and navigation use the same posted target queue in this mode. Mixing synchronous WM_CHAR with posted Enter allowed an early typed character to precede the newline. Control characters are filtered individually; Alt's WM_SYSCHAR is discarded individually. WinForms `SuppressKeyPress` can remove pending character messages, as shown in [the .NET implementation](https://source.dot.net/System.Windows.Forms/System/Windows/Forms/Control.cs.html), so it is no longer used by this mode. Already composed ordinary characters are not rejected merely because a later modifier is held.
- Menu hover is queued promptly, coalesced and ownership-checked alongside input. Waiting for an idle timer could starve it behind repeated explicit inspections. Input/capture work remains off the viewer UI thread and serialized by the existing command gate.

The new transport is limited to the responsive VS viewer. Other capture backends and the diagnostic viewer retain their established paths. No code is injected into Visual Studio.

## Verification and measurements

Final evidence and timings are indexed in `artifacts/m29-verification-index.json`. Timing ends at the published preview report, not physical monitor scanout, and an input sequence acknowledges forwarding rather than application completion. The ordinary workflow therefore verifies actual IDE text before the dependent Save command, followed by exact saved files and a build through the IDE. The earlier fixed 250 ms test delay did not establish completion.

The expanded responsiveness test moves the caret away before each measured click, then verifies the click/Enter/typing result exactly. It also sends five click/Enter/text bursts and five Backspace/text bursts without waiting for intermediate frame acknowledgements, checks deletion variants, saves exact text, rejects Automation-owned picture input, and preserves the same IDE across handoff. These are automated senders through the actual viewer; physical human acceptance is pending.

The transport verification checks exact pixels and alpha, reuse, shrink/growth, independent channels, invalid descriptors, 50 fresh frames, recovery and released mappings. The ordinary mouse/menu workflow covers tabs, selection, wheel, context menu, Tools/Options, nested File/Open, two documents, IDE compilation and returning from Output to edit. New Project covers template selection, Next, configuration and Cancel; it does not create a project.

Final measurements use the same expanded harness against the preserved M28 viewer and current viewer:

| Operation | Preserved M28 median | Current median |
| --- | ---: | ---: |
| Editor click, five samples | 351 ms | 64 ms |
| Enter, five samples | 93 ms | 61 ms |
| Eight characters after Enter, five samples | 123 ms | 61 ms |
| Click + Enter + text burst, no intermediate frame waits | Failed rapid-input check | 63 ms |

The current first, uncached click took 125 ms; the following four took 61–64 ms. Geometry alone fell to about 1.3–1.4 ms for a revalidated candidate, versus roughly 240–340 ms in earlier whole-IDE scans. Eight single-character samples had a 70 ms median, range 46–108 ms. These small runs demonstrate an improvement in this configuration, not native foreground parity or a latency guarantee. The legacy control timed out on the new rapid sequence, so it has no valid later single-character/CPU comparison in that run.

The measured typing phase used 1,750 CPU ms over 1,889 wall ms across the owned viewer, IDE, test sender and sampled helpers: about 0.93 CPU cores, not 93% of the whole machine. Summed per-process peak working sets were about 2.80 GiB; those peaks need not be simultaneous. The page-file mapping for a 1920×1032 frame is about 7.56 MiB and is reused. GPU costs were not measured. A two-second Human-idle sample recorded four frames and 78.125 viewer CPU ms; Automation-idle recorded no frames and zero viewer CPU at timer resolution. Background IDE work and sampling variation prevent attributing a total CPU reduction to this revision alone.

All final tests passed: 27 responsiveness/editing checks, 13 mouse/menu checks plus actual IDE build and handoff, seven New Project checks, and 19 transport checks. Final reports are:

- `artifacts/vs-responsiveness-20261016-230937/viewer/responsiveness.json`
- `artifacts/vs-m29-mouse-6/vs-workflow.json`
- `artifacts/vs-new-project-20261016-231051/viewer/new-project.json`
- `artifacts/vs-m29-verified-transport/shared-preview.json`
- Legacy comparison: `artifacts/vs-m29-final-control/responsiveness.json`

Final physical-desktop monitors and personal-settings comparisons passed. No test IDE/wrapper processes survive. The full workflow's graceful shutdown exceeded its bound; owned job disposal performed the final cleanup. Saved files and the compiled constants were verified before cleanup. The build has zero warnings/errors, scripts parse, and all three accepted M26/M27/M28 snapshot manifests remain unchanged. `artifacts/m29-snapshot.json` identifies the new source/executable recovery snapshot. Retained experiment evidence is about 99.1 MiB; the existing experimental VS profile totals about 145.6 MiB, including prior work. No profiles were deleted or new browser profiles created. No Git or unrelated-project operations were performed.

## Retained unsuccessful experiments

- PNG removal alone left the expensive whole-IDE pointer query. An early bounds-pruned query also missed text below tab headers, unnecessarily taking the slower menu-style mouse path.
- A rapid test exposed dropped queued characters, and another observed the first typed character before the newline. Direct synchronous key delivery, including an explicit control-character attempt, did not insert Enter in VS and was removed. The final path retains normal posted navigation and posts text in the same order.
- Initial full workflow runs failed Save. Screenshots retained typed code with an extra `s` and the old saved file. Forwarding alone did not establish that editing had completed; the test now waits for the IDE's actual text before Save. This does not establish arbitrary shortcut ordering under application stalls.
- A repeated-inspection run starved menu hover. The subsequent change queues hover promptly instead of relying exclusively on a later idle tick.
- A handoff test checked only a transient status sentence, which faster live frames could replace before it was sampled. It now also recognizes the continuing owner status; actual rejection, same-process preservation and exact saved content remain required.
- One completed functional run failed strict physical foreground equality: its source never became foreground, pointer/confinement/desktop stayed stable, but unrelated foreground windows changed without a new physical-input timestamp. That result is retained; its origin is not inferred or repaired by manipulating the desktop.

The corresponding negative evidence remains in the `vs-m29-*` and `vs-responsiveness-20261016-*` experiment directories. Successful later runs do not erase those observations. Native-device equivalence, arbitrary application input semantics and foreground-equivalent speed remain unproven.

## Run

Close the older demo viewer, then run:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\demo-visualstudio.ps1" -SkipBuild
```

Choose **Human control**, click a code line, press Enter and type. Return to Automation rejects picture input; Hide preview pauses live inspection. To reproduce the focused benchmark:

```powershell
& "C:\Programming\Projects\VIAVW\scripts\test-visualstudio-responsiveness.ps1" -SkipBuild
```

The remaining useful performance target is native WPF rendering/capture and viewer presentation cost. Further changes should measure that path while preserving idle suppression, actual application acknowledgements for dependent automation steps, and the established ownership/lifecycle rules.

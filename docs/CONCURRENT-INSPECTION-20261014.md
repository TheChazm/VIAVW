# Concurrent ordinary layers and on-demand private inspection

Two unchanged ILSpy instances now have repeatable concurrent input, capture, ownership and recovery evidence. The viewer also has **Inspect hidden app**: it briefly shows a hidden source on its private desktop, captures it, and restores the hidden state. Native pixels work through this operation in ordinary ILSpy, WPF software mode, the unchanged WPF/startup-adapter mode, and the layered WPF GPU fixture. Default opaque hardware WPF remains outside the demonstrated capture scope.

The accepted ordinary-WPF implementation was preserved before development in `artifacts/baselines/accepted-ordinary-wpf-20261014`, with 186 file hashes. No Git operations were performed.

## Concurrent application evidence

Each ILSpy instance uses its own private desktop, worker, job and disposable configuration. Its release binaries are unchanged; the accepted .NET software rendering adapter remains explicitly enabled. Two driver tasks run concurrently, while each layer retains its own serialized command gate.

The test checks distinct search strings on every cycle, independent Ctrl+A replacement, rejection of a foreign layer HWND, and different scroll positions and selected code. Layer A takes human ownership while B continues automating. A's RPC worker is restarted; the source PID, text and owner remain unchanged, and both human input and resumed automation work afterward. In early runs worker restart measured about 123–131 ms. Closing A preserves B's application and captured document. Final results are under `artifacts/m7-concurrent-final`.

The ordinary application used approximately 270–290 MiB working set per instance in the early runs. Each observation worker used about 69–73 MiB working set / 27–29 MiB private memory. Shared pages make summed working sets an imperfect measure of total RAM. No GPU memory measurement or universal concurrency guarantee is claimed.

## Diagnostic cost is real work

The original observer walked up to 200 accessible elements to verify one search field. A new optional automation-ID query reads that field directly, through the same validated owned HWND. It performs no UIA writes or input actions. Broad observation remains available for discovery and QA reports. The query uses Microsoft's [FindFirst API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.findfirst?view=windowsdesktop-10.0).

Two comparisons ran the same twelve checked input/observation/capture cycles, six per layer, in opposite orders:

| Order and observation | Median checked cycle, A / B | Total owned CPU time |
|---|---:|---:|
| Broad first | 1,048 / 1,051 ms | 12.97 CPU seconds |
| Narrow second | 277 / 238 ms | 3.69 CPU seconds |
| Narrow first | 333 / 337 ms | 4.98 CPU seconds |
| Broad second | 1,488 / 1,496 ms | 18.41 CPU seconds |

Native capture itself remained around 8–10 ms. Narrow observation improves the checked cycle by about fourfold and uses substantially less CPU time per unit of verified work. Its shorter burst had a higher whole-machine utilization rate (about 10.4–10.6%, versus 8.5–8.6% broad). Faster completion and lower CPU work do not necessarily mean a lower instantaneous percentage. These are diagnostic workloads, not normal viewer idle cost or an explanation of the historic 22% observation.

One comparison finished application operations but failed report serialization because a deferred diagnostic enumeration was evaluated after disposal. Event data is now materialized at the time of observation. The opposite-order completed run is retained under `artifacts/m7-concurrent-observation-order-b`; the failed run and its intermediate results remain available.

## Hidden execution and visual inspection

Ordinary ILSpy accepted targeted keyboard text while hidden and retained its document and search state. Direct hidden PrintWindow produced black pixels even though the API returned success. Showing it briefly on the private desktop, waiting 150 ms for presentation, capturing and hiding it again produced the actual updated text. The saved PNGs were inspected. The source's hidden state and selection were restored; the physical desktop was never switched.

The experiment's complete inspection round trips were approximately 166–181 ms when initially hidden, including the settle interval, compared with ordinary captures of about 6–22 ms. The viewer reports the whole inspection operation; native rendering/encoding stage fields remain separate. A private-desktop guard, HWND/job validation, serialization and `finally` restoration constrain this operation. It is a presentation change, so other applications may react differently to visibility changes.

**Hide preview** still stops captures. **Hide app window** also changes the source's private visibility. **Inspect hidden app** requests one image and leaves an initially hidden source hidden. **Show app window** restores it for normal mouse interaction. Independent keyboard behavior while hidden is demonstrated for this ILSpy configuration, not every framework or input API.

The visibility CPU trials did not establish a reliable saving: initial visible/hidden samples consumed some CPU, later reshown samples reached zero, and another hidden interval consumed about 172 ms over five seconds. Startup/background work and warmup remain confounders. Windows naturally reported the display on during these trials, after earlier concurrent runs reported it off. Neither power state nor display policy was forced.

One early visibility run had different before/after pointer samples while the session last-input tick also advanced. Those samples do not identify the actor responsible for motion. The later visibility/inspection run, final concurrent run and strict core input samples preserved the sampled pointer/focus. No cursor-writing or global input API was introduced.

The final passive WPF benchmark observed display **on** throughout all 60 changed-frame controls, with zero retries: software/client median **7.84 ms**, layered GPU/full-content median **13.60 ms** (p95 **16.12 ms** for layered/full-content). The accepted display-off comparison was **6.18 / 13.30 ms**. These are separate runs rather than a controlled power experiment; they show useful capture in both reported states but do not explain the earlier 130–140 ms latency outlier.

## Lifecycle correction

Restart previously classified every newly appearing job PID as infrastructure. An ordinary application can create a helper during that interval. A disposable WinForms fixture now does precisely that: three tests confirmed real helper creation during restart. Every helper met the old "new PID" rule, yet must remain application-owned. The new implementation preserves it when graceful close is pending and requires explicit owned disposal to terminate it.

Infrastructure is now registered through worker/capture-helper identities and creation times. Only verified system console hosts associated with those identities are additionally classified. Process census uses read-only [Tool Help parent information](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32w) and [full image paths](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew), restricted to job-member records. Parent PIDs alone are insufficient; creation-time matching handles PID reuse. Unknown processes remain application-owned, and the primary application is excluded from console discovery.

The first concurrent run did not confirm graceful close after recovery; its remaining live processes were the replacement worker and a console host, while ILSpy had exited. Later runs closed gracefully. Console accounting is now more explicit, but that one early timeout has no independently established sole cause. Useful failure evidence is retained.

## Input limitation and verification

A broader run exposed a stale WPF telemetry read and a posted Shift+End that lost the temporary Shift state. Posted modifier transactions still depend on fixed dwell times and do not acknowledge processing. The viewer uses synchronous chords. Verification now polls actual resulting state without resending input. The development runner explicitly requires synchronous results and retains posted research outcomes separately; the default all-variants research command still requires all tested variants. The final full run reports `allSynchronousDemonstrated=true`, `allDemonstrated=false`: posted WPF Shift+End still failed after observation retries. This is a disclosed compatibility limit, not complete virtual keyboard emulation.

The complete required development regression passed at `artifacts/development-20261014-112604`: core isolation/fault containment, existing viewers, synchronous modifiers, private pointer state, Qt GPU, three WPF modes including hidden inspection, unchanged-adapter controls, ordinary ILSpy/Chrome, reusable GPU viewer, worker recovery, helpers created during restart, save prompts and launch/duplicate checks. Final concurrent, pair-launch and passive display benchmark results and cleanup/storage audits are recorded in `artifacts/concurrent-inspection-status.json`.

Cleanup audit: zero owned experimental application survivors; all 646 recorded files across four preserved snapshots match; script parsing is clean. All 60 identified disposable browser profiles remain retained, totaling 1,549.79 MiB (about 1.51 GiB), an increase of about 54 MiB for regression coverage. No profile deletion, Git operations, production data edits, drivers or persistent/privileged Windows changes were attempted. Failure references are consolidated in `artifacts/m7-failure-notes.json`.

## Working demonstration

```powershell
.\scripts\ilspy-pair-demo.ps1 -SkipBuild
```

Each window shows its own source PID. Take **Human control** in one viewer while the other remains under automation. Search for different types, select code, and return ownership independently. Use **Hide app window**, then **Inspect hidden app** to inspect it without leaving it shown. Close each viewer normally. The first launch respects the existing duplicate guard; the second explicitly permits the intentional second viewer. A pair manifest links both session status files. Startup failure preserves any already-running owned viewer for inspection rather than silently discarding it.

Automated physical acceptance is not claimed; the new manual demonstration is pending. The next promising experiment is temporary private presentation and input resumption for ordinary GPU applications such as Chrome, plus longer mixed workloads. The new operation supplies useful visual inspection, while continuous virtual pointer/device semantics, arbitrary hidden mouse interaction and default opaque hardware WPF capture remain unresolved.

# Milestone 17 — Standalone cursor guard and visual readiness

October 15, 2026. **qIV now supports native inspection from startup without initializing GPU capture.** A separate small DLL protects its imported cursor-confinement calls; its owned installer exits immediately. The viewer can subsequently enable hidden GPU sharing and return to native inspection without restarting the application or changing input ownership.

Larger images also exposed a useful limit: **a successful capture can contain the previous application state**, including through GPU sharing. The new experiment retains those stale first frames and verifies later frames through bounded inspection-only retries. This extends the accepted private-desktop implementation rather than replacing it.

## Implementation and compatibility

`QivCursorGuard.dll`, `QivGuardHost.exe` and `QivCursorGuardSession` separate protection from graphics setup. Both native binaries have no direct D3D, DXGI, WIC or DWM imports; the installer also reported no loaded D3D11/DXGI modules. Before GPU mode was requested, the source had no `QivPresentBridge.dll` loaded. The ordinary source application still creates and uses its own graphics resources normally.

The installer accepts only the exact pinned qIV executable, validates its SHA256 independently, and checks process creation identity, session, configured root class and non-input private desktop. The wrapper assigns it to the owned job before sending its command. Changed creation identity, input-desktop selection and an ordinary unrelated executable were rejected before installation. No privilege adjustment, security-boundary bypass or system-wide interception was introduced.

The installed DLL and its 4 KiB shared telemetry mapping remain with the owned source until exit. The GPU bridge recognizes and verifies this guard instead of replacing it. Actual application confinement calls remained denied through worker recovery and repeated backend changes. A refusal probe calls only the installed replacement functions, never the original physical-cursor APIs.

The pinned release exposes **one named ClipCursor import and zero named SetCursorPos imports**. Earlier code conditionally guarded SetCursorPos; that did not establish cursor-warp virtualization. Current documentation now states this distinction. The guard covers the tested executable's named imports, not every possible late-bound call or loaded module.

`DemoForm`, `DemoSession` and the qIV script add an explicit native-startup option. This path defaults to a configurable 30 ms native settle in the script. The accepted GPU-startup route and its zero-settle native-switch experiment remain available. Input/capture work stays off the viewer UI thread, under the existing serialized command and ownership controls.

The actual viewer passed native startup, Ctrl-wheel zoom, buffered pan, non-owner rejection, worker/reader recovery and repeated switches. Two independent sources simultaneously held different images while using different capture backends. The real launcher passed visible startup, duplicate refusal, reopening, graceful/forced closure and target rejection. Human senders in these tests are automated; physical acceptance remains pending.

## Large-image experiment

Four disposable **4096×3072 PNGs** contain distinct binary visual identifiers. Together they occupy **1,775,048 bytes / 1.693 MiB**. The application receives only ordinary files and native Home/End input; it has no research adapter or awareness of the identifiers.

The pinned source clamps neighbor preloading to at least one image and its effective GPU cache to at least one entry. Tests request the lowest cache setting and navigate between far endpoints, beyond adjacent preload. This is cache-constrained application navigation, not proof of a cold filesystem. No OS cache flush was attempted.

Latest complete evidence: `artifacts/qiv-guard-research-20261015-033124`. Four transitions were measured per condition:

| Capture condition | Correct first frame | Median first return | Median verified ready |
| --- | ---: | ---: | ---: |
| Standalone native, zero added settle | 0/4 | 14.7 ms | 53.5 ms |
| Standalone native, 30 ms settle | 4/4 | 58.4 ms | 60.6 ms |
| Standalone native, 100 ms settle | 4/4 | 124.2 ms | 126.6 ms |
| Native with sharing installed, zero settle | 0/4 | 14.8 ms | 49.6 ms |
| Native with sharing installed, 30 ms settle | 4/4 | 51.7 ms | 53.6 ms |
| Hidden GPU sharing, immediate inspection | 0/4 | 23.6 ms | 71.7 ms |

All **24 transitions** reached the selected image within two inspections. Twelve first frames were stale and remain explicitly recorded; they are not counted as immediate success. A separate earlier run reproduced the same freshness pattern. Readiness retries do not replay navigation. First-return timing includes input, capture and IPC; verified-ready timing additionally includes marker validation, the 15 ms retry interval and any repeated inspection.

In one retained pair, qIV already displayed its “Last image: 4” message while the image pixels still identified image 1. The next inspection showed image 4. A selection label alone therefore did not establish visual readiness in this test.

The 30 ms condition worked for these files in these runs. It is not a guaranteed completion signal. Known visual markers are a research oracle, not a production readiness heuristic. Source background decoding can continue after the input handler returns, while a graphics copy can correctly copy the still-old back buffer. Microsoft describes [PrintWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-printwindow) as synchronous source rendering; its return value does not acknowledge another application task's completion.

## Performance and resource observations

Standalone guard setup took **50.8 ms in the installer / 95.7 ms including managed validation and process startup** in the latest direct test. This is not a statistically established startup improvement over the previous route. The demonstrated improvement is eliminating wrapper GPU initialization and a retained graphics reader when native inspection is sufficient.

The DLL is **36,352 bytes** and the installer **71,680 bytes** on disk; those are not RAM-footprint estimates. Dependency evidence is retained in `artifacts/m17-guard-dependencies.json` and the corresponding dumpbin text files.

During the large-image workload, measured child processes used about **2.62% machine CPU over 1.69 s** in the standalone-native phase and **2.60% over 1.58 s** in the mixed phase. These are different mixes of settle intervals and retries, so they do not rank backend CPU efficiency. Supervisor image generation and validation CPU is excluded. Source private memory measured approximately **234 MiB** and later **289 MiB**; the added GPU reader held **50 MiB**, and the native worker approximately **23–33 MiB**. Decode/preload/capture state all contribute; the increase is not attributed solely to sharing. Individual GPU-engine measurements are retained without claiming whole-device utilization. The original 22% observation remains unexplained.

## Verification, failures and cleanup

The new suite passed **187 assertions and 103 named direct/viewer frames**, plus the 24 large-image navigation/readiness observations. The stale first frames are expected recorded research outcomes, not hidden assertion failures. The accepted capture-comparison and mpv/dynamic viewer suites also passed on the updated build; remaining regression results are indexed in `artifacts/m17-verification-index.json`.

The first guard-only run failed because my import-pointer verification used a read/modify/write operation after restoring read-only protection. A read-only verification corrected it. That failed run remains at `artifacts/m17-guard-initial`; its owned application cleaned up. This was an implementation error, not a Windows compatibility conclusion.

The accepted M16 sources, working managed binaries and native builds were copied before edits to `artifacts/baselines/accepted-capture-backends-20261015`, with a hash manifest. Final process, build, storage and baseline checks are recorded in `artifacts/m17-final-audit.json`. Browser profiles remain retained without deletion attempts; no Git operations were performed.

The audit found **zero owned process survivors**, **zero mismatches across 2,912 files in 15 saved baselines**, no script parse errors and no managed/native sources newer than the tested builds. Pinned qIV, mpv and accepted D3DProbe hashes match. Browser profiles remain **123 / 3,976.239 MiB**, with no new profiles. The two retained large-image sets total **8 PNGs / 3.386 MiB**; retained Y4M media totals **56 files / 184.585 MiB**. No deletion policy was bypassed.

## Demonstration and way forward

From `C:\Programming\Projects\VIAVW`:

```powershell
.\scripts\demo-qiv.ps1 -NativeStartup -SkipBuild
# Reproduce the complete experiment:
.\scripts\test-qiv-guard.ps1 -SkipBuild
```

Take **Human control**, zoom and pan, then choose **Use hidden GPU capture** and confirm the same application and state remain. Return to **Use native inspection**, hide the preview, change the image under automation, and inspect later. Return ownership to automation to reject picture input. The optional `-NativeInspectionSettleMilliseconds 0` selects the immediate native experiment; 30 is the new script's default. Close the viewer normally afterward.

Physical human acceptance is pending. Protection remains process-local instrumentation of an unchanged executable, not independent hardware input. The preview is unscaled, separate dialogs/menus are not composed, and raw input, IME, real device removal and GPU migration remain unverified.

The evidence supports separating **input delivery**, **capture completion** and **task readiness**. A useful next experiment is a bounded caller-supplied visual readiness condition, followed by ordinary multi-window/dialog interactions. The application should continue to run normally; an external observer can decide whether its intended result is visible without replaying input or requiring application modifications.

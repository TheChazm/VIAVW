# Milestone 2 — Compatibility, input/output, and lifecycle experiments

This milestone completed the bounded experiments described below. It expands the working prototype and identifies important compatibility limits; it does not establish universal input or capture support.

Milestone 1 remains accepted. The user's physical-desktop, inspection, and ownership-handoff observations are recorded separately in [MANUAL-ACCEPTANCE.md](MANUAL-ACCEPTANCE.md), with no invented timestamps, screenshots, or automated telemetry. The original M1 measurements remain unchanged in [MILESTONE-1.md](MILESTONE-1.md).

## Launch and working demonstrations

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\build.ps1
.\scripts\demo.ps1 -SkipBuild

# Qt Quick with child-only software rendering and native PrintWindow
.\scripts\quick-demo.ps1 -SkipBuild -Software

# Normal Qt Quick GPU rendering with explicit cooperative fixture capture
.\scripts\quick-demo.ps1 -SkipBuild

# WPF with cooperative rendering; keyboard navigation is more usable than mouse
.\scripts\wpf-demo.ps1 -SkipBuild
```

The default launcher now opens the **visible viewer**. It no longer applies Hidden to the actual DemoForm. A console owned solely by the demo is detached; an existing shared user terminal is untouched. The private applications/workers remain independent of Layer 0.

`-Hidden` explicitly hides the controller. Its printed status-file path can be passed to `scripts/show-demo.ps1 -SessionFile <path>` to reveal the same viewer/process/application state. This is voluntary restoration of the selected session, not silent attachment to an unrelated process. `-AllowDuplicate` explicitly permits another demo; otherwise a per-build-directory/per-desktop session mutex refuses duplicates. Older builds without that mutex are not retroactively controlled.

The launcher preserves `-SkipBuild`, validates executable paths, reports startup state/errors, and uses fresh status files under ignored project artifacts. A target that produces no usable owned window or hangs during its initial capture causes a clear startup failure and owned-resource cleanup. A timeout now identifies the operation and deadline.

Normal viewer close first requests application close. If a save prompt or remaining owned process prevents exit, the controller stays available under human ownership. **Force close** explicitly discards the owned layer's remaining unsaved state. Low-level `Dispose` is still emergency job termination.

## Architecture changes

```text
Viewer UI thread
  events, painting, status, voluntary ownership requests
    serialized asynchronous command queue
      background task: launch / enumerate / input / capture / close
        authenticated per-layer pipe
          worker on private Windows desktop
            ordinary target + descendants in kill-on-close job
```

The viewer is now multithreaded: startup, pipe waits, native input, capture, and graceful-close polling run off its UI thread. A semaphore orders viewer commands; the existing layer lock orders native RPC/ownership transitions. Automation timer ticks do not accumulate a backlog. Human-input preview refresh is debounced, and idle/hidden preview has no continuous capture loop.

Each layer still uses one existing Windows installation, normal processes, ordinary OS resources, a private desktop, and a job. No service, database, VM, new Windows installation, display driver, global input hook, or process injection was introduced.

Additional API operations are sent/posted keys and characters, bounded chord experiments, mouse message variants, owned focus/selection diagnostics, explicit edit/scrollbar control adapters, capture backend choice, graceful close, and explicit worker restart. The Chromium adapter now accepts a page-specific modifier mask.

Posted delivery is not a target acknowledgement. The host serializes delivery requests, but an application may process queued keys or asynchronous accessibility actions after the request returns. Clients must inspect/acknowledge the actual result before changing ownership. The accepted automation typing path uses synchronous `WM_CHAR`; no interrupted input is automatically replayed.

## Compatibility matrix

Legend: **N** = demonstrated through targeted native messages; **A** = demonstrated through an explicitly labeled adapter or application configuration; **P** = partial/indeterminate; **F** = failed to demonstrate the intended effect; **U** = untested. A successful Win32/CDP call alone is not treated as application success.

| Application | Text/navigation | Activation | Selection/editing | Modal/context behavior | Useful visual output | Handoff |
|---|---|---|---|---|---|---|
| Ordinary WinForms fixture | N text/Left; original checks retained | N left click | Advanced selection U | Advanced modal/context U | N PrintWindow; SW_HIDE black | N original and actual viewer regression |
| Unmodified Character Map | N characters, posted Tab/Enter | N Select appends glyph | N double-click word selection; A EM_SETSEL plus WM_CHAR replacement; drag F | Right-click context F | N PrintWindow | M1 native ownership regression; new handoff U |
| Unmodified winver.exe | Text entry not applicable | N OK click exits its dialog | Not applicable | Ordinary system dialog launched and closed | Native capture visually inspected in separate probe | U |
| Qt Widgets 6.10.3 fixture | M1 N text/navigation | M1 N click | New richer operations U | U | M1 N PrintWindow | M1 verified; no new framework-specific regression |
| Qt Quick 6.10.3, normal Direct3D11 renderer | N text/Left/Tab; Enter F; Space N | N left click | N drag/double-click selection | N right-click menu, modal opening, Escape close | GPU PrintWindow F; A cooperative grabWindow correct | N input/state preservation; actual viewer passes with A capture |
| Same Qt Quick app, child-only software configuration | Same tested native input results | N left click | N drag/double-click selection | N menu/dialog/Escape | A renderer configuration enables useful native PrintWindow | Actual viewer passes without cooperative capture |
| WPF standard TextBox/Button plus custom visual | N text/Left/Tab/Enter | N keyboard default-button activation; sent/posted mouse F | Native drag/double-click/modified selection F | P delayed UIA opening; Escape demonstrated when dialog actually opened; save dialog verified separately | PrintWindow F; A RenderTargetBitmap correct | N keyboard/state/ownership; mouse handoff limited |
| Hardware Direct3D11 custom window | N WM_CHAR receipt and state; no full text-editor UI | N down handler changes triangle color | U | U | Private PrintWindow F; private WGC F; ordinary-desktop WGC control succeeds | N state/PID/owner; usable private GPU preview unresolved |
| Chromium/Chrome 154.0.8037.98 local page | A CDP keys and Ctrl+A | A CDP mouse | A Ctrl+A selects complete value; typing replaces it | Other expanded operations U | A page screenshot | Same browser/page/PID; non-owner input rejected |

The QML app uses actual TextField/Button/Dialog/Flickable/DragHandler controls and its normal event behavior. WPF uses ordinary framework controls, routed commands, a custom FrameworkElement, and a normal save MessageBox. Passive counters/hooks observe fixture behavior; they do not replace its input handling. The production Profile Builder, POS, and Charlie AI were not opened or modified.

## Expanded input findings

| Interaction | Concrete outcome |
|---|---|
| Ctrl+A | Native posted chords failed in QML/WPF/Character Map. Character Map received a literal `a` instead of selection. CDP's modifier mask selected the browser's entire value and subsequent key events replaced it. |
| Shift+End | No intended native modified selection demonstrated in QML/WPF. |
| Ctrl+K / Alt+K | Real QML/WPF shortcut handlers did not activate. WPF observed modifier state `None`; Direct3D's GetKeyState/GetAsyncKeyState-dependent Ctrl handler remained false. |
| Alt side effect | Synthetic system-key messages can invoke normal system-menu behavior rather than the intended accelerator. WPF tracing recorded SC_KEYMENU. The test cancels only the owned pending menu before continuing; an earlier exploratory run lost its window during subsequent operations. This is a compatibility risk, not an independent modifier device. |
| Tab | Posted navigation moved QML focus to its counter and Character Map focus to Select. WPF moved from TextBox to Button. |
| Enter / Space | WPF Enter executed the actual default Button.Click. Character Map Enter on Select appended a glyph. QML Enter did not click; Space did. |
| Hover | The tested native Move messages did not establish the observed QML/WPF hover state. |
| Wheel | Actual QML/WPF scroll offset and Character Map scrollbar position did not change through the tested wheel messages. A labeled WM_VSCROLL parent/control adapter moved Character Map's scrollbar from 0 to 1. |
| Drag / double-click | QML selection changed through down/move/up and double-click messages. Character Map double-click selected a word; its tested drag did not select. WPF selection did not respond. |
| Right-click/context menu | QML's ordinary right-button handler opened its menu. WPF and Character Map did not demonstrate the intended menu. |
| Modal/Escape | QML opened a real Dialog and Escape closed it. WPF UIA Invoke returned without a timely confirmed effect; a dialog appeared later in some runs and Escape was tested only when its open state was observed. |
| Native control adapters | EM_SETSEL selected the complete Character Map value; subsequent WM_CHAR replaced that selection with `REPLACED_BY_EDIT_PATH`. WM_VSCROLL moved its existing scrollbar. These are control operations, not Ctrl+A or wheel-device emulation. |

The API's mouse variants include held-button flags and screen-coordinate wheel delivery. The actual human picture widget still forwards simple left clicks/text/navigation. API/test-harness drag, right-button, and wheel results are not a claim that these hardware gestures are fully forwarded by that widget.

WPF's raw observer saw native mouse messages while its intended controls did not respond. This proves arrival, not usable pointer semantics. Its accessibility Invoke requests were also not a reliable timely substitute in these private-desktop tests; a later counter increment is retained as partial/indeterminate evidence. No global cursor movement, foreground takeover, keyboard-state modification, AttachThreadInput, or HID emulation was added to force a success.

## Capture experiments and correctness

Before/after frames were saved and visually inspected. Known green/orange marker pixels were independently checked against the fixture's observed counter state. Black-fraction sampling also identifies empty images. These checks establish the tested text/marker representation, not pixel-perfect coverage of every possible control/shader.

| Backend and environment | Result |
|---|---|
| PrintWindow, visible private WinForms/Character Map | Useful ordinary control output; retained baseline. |
| PrintWindow, private Qt Quick Direct3D11 / WPF / native flip-model D3D11 | API succeeded but client images were white and lacked the expected marker/content. |
| PrintWindow, SW_HIDE modern test windows | Black frames despite continuing application timer/state workload. |
| PrintWindow, Qt Quick child environment `QT_QUICK_BACKEND=software` | Text/buttons/marker rendered. Green before and orange after pixels verified. This is a launch configuration, not an application input modification. Some styled TextField outline/background details differ from GPU rendering. |
| Cooperative QQuickWindow.grabWindow | Correct GPU QML scene, text and orange marker on the private desktop. Requires explicit fixture/application integration. |
| Cooperative WPF RenderTargetBitmap | Correct standard/custom visual tree, text and marker on the private desktop. Requires integration. |
| Cooperative GPU QML / WPF after SW_HIDE | Both produced correct text/marker images while the actual native window was hidden; timer state continued. This is an integration result, not a change to hidden PrintWindow support. |
| WGC helper on private desktop | Capture item reported 0×0. Using valid explicit window dimensions did not solve frame-pool creation: HRESULT 0x8007139F (resource not in correct state). |
| WGC helper on Default targeting an owned private window | Also reported 0×0 and failed with explicit buffer dimensions. The helper never switched the desktop or sent input. |
| WGC control: same hardware D3D fixture on ordinary input desktop | Correct green GPU triangle captured. App used SW_SHOWNOACTIVATE, no input was sent, and recorded foreground/input desktop remained unchanged. |

The WGC control rules out a wholly nonfunctional capture implementation on this machine. It does not prove the cause or universality of the private-desktop restriction. The native Direct3D fixture reported hardware adapter **NVIDIA GeForce RTX 4060 Ti** and Present result **0x087A0001 / DXGI_STATUS_OCCLUDED** on its non-input desktop. It continued its bounded timer/render workload. That observation is consistent with inactive composition, but it is not a complete DWM/driver analysis.

The WGC helper creates a free-threaded frame pool and reads its Direct3D surface through IDirect3DDxgiInterfaceAccess into a staging texture and WIC PNG. This demonstrates D3D surface acquisition for a WGC-provided frame. It does not obtain an arbitrary foreign application's swap-chain texture. No undocumented swap-chain interception was attempted.

WGC is bounded to one on-demand capture; failures preserve the target. Cooperative capture is also requested on demand. No idle permanent GPU capture stream, virtual monitor, or display reconfiguration was introduced. Continuous WGC private-desktop capture could not be compared because first-frame setup did not work there.

## Human/automation preservation and physical isolation

The original WinForms viewer regression passes after the asynchronous refactor. Both actual Qt Quick viewer variants pass: automation enters text, the viewer routes `HUMAN` and a counter click, return to automation rejects further human text, and automation resumes in the same QML process with retained document/counter state. The GPU variant labels cooperative capture; the software variant uses native PrintWindow.

WPF and Direct3D API-level ownership/state checks also pass within the operations their frameworks accept. They do not acquire universal mouse behavior or a usable private GPU capture through those ownership checks.

The combined framework reports retained the same foreground HWND and `Default` input desktop before/after their experiments. Original native focus/pointer sampling passed. This remains sampled evidence, supplemented by the user's accepted manual M1 workflow. A legitimate user focus switch or pointer movement is kept in diagnostics and is not automatically attributed to VIAVW.

Default viewer startup/reveal is explicitly requested UI presentation and can naturally focus the controller. It is separate from private application input. There is no target focus-steal-and-restore implementation.

## Threading and performance

The user reported seeing approximately 22% in Task Manager for a period during development. No per-process trace or exact time was supplied, so that observation is not assigned to VIAVW, compilation, capture, or another cause. The new measurements did not reproduce that load.

The performance run used **four concurrent layers** (WinForms, WPF, normal GPU Qt Quick, hardware D3D), four seconds of warmup, **30 seconds idle**, and approximately **six seconds of repeated text input/on-demand inspection**. All four target text values accumulated fifteen `x` characters. GPU counters were read only for owned PIDs over separate 1.5-second intervals. Application telemetry costs are included in application CPU; manager GPU-counter diagnostics are included in manager CPU.

| Measurement | Actual observation |
|---|---:|
| Logical processors used for machine normalization | 24 |
| Manager idle CPU | 1.77% of one core / 0.07% of machine |
| Layer workers idle CPU | No measurable CPU-time increase in that interval |
| Application idle CPU | WinForms 0.47%, WPF 1.98%, Qt Quick 0.16%, D3D 0.05% of one core |
| Active manager CPU | 8.39% of one core / 0.35% of machine |
| Active workers CPU | 3.30–5.59% of one core each; approximately 0.14–0.23% of machine each |
| Active applications CPU | WPF 9.91%, Qt Quick 4.32%, WinForms 1.27%, D3D 0.25% of one core |
| Synchronous text input, 60 samples | median 0.95 ms; p95 3.44 ms |
| WinForms native capture, 15 samples | median 7.30 ms |
| WPF cooperative capture, 15 samples | median 194.69 ms |
| GPU Qt Quick cooperative capture, 15 samples | median 160.26 ms |
| D3D PrintWindow, 15 samples | median 7.38 ms **with incorrect pixels; not useful GPU acquisition** |
| Qt Quick software native capture after interaction | 8.96 ms single sample; marker/text visually verified |
| WGC ordinary-desktop control | 243.51 ms cold helper; 951 ms full calibration including app startup and explicit warmup |
| Manager working set / private bytes | 62.8 / 29.3 MiB in diagnostic benchmark |
| Workers working set / private bytes | 40.3–42.8 / 11.4–13.8 MiB each |
| Worker console helpers | approximately 7.95 / 1.18 MiB each |
| Apps working set / private bytes | WinForms 54.9/17.5; WPF 131.9/78.1; Qt Quick 108.6/103.2; D3D 42.2/51.3 MiB |
| Startup including layer/worker and ready target window | about 265 ms WinForms, 891 ms WPF, 759 ms QML, 275 ms D3D in benchmark |
| Graceful shutdown | about 64–125 ms in benchmark without save prompt |
| GPU-engine activity in inspection sample | QML 3D engine about 0.18%; WPF about 0.09%; D3D about 0.04% |
| Viewer during deliberately stalled startup capture | final launcher test: 95 UI pings; median 0.024 ms; maximum 2.00 ms |

Cooperative capture latency includes its 200 ms fixture request polling, readback/rendering, PNG encoding, file transfer, and IPC. It is not intrinsic Qt/WPF rendering latency. Native timings likewise include IPC/encoding. GPU engine values are short per-engine samples, not a summed whole-GPU usage claim. Working sets share runtime/OS pages and cannot be added as unique RAM.

The original M1 mouse median **0.82 ms**, capture median **8.29 ms**, approximately **45 MiB** manager, **33–43 MiB** workers, and short no-measurable-idle-CPU sample remain historical measurements. The M2 results do not turn that short observation into a sustained zero-CPU claim. Thirty seconds is still not an endurance test; hours/days of idle and mixed activity remain untested.

## Lifecycle, recovery, and failure results

- Default launcher visibility, explicit Hidden, reveal of the same PID, duplicate rejection without takeover, missing-executable errors, and normal owned-tree cleanup pass.
- Graceful WPF close preserves the process at its real save prompt, transfers ownership to Human, and writes exactly `M2_SAVE_DOCUMENT` after targeted Enter. It then exits normally. No user's file is involved.
- Explicit `RestartWorker` exits only the exact-job-validated worker, creates a fresh authenticated pipe/worker on the same desktop, preserves application PID/HWND/text and Human ownership, rejects a non-owner, and accepts resumed text. It does not replay input.
- A worker restart does not repair an application hung in its own print handler. A five-second stalled-capture deadline still performs emergency termination of that layer and loses its unsaved state; other layers remain alive.
- Abrupt manager termination still kills its worker/application/child job tree. A new manager cannot reconnect to a killed job. Full viewer-process disconnect/reconnect with independent manager lifetime is **not implemented**; hiding/revealing the same controller is supported.
- Native regression passes all 19 original assertions, including child cleanup and private desktop object release. Final process inspection found no surviving test processes with the named host/fixture/helper/utility names.

Graceful timeout does not silently discard a save prompt. A non-window child that remains after its parent exits can still require explicit force close. Runtime/queued/provider work must be observed rather than presumed completed. Experiments retain earlier failures separately; they are not relabeled as passing results.

## Reproduction and evidence

```powershell
.\scripts\test-milestone2.ps1
# Existing compiled binaries/helpers:
.\scripts\test-milestone2.ps1 -SkipBuild
# Avoid transient visible calibration windows and/or skip benchmark:
.\scripts\test-milestone2.ps1 -SkipBuild -SkipVisibleLauncherTest -SkipBenchmark
```

The successful combined experiment run is `artifacts\milestone2-20261013-224750` (benchmark omitted there). Its directories contain native/viewer regressions, save/recovery/launcher tests, unmodified Win32, WPF, GPU/software QML, both QML viewer handoffs, hardware D3D, expanded Chromium, and WGC control evidence.

Additional final evidence is `artifacts\m2-performance\performance.json`, `artifacts\m2-launcher-final\launcher.json` (clear timeout and console handling), `artifacts\m2-win32-final\win32-expanded.json` / `winver-interaction.json` (final scrollbar adapter and second utility), and the subsequent `artifacts\native-20261013-225029\verification.json` regression. Paths identify actual executed evidence; manually reported M1 acceptance is not merged into automated telemetry.

`artifacts\m2-hidden-capture-final\hidden-capture.json` verifies native visibility=false, retained `HIDDEN_BACKGROUND`, advancing timer ticks, and correct cooperative pixels for GPU QML and WPF. Those frames were visually inspected. `artifacts\m2-final-viewer-regression\viewer-verification.json` is the final compiled viewer regression.

Source files, artifacts, `.deps`, binaries, captures, profiles, logs, and benchmarks follow the existing project boundary and ignore rules. No Git initialization/commit/push/merge/branch/tag, production project edit, privileged install, driver, global input change, or persistent Windows configuration change was performed. Existing tool/runtime caches and ordinary OS graphics/process behavior are outside the source tree but do not represent a system-configuration change.

## Remaining limits and next experiment

Completed: launcher correction, accepted-evidence recording, asynchronous viewer, richer scoped native API, four framework classes, unmodified utilities, explicit capture comparisons, handoff regression, bounded CPU/memory/GPU measurements, save opportunity, owned worker replacement, and failure containment.

Partial: WPF native pointer behavior and asynchronous accessibility operations; Qt software style/feature equivalence; GPU graphical independence when the application offers no cooperative readback. Unsupported in these tests: useful private-desktop WGC, genuine modifier/keyboard-state virtualization, and general GPU PrintWindow. Untested: universal app compatibility, Raw Input/DirectInput/gamepad/IME, protected/elevated/secure content, drag/drop between processes, multi-monitor/high-DPI breadth, a persistent capture stream, long endurance, and manager-process reconnection.

The strongest next experiment is an independently built **unmodified Qt Quick application** launched with the same child-only software environment, including more controls, shader/effect/style coverage, and a longer CPU/GPU comparison. That can extend useful capture without a driver or application input changes. For applications requiring GPU rendering, compare a clearly cooperative texture/readback adapter before proposing interception. The WGC private-desktop failure alone does not prove the overall concept impossible, and the software/fixture success does not prove universal support.

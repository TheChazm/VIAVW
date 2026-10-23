# VIAVW — Virtual Application Layers for Windows

Licensed under [MIT](LICENSE). Copyright (c) 2026 The Coded Craftsman, LLC. Third-party components retain their own licenses and notices.

[Clean-checkout build and packaging](docs/BUILDING.md) describes the tracked-source mode and prerequisites. [Public validation summary](docs/validation/2026-10-23-source-build.md) separates reproducible build evidence from application/Human acceptance. Historical `artifacts/` paths throughout these reports are local generated evidence, excluded from Git; they are not supplied by a clean checkout.

[Windows installation and live Codex connection](docs/WINDOWS-INSTALLATION-20261023.md): VIAVW is installed at `%LOCALAPPDATA%\Programs\VIAVW`, with a Start menu manager and a standard Windows MSI in `dist`. Install, repair, removal, live-layer protection and document retention passed automated tests. Codex's registered VIAVW tools were used directly in this conversation to edit and save an installed Notepad++ document. CharlieAI remains unchanged.

[Standalone tool package](docs/STANDALONE-TOOL.md) supplies a self-contained Windows executable, 13 discoverable MCP tools, a JSON command interface and an optional Human viewer. AI clients connect externally; CharlieAI remains unchanged. The packaged release passed direct installed Notepad++/7-Zip workflows and official MCP SDK tests, including controller-crash recovery and two independent applications. See [package verification](docs/STANDALONE-TOOL-20261023.md) and `artifacts/standalone-tool-current.json` for the tested ZIP/executable.

[Durable application layers — verified October 23](docs/DURABLE-LAYERS-20261023.md) separates application lifetime from clients/workers, preserves Human ownership and unsaved application memory through tested controller faults, and adds reconnectable independent layers. The selected release passed 1,012 edit/save cycles over twenty minutes with sixteen controller replacements, 98 release checks and the 33-case Notepad++/Visual Studio legacy regression. Run `scripts/demo-layers.ps1`. Actual CharlieAI integration and physical human acceptance remain pending; older viewer/API selections are preserved.

[Latest interaction fluidity results](docs/INTERACTION-FLUIDITY-20261022.md) select the verified prepared viewer: startup pointer preparation, 29 VS workflows, 66 menu paths, 15 mouse checks and 32 responsiveness checks. Final median glyph-to-viewer-paint time was 22.76 ms in seven samples; further cadence/painting changes did not show a reliable steady-state gain. Foreground-native parity and physical acceptance remain unverified.

[Installed applications and direct Codex control](docs/INSTALLED-APPLICATIONS-20261022.md) records actual Word editing/DOCX saving, VLC video and 7-Zip extraction through the wrapper, per-application capture options, repeatable launchers and a persistent Codex console. Menu/input limits and an intermittent deep-accessibility stall remain documented; the accepted executables are preserved.

[CharlieAI collaboration readiness](docs/CHARLIEAI-READINESS.md) adds a persistent workspace owner, context-checked return from Human control, save-prompt-preserving shutdown, a shared-application launcher, and one-command acceptance checks. CharlieAI remains unchanged by request. Run `scripts/demo-collaboration.ps1` or `scripts/test-agent-readiness.ps1`; physical human acceptance remains pending.

A standalone research prototype for launching ordinary applications on private Windows desktop objects, interacting through targeted window messages, and inspecting on-demand captures. Milestone 1 is accepted with [user-reported manual evidence](docs/MANUAL-ACCEPTANCE.md). Milestone 2 adds framework/input/capture experiments, a responsive viewer, and explicit lifecycle handling. [Milestone 3](docs/MILESTONE-3.md) adds modifier state and offscreen GPU research. [Continued development](docs/DEVELOPMENT-20261014.md) demonstrates native GPU capture and native Chrome input on private desktops, improved pointer semantics and reusable WGC resources. [WPF rendering research](docs/RENDERING-RESEARCH-20261014.md) adds native layered GPU output and an explicit startup software adapter for an unchanged disposable WPF executable.

**The baseline works for the tested controls and applications without switching the physical input desktop. It does not provide independent Windows keyboard/mouse devices or universal application compatibility.** Text is delivered through `WM_CHAR`; navigation uses `WM_KEYDOWN`/`WM_KEYUP`; simple clicks use mouse messages. These exercise ordinary application control/event handlers, but do not reproduce the complete hardware input pipeline.

[Local automation interface for CharlieAI integration](docs/AGENT-INTEGRATION-20261021.md) now exposes versioned sessions, observations, private input, attention and ownership handoff to external clients. Installed Notepad++ and Visual Studio workflows, failure containment and disconnect cleanup have automated verification. CharlieAI itself is unchanged. Try `scripts/demo-agent.ps1` for the disposable collaboration demonstration; see [protocol documentation](docs/AGENT-PROTOCOL.md). The existing Visual Studio demo remains available.

[Preview scheduling and split-region painting](docs/VIEWER-PREVIEW-DEADLINES-20261021.md) are now selected by the normal demo after 29 workflows, 66 menu paths and editing/mouse/recovery checks. Median individual-character paint completion improved from 49.1 to 22.2 ms in 14 samples per setting. Repeated sustained typing also passed; foreground-native equivalence and physical human acceptance remain unverified. Use `scripts/demo-visualstudio.ps1 -SkipBuild`; append `-PreviousInput` for the preserved earlier viewer or `-PrintCapture` for the printing viewer.

The [earlier incremental painting report](docs/VIEWER-INCREMENTAL-PAINT-20261021.md) preserves the previous implementation and measurements.

[Earlier October 21 mouse improvements](docs/VIEWER-SPEED-20261021.md) remain enabled: repeated Options popup handling improved from about 106 to 27 ms. The new viewer accelerates validated shortcuts while preserving pacing for application transitions.

[Viewer speed research](docs/VIEWER-SPEED-20261019.md) now uses reusable private-window DC capture by default for the configured VS software renderer. The preserved build passed 29 workflows, 66 nested menu paths and associated mouse, editing and resource checks. Corrected timing tests separate capture savings from variable glyph feedback. `scripts/demo-visualstudio.ps1 -SkipBuild` selects it; append `-PrintCapture` for the previous printing viewer. Foreground-equivalent speed remains unverified.

[October 20 speed experiments](docs/VIEWER-SPEED-20261020.md) compared earlier capture scheduling and a smaller, unscaled source window. Neither improved glyph feedback sufficiently; compact sizing exposed a restored-window menu failure. Both were kept out of the working viewer, and the maximized baseline passed a fresh mouse/edit/build/handoff recheck.

[Visual Studio executable workflows and capture timing](docs/VISUAL-STUDIO-END-TO-END-20261017.md) replaces the class-library demo with a runnable application, verifies actual run/debug and project-generation workflows, adds owned-window selection and standard accessibility assistance for WPF modal controls, and measures new glyph pixels. Earlier frame-report timing figures do not establish when edited text became visible. `scripts/demo-visualstudio.ps1 -SkipBuild` selects a separately preserved, hash-checked viewer through `artifacts/visual-studio-current.json`; it leaves an already running viewer's Release binaries alone. The report distinguishes selected workflow coverage from untested Visual Studio features and foreground-equivalent speed.

[Milestone 12 runtime adapter research](docs/RUNTIME-ADAPTER-RESEARCH-20261014.md) adds an isolated, explicitly instrumented D3D11 experiment: hidden private back-buffer sharing, reusable graphics resources, scoped asynchronous Ctrl state and two independent layers. It accepts only the unchanged project D3DProbe fixture; default viewers remain available. Unmodified Character Map also passed semantic UIA text updates, while its Invoke action failed. Run `scripts/test-runtime-adapter.ps1 -SkipBuild` for bounded experiments, screenshots and cleanup. Separate sessions and virtual display/HID drivers remain assessed proposals, with no enabling or installation performed.

[Milestone 13 dynamic GPU research](docs/DYNAMIC-GPU-RESEARCH-20261015.md) demonstrates an ordinary pinned portable **mpv** application on a private desktop with its native window hidden, using capture-only runtime instrumentation and native message input. It adds Present1/resize/device-recreation experiments, BGRA/RGBA readback and two concurrent mpv layers. Run `scripts/test-dynamic-gpu.ps1 -SkipBuild`; source applications close after verification. The ordinary executable stays unchanged on disk; the running process is instrumented.

[Milestone 14 GPU viewer integration](docs/GPU-VIEWER-RESEARCH-20261015.md) connects that backend to the multithreaded private viewer as an explicit option. Run `scripts/demo-mpv.ps1 -SkipBuild` for a visible viewer controlling ordinary mpv with its source window hidden. Inspection, ownership, resizing, reader/worker recovery and two concurrent viewers have automated verification through actual controls. `scripts/test-gpu-viewer.ps1 -SkipBuild` reproduces it on private QA desktops. Physical human acceptance remains pending; the default viewer keeps its existing capture path.

[Milestone 15 ordinary Direct2D research](docs/ORDINARY-D2D-RESEARCH-20261015.md) adds pinned **QuickImageViewer 3.2.0.452**, hidden GPU inspection, native wheel navigation, Ctrl-wheel zoom and buffered image panning. Run `scripts/demo-qiv.ps1 -SkipBuild`; `scripts/test-qiv-research.ps1 -SkipBuild` verifies the viewer, concurrent layers and real launcher. Its executable stays unchanged on disk, while the owned process receives capture instrumentation and a guard denying its imported cursor-confinement requests. Built-in dedicated file-backed configuration avoids its normal shell registration. The new suite passes; five older reports retain strict physical pointer/focus equality failures. Physical human acceptance remains pending.

[Milestone 16 capture backend research](docs/CAPTURE-BACKEND-RESEARCH-20261015.md) verifies fresh qIV images through temporary private native presentation, including after the GPU reader and capture interception stop. Its viewer now offers **Use native inspection** and **Use hidden GPU capture**, preserving the same source application and input owner across repeated switches. The native option closes the reader; cursor protection remains resident. `scripts/test-qiv-capture-comparison.ps1 -SkipBuild` compares full transactions, exercises a cold source without shared GPU frames and verifies concurrent viewers using different capture paths. Selection stays explicit and specific to the pinned disposable qIV demo.

[Milestone 17 guard and readiness research](docs/GUARD-READINESS-RESEARCH-20261015.md) adds `scripts/demo-qiv.ps1 -NativeStartup -SkipBuild`: native inspection starts with a small standalone cursor guard, without initializing GPU capture. GPU sharing remains available through the same viewer. Four large test images exposed stale first captures in both immediate native and GPU paths; bounded inspection-only retries verified the selected images. `scripts/test-qiv-guard.ps1 -SkipBuild` reproduces guard identity checks, native startup, handoff, mixed concurrent viewers, launcher cleanup and the large-image experiment. The new native-startup script uses a configurable 30 ms settle, which is an empirical delay rather than an application acknowledgement.

[Milestone 18 dialog and observation research](docs/DIALOG-OBSERVATION-RESEARCH-20261015.md) adds ordinary qIV file-picker interaction through the viewer while retaining its main GPU binding. Native modal capture, child input routing, ownership, worker recovery and simultaneous pickers have automated verification. `scripts/test-qiv-dialogs.ps1 -SkipBuild` also verifies bounded caller-defined visual conditions, cancellation, deadlines and ownership revisions without replaying input. Physical human acceptance remains pending.

[Milestone 19 window and menu research](docs/WINDOW-MENU-RESEARCH-20261015.md) adds a qIV **Window** chooser for ordinary Help/Directory panels, native system-menu inspection and navigation, and scaled previews with source-coordinate input. Main GPU resources remain bound while auxiliary windows use native capture. `scripts/test-qiv-windows.ps1 -SkipBuild` verifies concurrent menus, ownership, recovery, scaling and bounded report publication under reader conflicts. Physical mixed-DPI and human acceptance remain pending; menu command activation is limited.

## Start

[Faster interactive input and preview](docs/INTERACTIVE-LATENCY-20261016.md) removes PNG conversion from the default VS live preview, revalidates a retained pointer target instead of scanning the whole IDE for each click, and fixes rapid Enter/text ordering. The expanded responsiveness benchmark covers click/Enter/typing and immediate Backspace/typing alongside the existing ordinary workflow. Foreground-equivalent speed and physical acceptance remain unproven.

[Asynchronous preview repair](docs/ASYNC-PREVIEW-20261016.md) fixes the frozen New Project loading picture, follows changing content while Human views the application, and verifies non-default template selection, Next, configuration and Cancel through actual mouse input. Hide preview and Automation ownership pause the live picture. `scripts/test-visualstudio-new-project.ps1 -SkipBuild` reproduces the workflow; wizard project generation remains untested.

[Viewer responsiveness and Backspace](docs/VIEWER-RESPONSIVENESS-20261016.md) separates interactive preview from expensive diagnostics, reduces measured single-character frame-report latency from about one second to 80–125 ms, and verifies Backspace, repeated/word/selection deletion and forward Delete in installed VS. Refresh updates pixels; Inspect details collects a full read-only snapshot. Run `scripts/test-visualstudio-responsiveness.ps1 -SkipBuild` for the real-viewer benchmark. Foreground-equivalent speed and physical human acceptance remain unproven.

[Private mouse and menu workflow](docs/PRIVATE-MOUSE-MENUS-20261015.md) now covers installed Visual Studio mouse menus/submenus, text selection, wheel scrolling, context commands and pointer recovery from Output. The viewer composes native popup windows and applies capture policies suited to menus and text controls. Text dragging uses an anchor/Shift-click translation; general window docking and hardware input equivalence remain unverified.

[Visual Studio mouse follow-up](docs/VISUAL-STUDIO-MOUSE-20261015.md) records the user's failed mouse usability experience and narrower automated caret evidence. The demo now opens `Greeting.cs` immediately and starts maximized. The later private mouse/menu workflow above supersedes those earlier pointer limitations within its tested scope.

[Visual Studio input and build research](docs/VISUAL-STUDIO-INPUT-BUILD-20261015.md) adds working Ctrl+Tab release timing and an actual viewer workflow that edits two files, switches/reopens them, builds through the IDE, returns from Output through Ctrl+O, edits again and verifies ownership. The installed IDE still uses normal packages and its built-in software-rendering option in an experimental profile. Run `scripts/demo-visualstudio.ps1 -SkipBuild` or `scripts/test-visualstudio.ps1 -SkipBuild`. Native GPU capture remains unresolved. The mouse/menu follow-up above extends this keyboard baseline. The [earlier working configuration](docs/VISUAL-STUDIO-PRIVATE-WORKFLOW-20261015.md) and [initial failure report](docs/VISUAL-STUDIO-RESEARCH-20261015.md) retain their evidence and limitations.

The [compatibility testing standard](docs/COMPATIBILITY-TESTING.md) now prioritizes ordinary workflows. The [Notepad++ document workflow](docs/NOTEPAD-WORKFLOW-20261015.md) opens, selects, edits, saves, switches, closes and reopens multiple disposable files through the viewer. It exposed and resolved cropped dialogs and key-release routing during Ctrl+Tab. The Notepad++ demo now fits its preview; its GDI compatibility setting is still required for readable document pixels.

[Installed Notepad++ compatibility](docs/INSTALLED-NOTEPAD-RESEARCH-20261015.md) now tests the workstation's existing Notepad++ 8.8.3 executable, with separate disposable settings/documents and no injected code. `scripts/demo-notepad.ps1 -SkipBuild` uses its built-in GDI rendering setting and the new opt-in posted-shortcut path. Editing, saved Unicode contents, undo/redo, ownership, recovery and two actual viewers passed automated verification. Default DirectWrite document capture remains blank; plugins and Word are untested. `scripts/test-installed-notepad.ps1 -SkipBuild` repeats the controlled cases. Fixtures remain mechanism and regression tools, not substitutes for installed-application evidence.

Requirements: an interactive Windows session, x64 .NET 10 SDK for building, and .NET 10 Windows Desktop runtime for execution. Tested on Windows build 26300, SDK 10.0.302 and runtime 10.0.10. No administrator permission is needed in the tested session.

From PowerShell:

```powershell
Set-Location C:\Programming\Projects\VIAVW
.\scripts\build.ps1
.\scripts\demo.ps1 -SkipBuild
```

The demo opens a visible viewer and launches the disposable WinForms application on a private desktop. A console owned solely by the demo is detached; a shared user terminal is left alone. Workers and target windows remain off Layer 0. Closing the viewer first requests graceful application close. If a save prompt or remaining owned process prevents exit, the viewer stays available under human ownership. **Force close** explicitly terminates the owned tree and discards unsaved state.

The private viewer now forwards Ctrl/Alt shortcuts and Shift navigation using `SendQueueStateChord`. This opt-in API temporarily shares keyboard state with the owned target thread on its non-input private desktop. Ctrl+A and Shift+End work in unmodified Character Map; WPF and Qt Quick also recognize Ctrl+K and Alt+K. It does not synthesize `GetAsyncKeyState`, raw input or physical mouse state.

To inspect an unmodified application:

```powershell
.\scripts\demo.ps1 -SkipBuild -Executable C:\Windows\System32\charmap.exe
```

Use disposable applications/documents. Direct `ApplicationLayer.Dispose` remains emergency termination; `CloseGracefully` and normal viewer closure give the application an opportunity to save.

Explicit background startup and reveal of the same session:

```powershell
.\scripts\demo.ps1 -SkipBuild -Hidden
# Copy the status-file path printed above:
.\scripts\show-demo.ps1 -SessionFile C:\Programming\Projects\VIAVW\artifacts\sessions\<session-id>.json
```

Duplicate demos from this build directory on the same desktop are refused. `-AllowDuplicate` explicitly permits another instance. Existing processes are never silently attached to or terminated, and older builds without the session mutex are outside this duplicate detector. Startup errors are printed by the launcher and retained in the session status file.

Qt Quick demonstrations:

```powershell
# Child-only QT_QUICK_BACKEND=software; native PrintWindow capture
.\scripts\quick-demo.ps1 -SkipBuild -Software

# Normal GPU renderer; explicitly cooperative fixture capture
.\scripts\quick-demo.ps1 -SkipBuild

# WPF keyboard interaction and cooperative rendering; native mouse is limited
.\scripts\wpf-demo.ps1 -SkipBuild
```

These use disposable fixtures. No Profile Builder or production application is modified. Software Qt rendering has feature/style limitations; GPU/WPF cooperative capture requires application integration and is not transparent capture of arbitrary applications.

## Native GPU applications on private desktops

```powershell
# Installed, unmodified Chrome; native input and native full-content capture.
.\scripts\private-browser-demo.ps1 -SkipBuild

# Ordinary Qt Quick GPU rendering; no cooperative grab adapter.
.\scripts\quick-demo.ps1 -SkipBuild -NativeGpu

# WPF remains cooperative for pixels; enable the new native pointer path.
.\scripts\wpf-demo.ps1 -SkipBuild -QueuePointer
```

Take **Human control**, click the page/editor and type. Ctrl+A replaces the selection, and the GPU color button updates the native preview. **Return to automation** rejects further picture input while preserving the same application. Physical-user acceptance of these new demonstrations is pending; automated viewer handoff passed.

`-FullContent` selects `PW_CLIENTONLY | PW_RENDERFULLCONTENT`. It captured private Chrome, Qt Quick and hardware Direct3D windows where the original PrintWindow mode was blank. It remains opt-in; default opaque hardware WPF still produced blank pixels, including after private activation. `-QueuePointer` supplies private queue button state and focus/capture priming for clicks, wheel input and bounded drag gestures. Viewer drags are delivered when released; losing capture or changing ownership cancels a pending gesture. `-RootInput` routes custom-window input to the selected top-level HWND instead of helper child windows. No software-rendering override, Chrome developer protocol, executable modification or driver is used by the private browser demonstration.

## Hidden GPU inspection and finite private input

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -VisibilityResearch
```

Ordinary Chrome now has verified hidden inspection and finite show/input/capture/hide bursts, including two independent GPU layers. Take **Human control** and click a text field, return to automation, then **Hide app window**. **Inspect hidden app** obtains a fresh frame and restores hidden visibility. **Click and send privately** deliberately repeats that selected click and sends the toolbar text, also restoring visibility; a drag does not select a new click point. **Hide preview** suppresses its optional capture. Physical manual acceptance is pending.

`ApplicationLayer.PresentForInput` exposes bounded native text/chord/mouse bursts with explicit ownership, subtree validation and visibility restoration. The existing occlusion flag remains necessary in the tested browser configurations. Showing/focusing alone failed; the later opt-in activation path below supersedes the earlier click requirement. See [private GPU presentation research](docs/PRIVATE-GPU-PRESENTATION-20261014.md) for the original evidence.

**Send without clicking** restores tested Chrome/Edge input at the retained field, caret and selection using a targeted activation-notification pair. With preview enabled it supplies native pixels through temporary private presentation. With **Hide preview**, it sends text without requesting source show or capture; **Inspect hidden app** obtains pixels separately. Hidden Ctrl+A failed with this activation-only mechanism, so its input-only API accepts text. New physical manual acceptance remains pending.

`PresentForInput(..., notifyActivation: true)` opts into presented activation; `SendActivatedInput` is the bounded text-only path. Installed Edge is available with `private-browser-demo.ps1 -SkipBuild -VisibilityResearch -Browser edge`. [Activation research](docs/ACTIVATION-RESEARCH-20261014.md) documents two-field/Unicode tests, keyboard-only simultaneous Chrome/Edge layers, failures, latency and reusable disposable research profiles. Default behavior and the explicit-click alternative remain available.

An additional experimental hidden input path supports tested Chrome/Edge shortcuts, text, clicks and a buffered slider drag while the native window stays hidden. A bounded visibility-notification pulse briefly resumes framework rendering. Direct hidden GPU capture remains blank; inspection separately uses accepted temporary private presentation. This option adds **Replace hidden field** to the viewer; default paths remain available. See [hidden input research](docs/HIDDEN-INPUT-RESEARCH-20261014.md) for measurements, failures and pending physical acceptance.

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput
# Select the field under Human control, return to automation,
# Hide app window, Hide preview, then use Replace hidden field.
# Inspect hidden app obtains the native pixels separately.
```

Private hidden inspection now accepts an optional bounded wait. The default remains 150 ms; tested Chrome/Edge inspections worked at 30 ms, while zero wait sometimes returned stale pixels. The alternative native style-bit capture remained experimental because it offered no useful latency advantage. See [capture timing research](docs/CAPTURE-TIMING-RESEARCH-20261014.md) for fresh-pixel checks, concurrent ownership/recovery and measured CPU/rendering costs.

```powershell
.\scripts\private-browser-demo.ps1 -SkipBuild -ExperimentalHiddenInput -InspectionSettleMilliseconds 30
```

## WPF rendering research and optional startup adapter


The unchanged accepted WPF executable now works with a process-local .NET startup adapter selecting software rendering. This loads trusted code into that disposable application; it is an opt-in compatibility option, verified only for .NET 10 within this project. Native capture supplies its pixels. The ordinary opaque hardware WPF path is still blank. WPF's own layered fixture mode also supplies native private GPU pixels. See [rendering research](docs/RENDERING-RESEARCH-20261014.md) for controls, failures, variable timing and scope.

```powershell
.\scripts\wpf-hook-demo.ps1 -SkipBuild
.\scripts\wpf-demo.ps1 -SkipBuild -Software
.\scripts\wpf-demo.ps1 -SkipBuild -Layered
```

Typing, modifiers, clicks, wheel scrolling and ownership handoff passed through the actual private viewer in all three modes. New physical-user acceptance remains pending. Preview pixels can lag observed control state; refresh again when needed. The Firefox investigation is incomplete because of first-run consent and disabled WebGL in its fresh profiles.

## Ordinary WPF application demonstration

The pinned portable ILSpy 10.1 application now runs on a private desktop with unchanged release binaries and the accepted process-local software rendering adapter. Native captures show its real search results and decompiled code. Targeted keyboard shortcuts, result navigation, wheel scrolling, drag selection and simulated human/automation ownership passed through the actual viewer. Its own configuration stays inside project artifacts, update checks are disabled, and `--newinstance` avoids attaching to any existing instance.

```powershell
.\scripts\ilspy-demo.ps1 -SkipBuild
```

Take **Human control**, click the magnifying glass in ILSpy, type `ProbeWindow`, and press Down/Enter to open a result. Close the search pane, scroll the code, and drag to select text; selection appears after release. **Return to automation** makes picture input read-only. Physical-user acceptance is pending. Two early QA runs did not confirm graceful close; later instrumented and full regression runs passed. Evidence and display-off timing measurements are in [ordinary WPF research](docs/ORDINARY-WPF-20261014.md).

The package is already under `.deps/ILSpy-10.1.0.8386-x64`; its original ZIP, release digest and license are retained. No installer was run. The startup adapter is required for these captured pixels; this result does not resolve default opaque hardware WPF capture.

Two independent viewers are available with `scripts/ilspy-pair-demo.ps1 -SkipBuild`. Each owns its own source, desktop, input owner and configuration. The viewer's **Inspect hidden app** button temporarily presents a hidden source only on its private desktop, captures it and restores its hidden state. Use **Show app window** for mouse interaction. Concurrent ordinary input/recovery and native WPF software/layered inspection passed automated checks; new physical acceptance is pending. See [concurrency and inspection research](docs/CONCURRENT-INSPECTION-20261014.md) for measurements, lifecycle changes and posted-modifier limitations.

## Alternative offscreen GPU browser demonstration

```powershell
.\scripts\gpu-demo.ps1 -SkipBuild
```

This opens a second viewer backed by the installed, unmodified Chrome and a disposable local WebGL page. Chrome remains offscreen on the normal Default desktop. The viewer captures the full native browser window with Windows Graphics Capture; startup and page input use an explicit CDP adapter. This is a separate backend with weaker isolation than the private desktop. It is not a general launcher for arbitrary applications.

Use **Automation: type** and **Automation: change color**, then **Human control**. Click the page in the picture to type or select text with Ctrl+A. **Return to automation** makes the picture read-only while keeping the same browser process. **Hide preview** stops captures; **Refresh** or an interaction requests a new frame. Browser toolbar pixels are inspectable; input is routed to the disposable page. Closing the viewer terminates its disposable browser job.

The capture worker now retains its device, capture item, readback texture and encoder factory. Pools/sessions are created and closed per request. **Restart capture worker** replaces only that worker, preserving Chrome. Warm captures measured about 21 ms; browser scheduling after input raised updated-frame captures to about 41 ms in the verified local page. First capture still pays initialization cost.

The tested WebGL renderer is NVIDIA Direct3D11. No Chrome executable changes, drivers or persistent browser settings are needed. The source window remains logically visible so DWM can compose it; `SW_HIDE` is not a working GPU capture substitute. The report includes actual screenshots, failure evidence and performance observations.

Run the combined research verification:

```powershell
.\scripts\test-milestone3.ps1 -SkipBuild
```

Omit `-SkipBuild` to rebuild .NET programs and the native helper with the installed Visual C++/Windows SDK toolchain. Tests also require the project-local PySide6 setup and the installed Chrome path used in Milestone 2. No dependencies are installed by this verification script.

## Viewer operation

- **Automation: send text** sends text to the control selected in the dropdown. The initial WinForms selection is its EDIT control. Select `RICHEDIT50W` for Character Map, or the Qt top-level window for the supplied Qt fixture.
- **Run typing test** sends one character every 250 ms without capture, focus requests, or physical pointer movement. Switch to a separate foreground application and type there; return and use **Stop typing test**, then **Refresh**.
- **Refresh** takes one capture. There is no continuous capture loop after startup discovery completes.
- **Human control** suspends the automation timer and changes input ownership. Click the image, then type. The same running target receives messages through the wrapper. Simple left clicks/text and posted navigation, Tab, Enter, Escape, Home, End, and Delete are available; their effect depends on the framework.
- **Return to automation** releases human ownership. Choose the intended text control before sending more text; clicking a button makes that button the current input target.
- **Hide preview** removes the picture and stops taking captures. The target keeps running, with its window visible only on its private desktop. **Show preview** takes a fresh capture.
- **Hide app window** additionally calls `SW_HIDE` on the private target. Its timer kept running in our fixtures, but the WinForms capture became black. Prefer hiding the preview for background operation.
- **Request attention** sets a simple attention reason. It does not change ownership or send external notifications.

The image is drawn at native client pixel size. Large windows can extend beyond the viewer. The human picture widget forwards simple clicks/text/navigation and private queue-state modifier chords. Opt-in `-QueuePointer` forwards clicks and wheel messages with scoped private queue state. Human drag/right-click forwarding, scaling/scrolling of the picture, IME and asynchronous device-state emulation remain outside the implemented viewer behavior.

Startup, input, enumeration, capture, and graceful-close waits execute off the viewer's UI thread. A per-viewer semaphore serializes commands; each layer retains its own ownership lock and desktop-local worker. Human input refresh is debounced, and no capture loop runs while idle or while preview is hidden. Posted messages are queued, not application acknowledgements: wait for observed state before changing ownership after a posted/adapter action. Core automation typing uses synchronous character delivery.

## Reproduce the experiments

```powershell
# Core native isolation/input/capture/lifecycle checks
.\scripts\test.ps1 -SkipBuild

# Native checks + real viewer handoff + Character Map + available Qt/Chromium
.\scripts\test-all.ps1 -SkipBuild

# Milestone 2 framework, lifecycle, launcher, viewer, and performance experiments
.\scripts\test-milestone2.ps1

# Optional project-local Qt runtime; no system Python package installation
.\scripts\setup-qt.ps1
.\scripts\test-all.ps1 -SkipBuild -Python C:\Python314\python.exe
```

The Qt setup downloads two pinned wheels into `.deps`; it does not install into the system interpreter. The extended suite skips Qt or Chromium if their prerequisites are absent and prints that fact. Supply `-Chrome` with another installed Chromium executable if necessary. These scripts do not change execution policy, install drivers, or alter Windows settings.

Milestone 2's optional native helpers require an existing Visual C++ toolchain and Windows SDK with C++/WinRT headers. `scripts/build-native.ps1` locates them without modifying system configuration. The M2 runner uses this machine's `C:\Python314\python.exe` and installed Chrome paths. `-SkipVisibleLauncherTest` omits the brief ordinary-desktop viewer/capture-control checks; `-SkipBenchmark` omits the 30-second idle plus activity sample. Framework incompatibilities are recorded in reports and are not claimed as supported behaviors merely because an experiment finishes.

Outputs go into timestamped `artifacts` directories. A Chromium experiment requires a fresh profile path on each run. The test runner creates one automatically. Binaries, captures, logs, profiles, temporary telemetry, and dependencies are ignored by `.gitignore` for any future repository.

For a visual-only bounded compatibility probe:

```powershell
& .\src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe --probe-app C:\Windows\System32\charmap.exe
```

This enumerates and captures owned private-desktop windows, records their properties, and closes the layer. If an application delegates startup to an existing instance or a broker outside the job, no fallback input is sent to that instance.

## Architecture

```text
Layer 0 / existing input desktop
  LayerManager + optional viewer
    ApplicationLayer A       ApplicationLayer B
      private desktop A        private desktop B
      kill-on-close job A      kill-on-close job B
        RPC worker A             RPC worker B
        target + children        target + children
```

`LayerManager` creates independent `ApplicationLayer` instances. Each creates an ordinary Windows desktop object in the current `WinSta0` station. A worker is launched suspended with `STARTUPINFO.lpDesktop`, assigned to its layer's job, and then resumed. The worker launches the application normally with inherited user identity, environment, filesystem, registry, networking, and installed runtime. The desktop is a UI container, not an OS copy or a resource/security sandbox. Filesystem access is explicitly verified; registry/network/GPU resource functionality is not separately benchmarked.

A same-user, randomly named pipe with a random handshake token connects manager and worker. Native operations execute in a worker already attached to the target desktop because window messages are desktop-scoped. Every operation carries a fresh list of the job's processes; target HWNDs must belong to that job and that desktop. Enumeration includes child controls and owned application processes.

`PrintWindow(PW_CLIENTONLY)` renders into a GDI bitmap; the worker returns a PNG on demand. Optional backends experimentally call WGC or request an explicitly enabled fixture capture. Native GPU PrintWindow results were blank; WGC failed on the tested private desktops. Qt Quick's child-only software backend produced useful native captures. See the matrix before selecting a backend.

The new full-content PrintWindow mode supplies real GPU pixels on the tested private Chrome/Qt Quick/Direct3D windows. WGC still fails on these private sources. Capture success depends on the mechanism and application; the historical Milestone 2/3 results remain preserved.

A stalled worker/capture has a five-second RPC deadline. Emergency cleanup closes only that layer's job and loses its unsaved state. Normal close instead posts `WM_CLOSE`, preserves a pending save prompt, and suspends automation. An explicit `RestartWorker` replaces the owned worker/pipe while retaining a healthy target process, HWNDs, text, and ownership; no input is replayed. It cannot repair an application hung inside its own print handler. The parent holds the only job handle, so abrupt parent death still triggers tree cleanup.

`ILayerSurface` exposes common keyboard, mouse, capture, ownership, and attention operations. `NativeWindowSurface` translates client coordinates to owned child windows. `ChromiumLayer` implements the same surface operations using CDP and a disposable headless browser profile. Its events and captures are browser-level; they do not extend native Windows input compatibility.

`OffscreenBrowserSession` is an alternative surface using unmodified headful Chrome on Default. Its native WGC window pixels and CDP page input have separate semantics. Background target creation, offscreen placement, owned HWND validation and a kill-on-close job bound this controlled experiment; they do not establish private-desktop isolation for arbitrary applications.

Input requests and ownership transitions share a lock. A handoff waits for an in-flight wrapper request to complete; subsequent requests from the previous owner are rejected. Posted input and asynchronous accessibility providers can still have pending target work after a request returns; clients must confirm the intended state before handing over. Preview keeps ownership and serializes that layer's operations without blocking the viewer UI.

## Implementation files

| File | Responsibility |
|---|---|
| `src/LayerHost/LayerManager.cs` | Desktop/job lifecycle, authenticated worker connection, input ownership, RPC deadlines |
| `src/LayerHost/Worker.cs` | Scoped window discovery, message input, hit testing, GDI/PNG capture |
| `src/LayerHost/Native.cs` | Documented Win32 interop and process argument quoting |
| `src/LayerHost/LayerSurface.cs` | Common surface interface and native window facade |
| `src/LayerHost/ChromiumLayer.cs` | CDP page adapter and browser lifecycle |
| `src/LayerHost/OffscreenBrowserSession.cs` | Experimental offscreen Chrome surface, native GPU capture and ownership |
| `src/LayerHost/GpuBrowserForm.cs` | On-demand GPU preview and human/automation handoff |
| `src/LayerHost/ReusableCaptureClient.cs` / `experiments/native/WgcWorker.cpp` | Reusable graphics resources, bounded pipe capture, helper-only recovery |
| `src/LayerHost/PrivateGpuVerification.cs` / `PrivateBrowserViewerVerification.cs` | Native private GPU pixels and ordinary Chrome viewer input |
| `src/LayerHost/PrivateBrowserVisibilityVerification.cs` / `ConcurrentBrowserBurstVerification.cs` | Hidden GPU inspection, native input bursts, passive frame checks and simultaneous Chrome layers |
| `src/LayerHost/PresentedInputValidation.cs` | Bounded text/chord/mouse transaction validation |
| `src/LayerHost/WindowActivationResearch.cs` / `BrowserActivationVerification.cs` | Guarded activation comparisons, retained-selection input and hidden text verification |
| `src/LayerHost/ResearchBrowserProfile.cs` | Leased, reusable project-owned disposable research profiles |
| `src/LayerHost/MouseStateVerification.cs` | Scoped private pointer state, drag and scrolling experiments |
| `src/LayerHost/CaptureReuseVerification.cs` / `PrivateGpuPerformance.cs` | Active capture costs, freshness, resize, recovery and concurrent GPU layers |
| `src/WpfRenderHook/StartupHook.cs` / `src/LayerHost/WpfHookEnvironment.cs` | Explicit process-local software compatibility option for disposable .NET 10 WPF |
| `src/LayerHost/WpfCaptureResearch.cs` / `WindowCaptureResearch.cs` | Private WPF capture/style/parent comparisons, with source restoration |
| `src/LayerHost/WpfNativeViewerVerification.cs` / `WpfNativePerformance.cs` | Actual native WPF viewer handoff, changed text pixels and activity measurements |
| `src/LayerHost/IlSpyVerification.cs` / `IlSpyViewerVerification.cs` / `IlSpyLauncherVerification.cs` | Ordinary unchanged WPF application, private viewer input and actual script verification |
| `src/LayerHost/AccessibleObservation.cs` / `DisplayPowerObserver.cs` | Bounded read-only owned UI evidence and passive session display diagnostics |
| `src/LayerHost/ConcurrentIlSpyVerification.cs` / `IlSpyVisibilityVerification.cs` | Parallel ordinary input/recovery, observer cost and hidden presentation experiments |
| `src/LayerHost/ProcessCensus.cs` / `WorkerInfrastructureVerification.cs` | Read-only job-member identity and application helpers born during worker restart |
| `scripts/ilspy-demo.ps1` | Isolated ordinary WPF demonstration using the opt-in software adapter |
| `scripts/ilspy-pair-demo.ps1` | Two independent visible viewers with a shared session manifest |
| `src/LayerHost/FirefoxNativeVerification.cs` / `PassivePageServer.cs` | Incomplete ordinary Firefox research with passive loopback observation |
| `src/LayerHost/QueueInputVerification.cs` / `Milestone3Verification.cs` | Modifier compatibility, independent states and activity measurements |
| `src/LayerHost/DemoForm.cs` | Preview, attention, and minimal human-control viewer |
| `src/LayerHost/Verification.cs` | Repeatable native isolation, resource, and failure tests |
| `src/LayerHost/AdapterVerification.cs` | Character Map, Qt, browser, and viewer tests |
| `src/LayerHost/CompatibilityVerification.cs` | Framework interaction and pixel-correctness experiments |
| `src/LayerHost/PerformanceVerification.cs` | Per-process CPU/memory and owned GPU-engine measurements |
| `src/LayerHost/LauncherVerification.cs` | Real launcher, duplicate, startup failure, responsiveness, and save tests |
| `src/LayerHost/RecoveryVerification.cs` | Explicit worker restart with target state preservation |
| `src/LayerHost/Win32Expansion.cs` | Unmodified Character Map editing/navigation and winver dialog interaction |
| `src/LayerProbe/Program.cs` | Standalone disposable WinForms fixture; passive event counters |
| `experiments/qt_probe.py` | Ordinary Qt widgets; passive event observation |
| `experiments/quick_probe.py` / `.qml` | Ordinary Qt Quick controls; optional cooperative grab adapter |
| `src/WpfProbe/Program.cs` | Standard/custom WPF controls; passive observation and optional render adapter |
| `experiments/native/` | Hardware Direct3D fixture and bounded WGC capture helper |
| `experiments/browser-probe.html` | Disposable local browser page; normal DOM event handlers |

## Native API example

```csharp
using LayerHost;

using var manager = new LayerManager();
var layer = manager.CreateLayer();
layer.LaunchApplication(@"C:\path\DisposableApp.exe");
// Poll GetWindows until the application's window/control is ready.
var windows = layer.GetWindows();
var root = windows.First(w => w.Parent == 0 && w.Visible);
var editor = windows.First(w => w.ClassName.Contains("EDIT"));
ILayerSurface surface = new NativeWindowSurface(layer, root.Handle, editor.Handle);
surface.SendKeyboardInput("Hello");
layer.SendQueueStateChord(editor.Handle, [0x11, 0x41]); // Ctrl+A on a compatible private control.
File.WriteAllBytes("preview.png", surface.CaptureVisualOutput().Png);
surface.RequestAttention("Inspect this configuration.");
surface.TakeHumanControl();
surface.SendKeyboardInput(" human edit", ControlOwner.Human);
surface.ReturnToAutomation();
// Disposing the manager closes all its layers and application trees.
```

For direct control, `ApplicationLayer` exposes sent/posted keys/text, bounded chord experiments, mouse message variants, `HitTest`, owned focus inspection, capture-backend selection, graceful close, and explicit worker restart. `SelectControlText` and `NotifyScrollControl` are labeled native control adapters, not keyboard/wheel emulation. `InvokeAccessibleAction` is experimental and did not produce a timely reliable WPF action in our tests. HWNDs can become stale; rediscover them. The browser adapter's `Inspect` evaluates JavaScript and is not a read-only security boundary.

## Findings and next work

The original measurements are preserved in [docs/MILESTONE-1.md](docs/MILESTONE-1.md). Manual acceptance is in [docs/MANUAL-ACCEPTANCE.md](docs/MANUAL-ACCEPTANCE.md). Framework baseline findings are in [docs/MILESTONE-2.md](docs/MILESTONE-2.md); accepted offscreen GPU and modifier results are in [docs/MILESTONE-3.md](docs/MILESTONE-3.md). Current private GPU capture, reusable resources and pointer results are in [continued development](docs/DEVELOPMENT-20261014.md). Mechanism research is in [docs/RESEARCH.md](docs/RESEARCH.md).

Device reuse and private pointer priming now have working implementations and measurements. The strongest next experiments are transparent WPF rendering, broader ordinary GPU application coverage and longer mixed-input/recovery runs. Complete virtual-device input and universal graphics compatibility remain unresolved.

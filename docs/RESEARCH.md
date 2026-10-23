# Mechanisms investigated

This document begins with the Milestone 1 mechanism research. Implemented Milestone 2 experiments supersede its earlier "not implemented/tested" status where noted in [MILESTONE-2.md](MILESTONE-2.md).

Research date: October 13, 2026, America/New_York. The prototype results are local execution observations; the references below explain the underlying documented APIs. No universal compatibility conclusion follows from those references or from a single successful application. [Ordinary WPF research](ORDINARY-WPF-20261014.md) adds unchanged ILSpy compatibility, viewer drag selection, measured CLR startup deadlines and passive display-off capture evidence. [Concurrent inspection research](CONCURRENT-INSPECTION-20261014.md) adds simultaneous ordinary layers, scoped observation cost, temporary private presentation, process-identity accounting and a posted-modifier reliability limit.

The later [private GPU presentation research](PRIVATE-GPU-PRESENTATION-20261014.md) demonstrates finite native input/capture bursts in two hidden Chrome layers, actual rendering suspension, empirical settle costs and failure of focus-only restoration. It preserves the private desktop foundation and the separate reusable offscreen backend.

[Activation research](ACTIVATION-RESEARCH-20261014.md) subsequently restores Chrome/Edge input through an opt-in inactive/active notification pair. Text can remain hidden, while shortcuts still use temporary private presentation. Native Windows focus handles and framework readiness were experimentally different; retained-selection, Unicode, ownership, recovery and concurrent keyboard-only GPU output have repeatable evidence.

The later [hidden input research](HIDDEN-INPUT-RESEARCH-20261014.md) adds bounded visibility notifications: Chrome/Edge shortcuts and pointer gestures work without changing native visibility, while framework rendering briefly resumes. Direct hidden GPU capture remains blank. Character Map, native EDIT, layered WPF and Qt Quick passed hidden selection/replacement through existing messages alone. The new path is opt-in; the earlier activation-only shortcut limitation remains accurate for that mechanism.

The subsequent [capture timing research](CAPTURE-TIMING-RESEARCH-20261014.md) failed to recover fully hidden Chrome GPU pixels through print/DC/child routes. Temporary native visibility plus framework notifications worked, but ordinary private presentation was equally fast. A bounded optional 30 ms inspection wait passed native pixel, concurrent layer and viewer checks; zero wait produced stale output. Existing 150 ms defaults remain available.

## Private desktop objects

Windows desktops contain windows, menus, hooks, and a logical display surface. Only one desktop in the interactive window station is the physical input desktop. Window messages are scoped to a desktop. This makes an application/worker pair on a private desktop a promising first UI container; it does not create a second physical input destination. [Microsoft: Desktops](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops).

The launcher uses the `lpDesktop` field of process startup configuration. It does not switch the input desktop or move existing windows between desktops. Workers are separate processes so they start on the appropriate desktop and can be terminated if a capture blocks. A thread with existing windows/hooks cannot freely change its desktop association, which also argues against switching the viewer thread back and forth. [Microsoft: SetThreadDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddesktop).

The number of desktops is limited by desktop heap resources. This prototype tested two native application layers concurrently, plus additional bounded failure/QA layers. It did not test large-scale layer counts or change heap configuration. [Microsoft: Window station and desktop creation](https://learn.microsoft.com/en-us/windows/win32/winstation/window-station-and-desktop-creation).

## Targeted window messages versus device input

The native implementation sends bounded synchronous messages from the same desktop: characters, selected virtual key down/up pairs, pointer movement messages, and left-button down/up. It never calls `WM_SETTEXT`, `BM_CLICK`, UI Automation, `SendInput`, `SetCursorPos`, `SetForegroundWindow`, `AttachThreadInput`, or `SwitchDesktop`.

The tests observe normal WinForms KeyPress/KeyDown/Button.Click and Qt key/mouse/click handlers, plus changes in an unmodified Character Map rich-edit control. These are genuine graphical control paths, but synthetic window messages do not supply keyboard device state, scan-code stream behavior, raw input, DirectInput, or full accelerator/IME semantics. Those deeper paths were not exercised.

`SendInput` adds events to the keyboard/mouse input stream; it has no layer/HWND argument. The documentation also describes interactions with existing keyboard state and UIPI restrictions. Using it as an arbitrary target-specific layer input mechanism is not justified by this interface. No experimental `SendInput` call was made because unrelated foreground windows must not receive our input. [Microsoft: SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [Microsoft: Keyboard input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-keyboard-input).

The WinRT InputInjector APIs require the restricted `inputInjectionBrokered` capability and inject input to the system. That does not establish a supported, independently addressed desktop keyboard/mouse for this ordinary unpackaged host. This route was researched, not enabled or executed. [Microsoft: InputInjector](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector).

## Capture and rendering

`PrintWindow` asks the owning application to render to the supplied device context and is synchronous. A successful return does not prove correct pixels for every rendering framework. The observed visible private-desktop WinForms, Character Map, and raster Qt captures contain the current application text/counter. `SW_HIDE` produced a black WinForms capture despite continued timer execution. This is an observed limitation of this test, not a claim that all hidden windows fail. [Microsoft: PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow).

Windows Graphics Capture offers a window-targeted capture item through `IGraphicsCaptureItemInterop::CreateForWindow` on Windows 10 1903 and later. It is a candidate for a GPU-backed capture adapter. This milestone did not implement it, and no claim is made about its behavior on non-input desktops, minimized windows, protected surfaces, or particular GPU applications. [Microsoft: CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow).

DWM thumbnails were considered as a presentation mechanism. They do not by themselves create an independently routable input environment; no DWM thumbnail experiment was run. [Microsoft: DWM thumbnail overview](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw).

## Accessibility and application adapters

UI Automation value/action patterns can be useful for target-specific control. Changing a value through its accessibility provider would be separate evidence from delivery of key/mouse events, so it is not the input mechanism used to prove this baseline. [Microsoft: IUIAutomationValuePattern.SetValue](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationvaluepattern-setvalue).

Qt documentation distinguishes native windows from widgets without their own platform window. The disposable Qt fixture used normal widgets without forcing native children; its top-level HWND routed the messages through Qt's normal focused-widget and mouse handling in this experiment. This is evidence for Qt Widgets 6.10.3 raster rendering, not Qt Quick/OpenGL or arbitrary Qt applications. [Qt: QWidget](https://doc.qt.io/qt-6/qwidget.html).

Chromium provides page-addressed CDP input and screenshot operations. The adapter sends `Input.dispatchKeyEvent` and `Input.dispatchMouseEvent`, observes trusted DOM key/input/mouse events, and captures through `Page.captureScreenshot`. It does not modify input values with JavaScript. Its JavaScript evaluations are diagnostic reads of the fixture state. [Chromium: CDP Input](https://chromedevtools.github.io/devtools-protocol/tot/Input/), [Chromium: CDP Page](https://chromedevtools.github.io/devtools-protocol/tot/Page/).

The browser runs headless with a new project-local profile and a loopback debug endpoint using an ephemeral port. This deliberately avoids the user's existing profile/browser session. CDP versions can change, and the protocol is a privileged debugging interface, so the endpoint is kept local and exists only for the disposable browser lifetime. [Chrome: Headless debugging](https://developer.chrome.com/docs/automation-and-testing/debug-headless), [Chromium: Protocol scope](https://chromedevtools.github.io/devtools-protocol/).

## Process ownership and failure containment

Each worker is assigned to its job before it resumes. Its normally created descendants inherit job membership. The job's kill-on-close flag terminates its members when the final job handle closes. The manager holds that sole handle; the worker does not inherit/open a job handle. Default child inheritance, abrupt host kill, and deliberate capture hang were executed and verified. Brokered applications and processes created through external services can escape ordinary parent/child ownership; those are outside the demonstrated boundary. [Microsoft: Job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

No kernel driver, global input hook, process injection, API interception, elevation bypass, secure desktop access, display reconfiguration, alternate OS, or VM was introduced. Deeper interception is unnecessary for the current positive results and should be considered only after a specific reproducible application failure justifies it.

## Milestone 2 references and observed boundaries

Qt documents the `QT_QUICK_BACKEND` environment selection and a software scene-graph adaptation. VIAVW supplies the setting only in the child's process environment. The tested QML app then reports GraphicsApi.Software and produces usable PrintWindow output. Software adaptation omits some graphics features; it is not a promise of equivalent rendering for every app. [Qt scene graph adaptations](https://doc.qt.io/qt-6/qtquick-visualcanvas-adaptations.html), [Qt software adaptation limitations](https://doc.qt.io/qt-6/qtquick-visualcanvas-adaptations-software.html).

`QQuickWindow.grabWindow` is a GUI-thread application API and can involve expensive readback. The enabled fixture calls it on its own GUI thread only when explicitly requested. The WPF fixture renders its own visual through RenderTargetBitmap. These are cooperative application adapters; neither gives the external host transparent access to arbitrary application rendering objects. [Qt QQuickWindow](https://doc.qt.io/qt-6/qquickwindow.html), [Microsoft RenderTargetBitmap.Render](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.rendertargetbitmap.render).

The WGC helper uses CreateFreeThreaded, whose frame notifications run on an internal worker without a DispatcherQueue dependency. It successfully captures a nonactivating ordinary-desktop GPU control window. Private-window capture items reported zero size, and valid explicit buffer sizes still failed in tested private/default helper contexts. These are measured results, not a documented universal prohibition. [Microsoft CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded), [Microsoft screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture).

System-key messages include scan/context/transition metadata, but synthetically posting them did not establish target modifier state in the tested frameworks. WPF tracing also observed system-menu behavior, so bounded experiments cancel an owned menu before proceeding. No keyboard-state or foreground takeover fallback was used. [Microsoft WM_SYSKEYDOWN](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-syskeydown).

GPU measurements use PDH wildcard GPU-engine counters, two collections, and formatted counter arrays filtered to owned PIDs. Per-engine percentages are retained separately; they are not added into a whole-device claim. [Microsoft PdhGetFormattedCounterArrayW](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw).

## Milestone 3 updates

The earlier modifier result is superseded for the new opt-in queue-state path: AttachThreadInput and SetKeyboardState, confined to an owned non-input private desktop, enabled normal Ctrl/Shift/Alt handlers in the tested controls. It still does not supply asynchronous global device state. Native WGC obtained changing GPU pixels from offscreen-visible Default windows, including unmodified headful Chrome after child-instance occlusion throttling was disabled. That alternative uses weaker presentation isolation and explicit CDP startup/input; native private-desktop GPU capture remains unresolved. See [Milestone 3](MILESTONE-3.md) for actual pixels, failure evidence, measured setup cost, demo instructions and cleanup verification.

## Continued private GPU development

The private GPU capture limitation above is superseded for the three tested application paths: `PrintWindow` with `PW_CLIENTONLY | PW_RENDERFULLCONTENT` captures changing hardware-rendered Chrome, Qt Quick and D3D11 pixels on private desktops. Native Chrome input also works through scoped private queue focus/capture and root-window routing, without CDP in the production demonstration. WPF native pixels remain blank, and WGC still fails to acquire these private windows. A reusable WGC worker improves the alternative offscreen path while closing its capture sessions between requests. See [continued development](DEVELOPMENT-20261014.md) for measured results, preserved failures, compatibility limits and reproduction instructions.

## WPF rendering and process hosting update

Opaque default WPF remains blank, but software preference and WPF's own per-pixel layered mode supply native private pixels. The layered fixture also produces measured owned GPU-engine activity. A documented, process-scoped .NET startup hook selects software rendering in the unchanged accepted WPF executable, with positive/mismatched controls and actual viewer handoff. This is explicitly an in-process compatibility adapter. External layered styles and wrapper-parent experiments did not recover default hardware pixels. Firefox captured its first-run dialog but still needs manual consent and WebGL diagnosis. See [rendering research](RENDERING-RESEARCH-20261014.md) for variable latency, actual frames, boundaries, retained failures and demonstrations.

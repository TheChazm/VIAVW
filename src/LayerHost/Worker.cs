using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LayerHost;

internal static class Worker
{
    static string? applicationDirectory;
    static OwnedTextEventObserver? textEvents;
    static OwnedWindowEvents? windowEvents;
    static ReusableCaptureClient? reusableCapture;
    static readonly SharedPreviewWriter sharedPreview = new();
    static readonly PrivateInputSession inputSession=new();
    static readonly PrivatePreviewThread asynchronousPreview=new();
    internal static int RunMta(string pipeName,string token)
    {
        int result=1;Exception? failure=null;
        var thread=new Thread(()=>{try{result=Run(pipeName,token);}catch(Exception error){failure=error;}}){Name="VIAVW private MTA worker"};
        thread.SetApartmentState(ApartmentState.MTA);thread.Start();thread.Join();
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
    internal static int Run(string pipeName, string token, bool inspectionOnly = false, bool stallInspection = false)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        pipe.Connect(8000);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(new { token, desktop = Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())), pid = Environment.ProcessId }));
        string? line;
        try
        {
            while ((line = reader.ReadLine()) != null)
            {
                Reply reply;
                try
                {
                    var r = JsonSerializer.Deserialize<Request>(line)!;
                    if (inspectionOnly)
                    {
                        if (r.Operation is not ("accessible-observation" or "accessible-menu-observation" or "scoped-accessible-point")) throw new InvalidOperationException("The passive inspection helper only accepts read-only observations.");
                        Validate(r, (nint)r.Window);
                        if (stallInspection) Thread.Sleep(Timeout.Infinite); // Explicit disposable fault-verification worker only.
                        var result = r.Operation=="scoped-accessible-point"?ScopedAccessiblePoint.Read((nint)r.Window,r.AllowedProcessIds??[],r.X,r.Y):AccessibleObservation.Read((nint)r.Window, r.AllowedProcessIds ?? [], r.Text, menuItems:r.Operation=="accessible-menu-observation",allVisibleMatches:r.Key==1,
                            progress: partial => writer.WriteLine(JsonSerializer.Serialize(Reply.Ok(new { status = "partial", result = partial }))));
                        reply = Reply.Ok(new { status = "complete", result });
                    }
                    else reply = Reply.Ok(Execute(r));
                }
                catch (Exception e) { reply = Reply.Fail(e); }
                writer.WriteLine(JsonSerializer.Serialize(reply));
            }
        }
        finally { try{windowEvents?.Dispose();}finally{try{textEvents?.Dispose();}finally{try{inputSession.Dispose();}finally{asynchronousPreview.Dispose();sharedPreview.Dispose(); reusableCapture?.Dispose();}}} }
        return 0;
    }

    static void Validate(Request r, nint hwnd)
    {
        if (!Native.IsWindow(hwnd)) throw new InvalidOperationException("Window no longer exists.");
        uint thread = Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId || !(r.AllowedProcessIds ?? []).Contains((int)pid))
            throw new InvalidOperationException("Window is outside this layer's job.");
        var current = Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId()));
        if (Native.ObjectName(Native.GetThreadDesktop(thread)) != current)
            throw new InvalidOperationException("Window is outside this layer's desktop.");
    }

    static object? Execute(Request r)
    {
        nint hwnd = (nint)r.Window;
        if (r.Operation == "configure") { applicationDirectory = r.Directory; return true; }
        if(r.Operation=="poll-preview")return asynchronousPreview.Poll(r.PreviewTicket);
        if(r.Operation=="persistent-private-input"){inputSession.Enable(r.LowLatencyInput,r.PreciseMouseWaits);return inputSession.State();}
        if(r.Operation=="private-input-state")return inputSession.State();
        if(r.Operation=="research-pacing-waits")return PacingWaitResearch.Measure();
        if (r.Operation == "launch")
        {
            var info = Native.LaunchSuspended(r.Executable!, r.Arguments ?? [], null, r.Directory, r.EnvironmentOverrides);
            applicationDirectory = r.Directory;
            try
            {
                if (Native.ResumeThread(info.hThread) == uint.MaxValue) { Native.TerminateProcess(info.hProcess, 1); throw new InvalidOperationException("Resume target failed."); }
                return (int)info.dwProcessId;
            }
            finally { Native.CloseHandle(info.hThread); Native.CloseHandle(info.hProcess); }
        }
        if (r.Operation == "windows") return Windows(r);
        if (r.Operation == "presentation-windows") return Windows(r, childText: false);
        Validate(r, hwnd);
        if(r.FollowPrivateFocus)
        {
            WindowCaptureResearch.PrivateOnly();
            if(r.Operation is not ("text" or "key" or "queue-chord"))throw new ArgumentException("Private focus following is only for serialized keyboard input.");
            uint focusThread=Native.GetWindowThreadProcessId(hwnd,out _);
            var focusInfo=new Native.GUITHREADINFO{cbSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>()};
            Native.Check(Native.GetGUIThreadInfo(focusThread,ref focusInfo),"Resolve current private keyboard focus");
            if(focusInfo.hwndFocus!=0)
            {
                Validate(r,focusInfo.hwndFocus);
                nint selectedRoot=Native.GetAncestor(hwnd,2),candidate=Native.GetAncestor(focusInfo.hwndFocus,2);
                var seen=new HashSet<nint>();bool related=false;
                for(int depth=0;candidate!=0&&depth<64&&seen.Add(candidate);depth++,candidate=Native.GetWindow(candidate,4))
                    if(candidate==selectedRoot){related=true;break;}
                if(related)hwnd=focusInfo.hwndFocus;
            }
        }
        if (r.Operation is "text" or "key" or "chord" or "queue-chord" or "queue-mouse" or "mouse" or "click" or "invoke" or "accessible-point-input" or "seed-private-modifiers" or "research-accessible-value" or "presented-input" or "activated-input" or "notification-input")
        {ValidateInput(hwnd);if(inputSession.Enabled)inputSession.Ensure(hwnd);}
        switch (r.Operation)
        {
            case "research-stall-after-text":
                WindowCaptureResearch.PrivateOnly();Execute(r with {Operation="text"});Thread.Sleep(Timeout.Infinite);return null!;
            case "prime-pointer":
                WindowCaptureResearch.PrivateOnly();if(r.Key==1)Thread.Sleep(Timeout.Infinite);
                return AccessibleObservation.PointerGeometry(hwnd,r.AllowedProcessIds??[],r.Steps);
            case "research-start-window-events":
                if(windowEvents!=null)throw new InvalidOperationException("One exact-process event observer at a time.");windowEvents=new(hwnd);return windowEvents.Snapshot();
            case "research-read-window-events":return windowEvents?.Snapshot()??throw new InvalidOperationException("No owned window event observer.");
            case "research-stop-window-events":windowEvents?.Dispose();var windowEventState=windowEvents?.Snapshot();windowEvents=null;return windowEventState;
            case "research-window-event-reclamation":
                WindowCaptureResearch.PrivateOnly();if(windowEvents!=null)throw new InvalidOperationException("Stop the diagnostic observer first.");
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                using(var diagnosticProcess=Process.GetCurrentProcess())return new{handles=diagnosticProcess.HandleCount,note="Explicit research-only managed reclamation, never normal operation."};
            case "input-subtree-windows": return Windows(r,rootOnly:true);
            case "research-start-text-events":
                WindowCaptureResearch.PrivateOnly();if(textEvents!=null)throw new InvalidOperationException("One owned editor observer at a time");textEvents=new(hwnd,r.AllowedProcessIds??[]);return textEvents.Snapshot();
            case "research-read-text-events": WindowCaptureResearch.PrivateOnly();return textEvents?.Snapshot()??throw new InvalidOperationException("No editor event observer");
            case "research-stop-text-events": WindowCaptureResearch.PrivateOnly();textEvents?.Dispose();var eventState=textEvents?.Snapshot();textEvents=null;return eventState;
            case "research-system-menu":
                WindowCaptureResearch.PrivateOnly(); ValidateInput(hwnd); Native.Check(Native.PostMessageW(hwnd, 0x112, 0xF100, 0x20), "Post owned SC_KEYMENU"); return true;
            case "research-menu-character":
                WindowCaptureResearch.PrivateOnly(); ValidateInput(hwnd);
                if (r.Key is < 'a' or > 'z') throw new ArgumentException("Use one lowercase menu mnemonic.");
                Deliver(hwnd, 0x106, (nuint)r.Key, 0x20000001, r.Posted); return true; // WM_SYSCHAR, Alt context.
            case "menu-observation": return OwnedMenuObservation.Read(hwnd);
            case "research-resize-window":
                WindowCaptureResearch.PrivateOnly();
                if (Native.GetAncestor(hwnd, 2) != hwnd || r.X is < 320 or > 1800 || r.Y is < 300 or > 1100) throw new ArgumentException("Bound owned top-level research resize to 320–1800 by 300–1100.");
                Native.Check(Native.SetWindowPos(hwnd, 0, 0, 0, r.X, r.Y, 2 | 4 | 0x10), "Resize owned private research window"); return true;
            case "research-dc-costs": return WindowCaptureResearch.MeasurePrivateDcCosts(hwnd);
            case "research-window-response": Native.Message(hwnd, 0, 0, 0); return true;
            case "accessible-observation": return AccessibleObservation.Read(hwnd, r.AllowedProcessIds ?? [], r.Text,allVisibleMatches:r.Key==1);
            case "accessible-menu-observation": return AccessibleObservation.Read(hwnd,r.AllowedProcessIds??[],menuItems:true);
            case "text-hit": WindowCaptureResearch.PrivateOnly(); return AccessibleObservation.TextContainsPoints(hwnd, r.AllowedProcessIds ?? [], r.Steps);
            case "text-geometry": WindowCaptureResearch.PrivateOnly(); return AccessibleObservation.TextGeometry(hwnd, r.AllowedProcessIds ?? []);
            case "pointer-geometry":
                WindowCaptureResearch.PrivateOnly();nint scope=(nint)r.TargetWindow;
                if(scope!=0)
                {
                    Validate(r,scope);
                    if(Native.GetAncestor(scope,2)!=hwnd||!Native.GetWindowRect(scope,out var scopeBounds))scope=0;
                    else foreach(var step in r.Steps??[])
                    {
                        var scopedPoint=new Native.POINT(step.X,step.Y);Native.ClientToScreen(hwnd,ref scopedPoint);
                        if(scopedPoint.X<scopeBounds.Left||scopedPoint.X>=scopeBounds.Right||scopedPoint.Y<scopeBounds.Top||scopedPoint.Y>=scopeBounds.Bottom){scope=0;break;}
                    }
                }
                return AccessibleObservation.PointerGeometry(hwnd,r.AllowedProcessIds??[],r.Steps,r.Text,scope);
            case "accessible-point-input": return AccessiblePointInput.Act(hwnd,r.AllowedProcessIds??[],r.X,r.Y,focusBeforeAction:!inputSession.LowLatency);
            case "seed-private-modifiers": return inputSession.Seed(hwnd,r.Keys??[]);
            case "research-accessible-value":
                if ((r.Text?.Length ?? 0) > 4096) throw new ArgumentException("Bound accessible research value.");
                var valueElement = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (!(r.AllowedProcessIds ?? []).Contains(valueElement.Current.ProcessId) || !valueElement.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var valueObject)) throw new InvalidOperationException("Owned control does not expose a Value pattern.");
                var valueAdapter = (System.Windows.Automation.ValuePattern)valueObject;
                if (valueAdapter.Current.IsReadOnly) throw new InvalidOperationException("Owned accessible control is read-only.");
                valueAdapter.SetValue(r.Text ?? ""); return true;
            case "text":
                // Genuine control message handling; no WM_SETTEXT, UIA, or data model writes.
                string characters=r.Text??"";
                for(int index=0;index<characters.Length;index++)
                {
                    char c=characters[index];
                    if(r.TextControlKeys&&c is '\r' or '\n')
                    {
                        if(c=='\r'&&index+1<characters.Length&&characters[index+1]=='\n')index++;
                        Key(hwnd,13,true,r.Posted);Key(hwnd,13,false,r.Posted);
                    }
                    else if(r.TextControlKeys&&c=='\t'){Key(hwnd,9,true,r.Posted);Key(hwnd,9,false,r.Posted);}
                    else Deliver(hwnd, Native.WM_CHAR, c, 1, r.Posted);
                }
                return true;
            case "key":
                Key(hwnd, r.Key, true, r.Posted); Key(hwnd, r.Key, false, r.Posted);
                return true;
            case "chord":
                foreach (int key in r.Keys ?? []) Key(hwnd, key, true, r.Posted, (r.Keys ?? []).Contains(0x12));
                foreach (int key in (r.Keys ?? []).Reverse()) Key(hwnd, key, false, r.Posted, (r.Keys ?? []).Contains(0x12));
                return true;
            case "queue-chord": return QueueStateChord(hwnd, r);
            case "queue-mouse": return QueueMouse(hwnd, r);
            case "popup-surfaces": return PopupSurfaces(hwnd, r);
            case "capture-popups": return CapturePopups(hwnd, r);
            case "capture-popups-shared": WindowCaptureResearch.PrivateOnly(); return CapturePopups(hwnd, r, true);
            case "capture-popups-direct":
                WindowCaptureResearch.PrivateOnly();
                if(r.Message>1)throw new ArgumentException("Unsupported private capture method.");
                return CapturePopupsDirect(hwnd,r,windowDc:r.Message==1);
            case "begin-preview":
                WindowCaptureResearch.PrivateOnly();
                if(r.Key is not (1 or 3))throw new ArgumentException("Choose client or full-content private preview flags.");
                if(r.Message>1)throw new ArgumentException("Unknown private preview copy method.");
                return asynchronousPreview.Begin(r.Text??"",output=>{Validate(r,hwnd);return CapturePopupsDirect(hwnd,r,output,(uint)r.Key,r.Message==1);});
            case "capture-popups-repaint": WindowCaptureResearch.PrivateOnly(); Native.Check(Native.RedrawWindow(hwnd,0,0,0x181),"Refresh owned private paint"); return CapturePopupsDirect(hwnd,r);
            case "pointer-hit":
                WindowCaptureResearch.PrivateOnly();
                var pointer = new Native.POINT(r.X, r.Y); Native.ClientToScreen(hwnd, ref pointer);
                uint pointerThread = Native.GetWindowThreadProcessId(hwnd, out _);
                var pointerGui = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
                Native.Check(Native.GetGUIThreadInfo(pointerThread, ref pointerGui), "Read owned pointer capture");
                nint pointerTarget = pointerGui.hwndCapture;
                if (pointerTarget != 0) { Validate(r, pointerTarget); ValidateInput(pointerTarget); }
                else
                {
                    var popup = PopupSurfaces(hwnd, r).FirstOrDefault(p => pointer.X >= p.X && pointer.X < p.X + p.Width && pointer.Y >= p.Y && pointer.Y < p.Y + p.Height);
                    pointerTarget = popup == null ? hwnd : (nint)popup.Handle;
                    for (int depth = 0; depth < 32; depth++)
                    {
                        var local = pointer; Native.ScreenToClient(pointerTarget, ref local);
                        nint child = Native.ChildWindowFromPointEx(pointerTarget, local, 1 | 2 | 4);
                        if (child == 0 || child == pointerTarget) break;
                        pointerTarget = child;
                    }
                    Validate(r, pointerTarget);
                }
                Native.ScreenToClient(pointerTarget, ref pointer);
                return new { window = (long)pointerTarget, x = pointer.X, y = pointer.Y };
            case "focus":
                uint thread = Native.GetWindowThreadProcessId(hwnd, out _);
                var gui = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
                Native.Check(Native.GetGUIThreadInfo(thread, ref gui), "GetGUIThreadInfo");
                if (gui.hwndFocus != 0) Validate(r, gui.hwndFocus);
                return (long)gui.hwndFocus;
            case "thread-observation":
                uint observedThread = Native.GetWindowThreadProcessId(hwnd, out _);
                var observed = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
                Native.Check(Native.GetGUIThreadInfo(observedThread, ref observed), "Observe owned GUI thread");
                foreach (nint candidate in new[] { observed.hwndActive, observed.hwndFocus, observed.hwndCapture, observed.hwndMenuOwner }) if (candidate != 0) Validate(r, candidate);
                return new { thread = observedThread, observed.flags, active = (long)observed.hwndActive, focused = (long)observed.hwndFocus, capture = (long)observed.hwndCapture, menuOwner = (long)observed.hwndMenuOwner };
            case "mouse":
                if (!Enum.IsDefined(typeof(MouseMessage), r.Message)) throw new ArgumentException("Unsupported mouse message.");
                var mousePoint = new Native.POINT(r.X, r.Y);
                if (r.Message == (uint)MouseMessage.Wheel) Native.ClientToScreen(hwnd, ref mousePoint);
                int mousePacked = (mousePoint.X & 0xffff) | ((mousePoint.Y & 0xffff) << 16);
                uint mouseParam = (uint)(r.Buttons & 0xffff) | (uint)((r.Delta & 0xffff) << 16);
                Deliver(hwnd, r.Message, mouseParam, mousePacked, r.Posted); return true;
            case "close":
                Native.Check(Native.PostMessageW(hwnd, 0x10, 0, 0), "Post WM_CLOSE"); return true;
            case "invoke":
                var element = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                var action = element.FindFirst(System.Windows.Automation.TreeScope.Subtree, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, r.Text));
                if (action == null || !action.TryGetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern, out var pattern)) throw new InvalidOperationException("Owned subtree does not expose that Invoke action.");
                ((System.Windows.Automation.InvokePattern)pattern).Invoke(); return true;
            case "selection":
                if (!Native.ClassName(hwnd).Contains("EDIT", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Selection query requires an owned native edit control.");
                Native.Check(Native.SendMessageTimeoutW(hwnd, 0xB0, 0, 0, 2, 750, out nuint selection) != 0, "EM_GETSEL");
                return new { start = (int)(selection & 0xffff), end = (int)((selection >> 16) & 0xffff) };
            case "select-control":
                if (!Native.ClassName(hwnd).Contains("EDIT", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Selection adapter requires an owned native edit control.");
                Native.Message(hwnd, 0xB1, (nuint)r.X, r.Y); return true;
            case "scroll-position": return Native.GetScrollPos(hwnd, r.Key);
            case "scroll-control":
                Validate(r, (nint)r.TargetWindow);
                if (Native.GetParent((nint)r.TargetWindow) != hwnd || Native.ClassName((nint)r.TargetWindow) != "ScrollBar") throw new InvalidOperationException("Scrollbar notification must target its owned parent/control pair.");
                Native.Message(hwnd, 0x115, (nuint)r.Key, (nint)r.TargetWindow); return true;
            case "cancel-menu": Native.Message(hwnd, 0x1F, 0, 0); return true;
            case "click":
                int packed = (r.X & 0xffff) | ((r.Y & 0xffff) << 16);
                Native.Message(hwnd, Native.WM_MOUSEMOVE, 0, packed);
                Native.Message(hwnd, Native.WM_LBUTTONDOWN, 1, packed);
                Native.Message(hwnd, Native.WM_LBUTTONUP, 0, packed);
                return true;
            case "hit":
                var point = new Native.POINT(r.X, r.Y);
                Native.ClientToScreen(hwnd, ref point);
                nint target = hwnd;
                for (int i = 0; i < 32; i++)
                {
                    var local = point; Native.ScreenToClient(target, ref local);
                    nint child = Native.ChildWindowFromPointEx(target, local, 1 | 2 | 4);
                    if (child == 0 || child == target) break;
                    target = child;
                }
                Validate(r, target);
                Native.ScreenToClient(target, ref point);
                return new { window = (long)target, x = point.X, y = point.Y };
            case "capture": return r.Backend switch { CaptureBackend.WindowsGraphicsCapture => CaptureWgc(hwnd), CaptureBackend.WindowsGraphicsCaptureFromDefault => CaptureWgc(hwnd, true), CaptureBackend.CooperativeFixture => Native.ClassName(hwnd) == "#32770" ? Capture(hwnd) : CaptureFixture(), CaptureBackend.PrintWindowFullContent => Capture(hwnd, 3), CaptureBackend.ReusableWindowsGraphicsCapture => CaptureReusable(hwnd), _ => Capture(hwnd) };
            case "capture-research": return WindowCaptureResearch.Capture(hwnd, r.Text ?? "");
            case "hidden-capture-research":
                var parts = (r.Text ?? "").Split(':');
                if (parts.Length != 2) throw new ArgumentException("Specify strategy:capture mode.");
                if (r.TargetWindow != 0)
                {
                    nint child = (nint)r.TargetWindow; Validate(r, child); nint ancestor = child;
                    for (int depth = 0; ancestor != 0 && ancestor != hwnd && depth < 64; depth++) ancestor = Native.GetParent(ancestor);
                    if (ancestor != hwnd) throw new ArgumentException("Capture target must belong to the hidden source subtree.");
                }
                return WindowCaptureResearch.Hidden(hwnd, parts[0], parts[1], r.SettleMilliseconds, (nint)r.TargetWindow);
            case "inspect-hidden": return WindowCaptureResearch.Inspect(hwnd, r.Key == 3, r.SettleMilliseconds);
            case "presented-input": return PresentForInput(hwnd, r);
            case "activated-input": return PresentForInput(hwnd, r with { CaptureAfter = false, SettleMilliseconds = 0, NotifyActivation = true }, false);
            case "notification-input": return PresentForInput(hwnd, r with { NotifyActivation = true }, false, true);
            case "research-activation": return WindowActivationResearch.Run(hwnd, r.Text ?? "");
            case "client-size":
                Native.Check(Native.GetClientRect(hwnd,out var agentClient),"Read owned client bounds");return new{Width=agentClient.Right-agentClient.Left,Height=agentClient.Bottom-agentClient.Top};
            case "client-origin":
                var origin = new Native.POINT(0, 0); Native.Check(Native.ClientToScreen(hwnd, ref origin), "Owned desktop client origin"); return new { origin.X, origin.Y };
            case "hide": Native.ShowWindow(hwnd, 0); return true;
            case "show": Native.ShowWindow(hwnd, 4); return true; // SW_SHOWNOACTIVATE on the private desktop.
            default: throw new ArgumentException("Unsupported operation.");
        }
    }

    static void Deliver(nint hwnd, uint message, nuint wparam, nint lparam, bool posted)
    { if (posted) Native.Check(Native.PostMessageW(hwnd, message, wparam, lparam), "PostMessage"); else Native.Message(hwnd, message, wparam, lparam); }
    static void ValidateInput(nint hwnd)
    {
        // A modal's owner is intentionally disabled. Check child ancestors only,
        // ending at GA_ROOT, rather than following popup ownership with GetParent.
        nint root = Native.GetAncestor(hwnd, 2), current = hwnd;
        for (int depth = 0; current != 0 && depth < 64; depth++)
        {
            if (!Native.IsWindowEnabled(current)) throw new InvalidOperationException("Input destination is disabled, possibly by an owned modal dialog. Refresh and select its enabled window.");
            if (current == root) return;
            current = Native.GetParent(current);
        }
        throw new InvalidOperationException("Input destination has no valid enabled root.");
    }
    static PresentedInteraction PresentForInput(nint root, Request request, bool presentSource = true, bool notifyVisibility = false)
    {
        WindowCaptureResearch.PrivateOnly(); PresentedInputValidation.Check(request.Inputs, request.SettleMilliseconds);
        if (!presentSource && !notifyVisibility && request.Inputs!.Any(input => input.Kind != PresentedInputKind.Text)) throw new ArgumentException("Input without presentation supports text commands only.");
        if (Native.GetParent(root) != 0) throw new ArgumentException("Private presentation requires a top-level source window.");
        // Validate every target before changing visibility or sending any input.
        foreach (var command in request.Inputs!)
        {
            nint target = command.Window == 0 ? root : (nint)command.Window; Validate(request, target); ValidateInput(target);
            nint ancestor = target;
            for (int depth = 0; ancestor != 0 && ancestor != root && depth < 64; depth++) ancestor = Native.GetParent(ancestor);
            if (ancestor != root) throw new ArgumentException("Input target must belong to the presented source subtree.");
        }
        bool hidden = !Native.IsWindowVisible(root); var watch = Stopwatch.StartNew(); CaptureFrame? frame = null;
        if (notifyVisibility && !hidden) throw new InvalidOperationException("Experimental visibility notification input requires an initially hidden source.");
        try
        {
            if (notifyVisibility) { WindowActivationResearch.NotifyPositionVisibility(root, true); Thread.Sleep(request.SettleMilliseconds); }
            if (hidden && presentSource) { Native.ShowWindow(root, 4); Thread.Sleep(request.SettleMilliseconds); }
            if (presentSource) Native.Check(Native.IsWindowVisible(root), "Verify private presentation became visible");
            if (request.NotifyActivation) { Validate(request, root); WindowActivationResearch.NotifyActivation(root); }
            foreach (var command in request.Inputs!)
            {
                long target = command.Window == 0 ? (long)root : command.Window;
                var input = request with { Window = target, Posted = false };
                Validate(input, (nint)target);
                switch (command.Kind)
                {
                    case PresentedInputKind.Text: Execute(input with { Operation = "text", Text = command.Text }); break;
                    case PresentedInputKind.KeyChord: QueueStateChord((nint)target, input with { Keys = command.Keys }); break;
                    case PresentedInputKind.MouseSequence: QueueMouse((nint)target, input with { Steps = command.Steps, PrimeCapture = true }); break;
                }
            }
            // Renderer/other-thread dispatch can continue after a synchronous window message.
            // This bounded delay is empirical; it neither retries nor acknowledges source work.
            Thread.Sleep(request.SettleMilliseconds);
            if (request.CaptureAfter) frame = WindowCaptureResearch.Capture(root, request.Key == 3 ? "pw3" : "pw1");
        }
        finally
        {
            try { if (notifyVisibility && Native.IsWindow(root)) WindowActivationResearch.NotifyPositionVisibility(root, false); }
            finally { if (hidden && Native.IsWindow(root) && (presentSource || Native.IsWindowVisible(root))) Native.ShowWindow(root, 0); }
        }
        double elapsed = watch.Elapsed.TotalMilliseconds;
        if (frame != null) frame = frame with { CaptureMilliseconds = elapsed, Backend = (notifyVisibility ? "Experimental private visibility notifications / " : "Private presentation / ") + (request.NotifyActivation ? "activation pulse + " : "") + "native input + " + (request.Key == 3 ? "pw3" : "pw1") + ", initially " + (hidden ? "hidden" : "visible") };
        return new(frame, hidden, Native.IsWindow(root) && !Native.IsWindowVisible(root), elapsed, request.NotifyActivation);
    }
    static bool QueueStateChord(nint hwnd, Request r)
    {
        string desktopName = Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId()));
        string inputDesktop = Native.InputDesktopName();
        if (!desktopName.StartsWith("VIAVW_", StringComparison.Ordinal) || inputDesktop == "unavailable" || desktopName == inputDesktop)
            throw new InvalidOperationException("Input-state sharing is restricted to a non-input VIAVW private desktop.");
        if(r.PreciseChordHoldMilliseconds!=0&&(r.PreciseChordHoldMilliseconds is <16 or >40||!r.Posted||!inputSession.Enabled||!inputSession.LowLatency))throw new ArgumentException("Precise chord pacing requires a posted fast private session and a 16–40 ms hold");
        uint target = Native.GetWindowThreadProcessId(hwnd, out _), current = Native.GetCurrentThreadId();
        Native.PeekMessageW(out _, 0, 0, 0, 0); // Ensure this RPC thread owns a USER message queue.
        if(inputSession.Enabled)inputSession.Ensure(hwnd);else Native.Check(Native.AttachThreadInput(current, target, true), "Attach owned private input queues");
        var original = new byte[256];
        // Function commands can open/disable framework windows between down
        // and up (notably VS designer/code switches). Keep the demonstrated
        // settling window for these transitions; ordinary shortcuts use the
        // shorter session pacing.
        bool functionTransition=(r.Keys??[]).Any(key=>key is >=0x70 and <=0x87);
        int stepMilliseconds=functionTransition?30:inputSession.StepMilliseconds;
        bool preciseChord=r.PreciseChordHoldMilliseconds>0&&!functionTransition;
        int holdMilliseconds=functionTransition?50:preciseChord?r.PreciseChordHoldMilliseconds:inputSession.ChordHoldMilliseconds;
        try
        {
            Native.Check(Native.GetKeyboardState(original), "Read attached keyboard state");
            var state = (byte[])original.Clone();
            foreach (int key in r.Keys ?? [])
            {
                state[key] |= 0x80;
                // Frameworks commonly query side-specific modifier codes too.
                if (key == 0x11) state[0xA2] |= 0x80;
                if (key == 0x10) state[0xA0] |= 0x80;
                if (key == 0x12) state[0xA4] |= 0x80;
                Native.Check(Native.SetKeyboardState(state), "Set attached keyboard state");
                Key(hwnd, key, true, r.Posted, (r.Keys ?? []).Contains(0x12));
                if (r.Posted) inputSession.DelayChord(stepMilliseconds,preciseChord); // Bounded pacing, not a processing acknowledgement.
            }
            if (r.Posted) inputSession.DelayChord(holdMilliseconds,preciseChord);
            foreach (int key in (r.Keys ?? []).Reverse())
            {
                // Alt+F4 can finish successfully by destroying its destination.
                // There is then no valid HWND to receive a key-up. The finally
                // block still restores the private state and releases attachment.
                if (!Native.IsWindow(hwnd)) break;
                nint releaseTarget = hwnd;
                if (r.FollowFocusOnKeyUp)
                {
                    var focused = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
                    bool observed = Native.GetGUIThreadInfo(target, ref focused);
                    if (!observed && !Native.IsWindow(hwnd)) break;
                    Native.Check(observed, "Observe owned shortcut release focus");
                    if (focused.hwndFocus != 0)
                    {
                        // A shortcut may create a popup before key release. Never
                        // route to another thread, job or physical input desktop.
                        Native.Check(Native.GetWindowThreadProcessId(focused.hwndFocus, out _) == target, "Shortcut release must stay on original GUI thread");
                        Validate(r, focused.hwndFocus); ValidateInput(focused.hwndFocus);
                        releaseTarget = focused.hwndFocus;
                    }
                }
                state[key] &= 0x7f;
                if (key == 0x11) state[0xA2] &= 0x7f;
                if (key == 0x10) state[0xA0] &= 0x7f;
                if (key == 0x12) state[0xA4] &= 0x7f;
                // Compare explicit release timing without changing the accepted
                // path. Frameworks may query GetKeyState while handling KeyUp.
                if (r.ReleaseStateBeforeKeyUp) Native.Check(Native.SetKeyboardState(state), "Release attached state before key-up");
                Key(releaseTarget, key, false, r.Posted, (r.Keys ?? []).Contains(0x12));
                if (r.Posted) inputSession.DelayChord(stepMilliseconds,preciseChord);
                if (!r.ReleaseStateBeforeKeyUp) Native.Check(Native.SetKeyboardState(state), "Release attached keyboard state");
            }
            return true;
        }
        finally
        {
            try { Native.Check(Native.SetKeyboardState(inputSession.Enabled?PrivateInputSession.Idle(original):original), "Restore attached state"); }
            finally { if(!inputSession.Enabled){bool detached=Native.AttachThreadInput(current,target,false); if(!detached&&Native.IsWindow(hwnd))Native.Check(false,"Detach private input queues");} }
        }
    }
    static object QueueMouse(nint hwnd, Request r)
    {
        string desktop = Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())), input = Native.InputDesktopName();
        if (!desktop.StartsWith("VIAVW_", StringComparison.Ordinal) || input == "unavailable" || desktop == input) throw new InvalidOperationException("Mouse queue state is confined to owned non-input private desktops.");
        uint current = Native.GetCurrentThreadId(), target = Native.GetWindowThreadProcessId(hwnd, out _);
        Native.PeekMessageW(out _, 0, 0, 0, 0); if(inputSession.Enabled)inputSession.Ensure(hwnd);else Native.Check(Native.AttachThreadInput(current, target, true), "Attach private mouse state");
        var original = new byte[256]; nint oldCapture = Native.GetCapture(); bool primed = false, warmupReleased = false, completed = false, postedContextRelease = false,destinationClosed=false,released=false;int deliveredSteps=0;
        try
        {
            Native.Check(Native.GetKeyboardState(original), "Read private button state");
            if (oldCapture != 0) Validate(r, oldCapture);
            if (r.PrimeCapture) { Native.SetFocus(hwnd); Native.SetCapture(hwnd); primed = Native.GetCapture() == hwnd; }
            foreach (var step in r.Steps ?? [])
            {
                // A popup or close button can destroy its HWND while handling
                // the final release. Never replay that action or send the
                // trailing pointer move to a destroyed destination.
                if(released&&!Native.IsWindow(hwnd)&&step.Message==MouseMessage.Move){destinationClosed=true;break;}
                var state = (byte[])original.Clone();
                foreach (var pair in new[] { (1, 1), (2, 2), (4, 0x10), (8, 0x11), (16, 4) }) state[pair.Item2] = (byte)((state[pair.Item2] & 0x7f) | ((step.Buttons & pair.Item1) != 0 ? 0x80 : 0));
                state[0xA0] = state[0x10]; state[0xA2] = state[0x11]; Native.Check(Native.SetKeyboardState(state), "Set private mouse/modifier state");
                var point = new Native.POINT(step.X, step.Y); if (step.Message == MouseMessage.Wheel) Native.ClientToScreen(hwnd, ref point);
                // Some context menus run a modal dispatcher inside RightUp.
                // Queue its already-released state instead of synchronously
                // blocking the worker until the human chooses a menu command.
                bool postContext = r.WarmupCapture && step.Message == MouseMessage.RightUp;
                try{Deliver(hwnd, (uint)step.Message, (nuint)((uint)step.Buttons | (uint)((step.Delta & 0xffff) << 16)), (point.X & 0xffff) | ((point.Y & 0xffff) << 16), r.Posted || postContext);}
                catch(System.ComponentModel.Win32Exception e)when(e.NativeErrorCode==1400&&!Native.IsWindow(hwnd)&&step.Message is MouseMessage.LeftUp or MouseMessage.RightUp)
                {destinationClosed=true;break;}
                deliveredSteps++;released=step.Message is MouseMessage.LeftUp or MouseMessage.RightUp;
                postedContextRelease |= postContext;
                if (r.WarmupCapture && !warmupReleased)
                {
                    // Activate the framework's mouse source with a move, then let
                    // the application acquire capture itself for down/menu/drag.
                    // Releasing capture after it opens a menu would cancel it.
                    if (Native.GetCapture() == hwnd && oldCapture != hwnd) Native.ReleaseCapture();
                    warmupReleased = true;
                }
                if (r.Posted || r.WarmupCapture || r.PacedMouse) inputSession.DelayMouse(inputSession.StepMilliseconds); // Bounded pacing; the compatibility suite checks the actual result.
            }
            completed = true;
            return new { primed, warmupReleased, postedContextRelease,destinationClosed,deliveredSteps, captureAfter = (long)Native.GetCapture(), privateOnly = true };
        }
        finally
        {
            try
            {
                if ((!warmupReleased || !completed) && Native.GetCapture() == hwnd && oldCapture != hwnd) { Native.ReleaseCapture(); if (!warmupReleased && oldCapture != 0 && Native.IsWindow(oldCapture)) { Validate(r, oldCapture); Native.SetCapture(oldCapture); } }
                Native.Check(Native.SetKeyboardState(inputSession.Enabled?PrivateInputSession.Idle(original):original), "Restore private mouse state");
            }
            finally { if(!inputSession.Enabled){bool detached=Native.AttachThreadInput(current,target,false);if(!detached&&Native.IsWindow(hwnd))Native.Check(false,"Detach private mouse state");} }
        }
    }
    static void Key(nint hwnd, int key, bool down, bool posted, bool system = false)
    {
        uint scan = Native.MapVirtualKeyW((uint)key, 4);
        uint bits = 1 | ((scan & 0xff) << 16) | ((scan & 0xff00) != 0 ? 1u << 24 : 0) | (system ? 1u << 29 : 0) | (down ? 0 : 0xC0000000u);
        bool systemMessage=system;
        Deliver(hwnd, systemMessage ? (down ? 0x104u : 0x105u) : (down ? Native.WM_KEYDOWN : Native.WM_KEYUP), (nuint)key, (nint)bits, posted);
    }

    static WindowInfo[] Windows(Request r, bool childText = true, bool rootOnly = false)
    {
        var windows = new List<WindowInfo>();
        Native.EnumProc collect = (hwnd, parent) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != Environment.ProcessId && (r.AllowedProcessIds ?? []).Contains((int)pid))
            {
                Native.GetWindowRect(hwnd, out var rect);
                windows.Add(new((long)hwnd, parent == 0 ? 0 : (long)Native.GetParent(hwnd), (int)pid, Native.ClassName(hwnd), childText ? Native.WindowText(hwnd) : parent == 0 ? Native.WindowCaption(hwnd) : "", Native.IsWindowVisible(hwnd), Native.IsWindowEnabled(hwnd), rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, parent == 0 ? (long)Native.GetWindow(hwnd, 4) : 0));
            }
            return true;
        };
        Native.EnumProc top = (hwnd, _) => { collect(hwnd, 0); Native.EnumChildWindows(hwnd, collect, hwnd); return true; };
        // An empty desktop can return false with ERROR_SUCCESS while the target starts.
        System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
        bool enumerated;
        if(rootOnly)
        {
            nint selectedRoot=Native.GetAncestor((nint)r.Window,2);Validate(r,selectedRoot);
            top(selectedRoot,0);enumerated=true;
        }
        else enumerated = Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), top, 0);
        int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        if (!enumerated && error != 0) throw new System.ComponentModel.Win32Exception(error, "EnumDesktopWindows");
        return windows.ToArray();
    }

    static PopupSurface[] PopupSurfaces(nint root, Request r)
    {
        WindowCaptureResearch.PrivateOnly();
        uint thread = Native.GetWindowThreadProcessId(root, out uint pid);
        var origin = new Native.POINT(0, 0); Native.ClientToScreen(root, ref origin); Native.GetClientRect(root, out var client);
        var result = new List<PopupSurface>();
        // EnumDesktopWindows preserves top-to-bottom z order. WPF popup HWNDs
        // often have no native owner; constrain these to the same GUI thread,
        // exact job/desktop, captionless popup style and framework window class.
        var candidates = new List<nint>();
        Native.EnumProc collect = (hwnd, _) => { if (hwnd != root && Native.IsWindowVisible(hwnd) && Native.IsWindowEnabled(hwnd) && Native.GetWindowThreadProcessId(hwnd, out uint candidatePid) == thread && candidatePid == pid) candidates.Add(hwnd); return true; };
        System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
        bool enumerated = Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), collect, 0);
        int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        if (!enumerated && error != 0) throw new System.ComponentModel.Win32Exception(error, "Read owned popup surfaces");
        foreach (nint hwnd in candidates)
        {
            string popupClass=Native.ClassName(hwnd);
            if (!popupClass.StartsWith("HwndWrapper[", StringComparison.Ordinal)&&popupClass!="#32768") continue;
            uint style = unchecked((uint)(long)Native.GetWindowLongPtr(hwnd, -16)), extended = unchecked((uint)(long)Native.GetWindowLongPtr(hwnd, -20));
            if ((style & 0x80000000) == 0 || (style & 0x00C00000) != 0) continue;
            Validate(r, hwnd);
            var position = new Native.POINT(0, 0); Native.ClientToScreen(hwnd, ref position); Native.GetClientRect(hwnd, out var bounds);
            if (bounds.Right <= 2 || bounds.Bottom <= 2) continue;
            bool clipped = position.X < origin.X || position.Y < origin.Y || position.X + bounds.Right > origin.X + client.Right || position.Y + bounds.Bottom > origin.Y + client.Bottom;
            result.Add(new((long)hwnd, position.X, position.Y, bounds.Right, bounds.Bottom, position.X - origin.X, position.Y - origin.Y, clipped, style, extended));
            if (result.Count > 8) throw new InvalidOperationException("Too many owned popup surfaces for bounded presentation.");
        }
        return result.ToArray();
    }
    static object CapturePopups(nint root, Request r, bool shared = false)
    {
        var watch = Stopwatch.StartNew(); var surfaces = PopupSurfaces(root, r);
        using var bitmap = RenderWindow(root, 3, out double nativeMs);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            foreach (var surface in surfaces.Reverse())
            {
                Validate(r, (nint)surface.Handle);
                using var popup = RenderWindow((nint)surface.Handle, 3, out double popupMs); nativeMs += popupMs;
                graphics.DrawImageUnscaled(popup, surface.FrameX, surface.FrameY);
            }
        }
        if (shared) return sharedPreview.Write(r.Text ?? "", bitmap, nativeMs, surfaces, watch);
        using var stream = new MemoryStream(); var encoding = Stopwatch.StartNew(); bitmap.Save(stream, ImageFormat.Png); encoding.Stop();
        return new PopupPresentation(new(stream.ToArray(), bitmap.Width, bitmap.Height, watch.Elapsed.TotalMilliseconds, "PrintWindow / native owned popup composition", NativeRenderingMilliseconds: nativeMs, EncodingMilliseconds: encoding.Elapsed.TotalMilliseconds), surfaces);
    }
    static SharedPreview CapturePopupsDirect(nint root, Request r, SharedPreviewWriter? output=null,uint rootFlags=3,bool windowDc=false)
    {
        var watch=Stopwatch.StartNew();var surfaces=PopupSurfaces(root,r);
        Native.Check(Native.GetClientRect(root,out var rect),"GetClientRect");
        var origin=new Native.POINT(0,0);Native.Check(Native.ClientToScreen(root,ref origin),"Read owned capture origin");
        if(windowDc)return (output??sharedPreview).RenderDc(r.Text??"",rect.Right,rect.Bottom,destination=>
        {
            double nativeMs=WindowCaptureResearch.CopyPrivateClient(root,destination,rect.Right,rect.Bottom);
            foreach(var surface in surfaces.Reverse())
            {
                nint popupWindow=(nint)surface.Handle;
                if(!Native.IsWindow(popupWindow)||!Native.IsWindowVisible(popupWindow))continue;
                Native.GetClientRect(popupWindow,out var size);if(size.Right<=2||size.Bottom<=2)continue;
                try
                {
                    Validate(r,popupWindow);
                    using var popup=RenderWindow(popupWindow,3,out double popupMs);nativeMs+=popupMs;
                    WindowCaptureResearch.CopyBitmap(popup,destination,surface.FrameX,surface.FrameY);
                }
                catch(InvalidOperationException) when(!Native.IsWindow(popupWindow)||!Native.IsWindowVisible(popupWindow)){}
            }
            return nativeMs;
        },surfaces,watch) with {ClientX=origin.X,ClientY=origin.Y};
        return (output??sharedPreview).Render(r.Text??"",rect.Right,rect.Bottom,bitmap=>
        {
            double nativeMs=RenderIntoWindow(root,bitmap,rootFlags);
            using var graphics=Graphics.FromImage(bitmap);
            foreach(var surface in surfaces.Reverse())
            {
                nint popupWindow=(nint)surface.Handle;
                if(!Native.IsWindow(popupWindow)||!Native.IsWindowVisible(popupWindow))continue;
                Native.GetClientRect(popupWindow,out var size);if(size.Right<=2||size.Bottom<=2)continue;
                try
                {
                    Validate(r,popupWindow);
                    using var popup=RenderWindow(popupWindow,3,out double popupMs);nativeMs+=popupMs;
                    graphics.DrawImageUnscaled(popup,surface.FrameX,surface.FrameY);
                }
                catch(InvalidOperationException) when(!Native.IsWindow(popupWindow)||!Native.IsWindowVisible(popupWindow)){}
            }
            return nativeMs;
        },surfaces,watch) with {ClientX=origin.X,ClientY=origin.Y};
    }
    static double RenderIntoWindow(nint hwnd, Bitmap bitmap, uint flags)
    {
        using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.Magenta);
        var dc=graphics.GetHdc();
        try{var timer=Stopwatch.StartNew();Native.Check(Native.PrintWindow(hwnd,dc,flags),"PrintWindow");return timer.Elapsed.TotalMilliseconds;}
        finally{graphics.ReleaseHdc(dc);}
    }
    static Bitmap RenderWindow(nint hwnd, uint flags, out double nativeMilliseconds)
    {
        Native.Check(Native.GetClientRect(hwnd, out var rect), "GetClientRect");
        int w = rect.Right, h = rect.Bottom;
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (long)w * h > 20000000) throw new InvalidOperationException("Capture size unsupported.");
        var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Magenta); // Makes failed/partial rendering visible.
            var dc = graphics.GetHdc();
            try { var nativeWatch = Stopwatch.StartNew(); Native.Check(Native.PrintWindow(hwnd, dc, flags), "PrintWindow"); nativeMilliseconds = nativeWatch.Elapsed.TotalMilliseconds; }
            finally { graphics.ReleaseHdc(dc); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    static CaptureFrame Capture(nint hwnd, uint flags = 1)
    {
        var watch = Stopwatch.StartNew(); using var bitmap = RenderWindow(hwnd, flags, out double nativeMilliseconds);
        using var stream = new MemoryStream();
        var encodingWatch = Stopwatch.StartNew();
        bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray(); encodingWatch.Stop();
        return new(bytes, bitmap.Width, bitmap.Height, watch.Elapsed.TotalMilliseconds, flags == 3 ? "PrintWindow / PW_CLIENTONLY | PW_RENDERFULLCONTENT" : "PrintWindow", NativeRenderingMilliseconds: nativeMilliseconds, EncodingMilliseconds: encodingWatch.Elapsed.TotalMilliseconds);
    }
    static CaptureFrame CaptureReusable(nint hwnd)
    {
        string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/reusable-private"));
        reusableCapture ??= new ReusableCaptureClient(0, Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())), Path.Combine(output, Environment.ProcessId.ToString()));
        string png = Path.Combine(output, Environment.ProcessId.ToString(), "frame.png"); var watch = Stopwatch.StartNew();
        try
        {
            reusableCapture.Capture(hwnd, png); var bytes = File.ReadAllBytes(png); using var stream = new MemoryStream(bytes); using var image = new Bitmap(stream);
            return new(bytes, image.Width, image.Height, watch.Elapsed.TotalMilliseconds, "Reusable WindowsGraphicsCapture / full window", reusableCapture.ProcessId);
        }
        catch { reusableCapture.Dispose(); reusableCapture = null; throw; }
    }
    static CaptureFrame CaptureFixture()
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts")) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(applicationDirectory ?? "");
        if (!directory.StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cooperative capture requires an explicit fixture working directory inside project artifacts.");
        string png = Path.Combine(directory, "fixture-grab.png");
        if (File.Exists(png)) File.Delete(png);
        File.WriteAllText(Path.Combine(directory, "grab.request"), "capture");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(png) && watch.ElapsedMilliseconds < 3000) Thread.Sleep(25);
        if (!File.Exists(png)) throw new InvalidOperationException("Application has no enabled cooperative fixture capture adapter.");
        byte[] bytes = File.ReadAllBytes(png); using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream);
        return new(bytes, image.Width, image.Height, watch.Elapsed.TotalMilliseconds, "CooperativeFixture (application integration)");
    }
    static CaptureFrame CaptureWgc(nint hwnd, bool fromDefault = false)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string executable = Path.Combine(project, "artifacts", "native-build", "WgcCapture.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Build the optional native WGC helper with scripts/build-native.ps1.", executable);
        string output = Path.Combine(project, "artifacts", "wgc", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        string png = Path.Combine(output, "frame.png"); var watch = Stopwatch.StartNew();
        if (fromDefault && Native.InputDesktopName() != "Default") throw new InvalidOperationException("Capture calibration requires the ordinary Default input desktop; no secure desktop will be accessed.");
        Native.GetWindowRect(hwnd, out var requestedBounds);
        var info = Native.LaunchSuspended(executable, [((long)hwnd).ToString(), png, (requestedBounds.Right - requestedBounds.Left).ToString(), (requestedBounds.Bottom - requestedBounds.Top).ToString()], fromDefault ? "WinSta0\\Default" : null, output);
        try
        {
            Native.Check(Native.ResumeThread(info.hThread) != uint.MaxValue, "Resume WGC helper");
            if (Native.WaitForSingleObject(info.hProcess, 4000) != 0) { Native.TerminateProcess(info.hProcess, 2); throw new TimeoutException("WGC helper exceeded its bounded capture deadline; target preserved."); }
            Native.Check(Native.GetExitCodeProcess(info.hProcess, out uint exit), "Read WGC exit status");
            if (exit != 0)
            {
                string error = Path.ChangeExtension(png, ".error.txt");
                throw new InvalidOperationException((File.Exists(error) ? File.ReadAllText(error) : "WGC helper failed") + "; evidence: " + output);
            }
        }
        finally { Native.CloseHandle(info.hThread); Native.CloseHandle(info.hProcess); }
        using var original = new Bitmap(png);
        Native.GetClientRect(hwnd, out var client); var point = new Native.POINT(0, 0); Native.ClientToScreen(hwnd, ref point);
        Native.GetWindowRect(hwnd, out var bounds);
        if (Native.DwmGetWindowAttribute(hwnd, 9, out var extended, 16) == 0) bounds = extended;
        int x = original.Width == client.Right ? 0 : Math.Max(0, point.X - bounds.Left);
        int y = original.Height == client.Bottom ? 0 : Math.Max(0, point.Y - bounds.Top);
        if (x + client.Right > original.Width || y + client.Bottom > original.Height) throw new InvalidOperationException("WGC/client bounds differ; refusing an incorrect coordinate mapping.");
        using var cropped = original.Clone(new Rectangle(x, y, client.Right, client.Bottom), PixelFormat.Format32bppArgb);
        using var stream = new MemoryStream(); cropped.Save(stream, ImageFormat.Png);
        return new(stream.ToArray(), cropped.Width, cropped.Height, watch.Elapsed.TotalMilliseconds, "WindowsGraphicsCapture");
    }
}

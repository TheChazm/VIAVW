using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

public sealed class LayerManager : IDisposable
{
    readonly List<ApplicationLayer> layers = [];
    public ApplicationLayer CreateLayer()
    {
        var layer = new ApplicationLayer(); layers.Add(layer); return layer;
    }
    internal ApplicationLayer CreateResearchMtaLayer(){var layer=new ApplicationLayer(true);layers.Add(layer);return layer;}
    internal ApplicationLayer CreateDurableLayer(){var layer=new ApplicationLayer(false,true);layers.Add(layer);return layer;}
    internal ApplicationLayer AdoptLayer(string descriptor){var layer=new ApplicationLayer(descriptor);layers.Add(layer);return layer;}
    public void Dispose() { foreach (var layer in layers) layer.Dispose(); layers.Clear(); }
}

// One isolated worker and one kill-on-close job per layer. A stuck capture only faults its layer.
public sealed partial class ApplicationLayer : IDisposable
{
    readonly object gate = new();
    readonly bool researchMtaWorker;
    readonly bool durableMode;
    nint workerJob;
    bool inputAttempted;
    internal bool PreserveOnWorkerFault {get;set;}
    bool workerFaulted,recoveryPending;string? workerFault;
    internal bool WorkerFaulted {get{lock(gate)return workerFaulted;}}
    internal bool RecoveryPending {get{lock(gate)return recoveryPending;}}
    internal string? WorkerFault {get{lock(gate)return workerFault;}}
    internal void AcceptRecovery(){lock(gate){if(workerFaulted)throw new InvalidOperationException("Recover the worker before reconciling.");recoveryPending=false;}}
    void SuspendWorker(string reason)
    {
        workerFaulted=true;recoveryPending=true;workerFault=reason;ownershipRevision++;AttentionReason="Layer paused after worker failure; observe and reconcile before input.";
        if(Native.JobProcessIds(job).Contains(WorkerProcessId))
        {
            nint process=Native.OpenProcess(0x0001|0x1000|0x00100000,false,(uint)WorkerProcessId);
            if(process!=0)try
            {
                if(Native.IsProcessInJob(process,job,out bool member)&&member&&Native.GetProcessTimes(process,out long created,out _,out _,out _)&&workerLifetimes.TryGetValue(WorkerProcessId,out var identity)&&created==identity.Created)
                {Native.Check(Native.TerminateProcess(process,3),"Stop exact failed layer worker");Native.WaitForSingleObject(process,1000);}
            }finally{Native.CloseHandle(process);}
        }
        DisposeConnection();
    }

    PassiveInspection? passiveInspection;
    PassiveInspection? pointInspection;
    long? legacyInspectionTicket;
    (long Window, string? Id, bool Menus) legacyInspectionTarget;
    long inspectionInputEpoch, legacyInspectionEpoch, legacyInspectionOwnership;
    NamedPipeServerStream? pipe;
    StreamReader? reader;
    StreamWriter? writer;
    nint desktop, job;
    bool closed;
    bool persistentInputRequested;
    bool lowLatencyInputRequested,preciseMouseWaitsRequested;
    int[] infrastructure = [];
    readonly Dictionary<int, long> infrastructureIdentity = [];
    readonly Dictionary<int, (long Created, long? Exited)> workerLifetimes = [];
    string? applicationDirectory;
    ControlOwner owner = ControlOwner.Automation;
    long ownershipRevision;
    readonly SharedPreviewReader sharedPreview = new();
    readonly SharedPreviewReader asynchronousPreview = new();
    readonly SemaphoreSlim previewRequests=new(1,1);
    EventWaitHandle? previewReady;
    public string Id { get; } = "VIAVW_" + Guid.NewGuid().ToString("N");
    public int WorkerProcessId { get; private set; }
    public int ApplicationProcessId { get; private set; }
    public string? AttentionReason { get; private set; }
    public ControlOwner Owner { get { lock (gate) return owner; } }
    public bool Closed { get { lock (gate) return closed; } }
    internal long OwnershipRevision { get { lock(gate) return ownershipRevision; } }

    internal ApplicationLayer():this(false){}
    internal ApplicationLayer(bool researchMtaWorker):this(researchMtaWorker,false){}
    internal ApplicationLayer(bool researchMtaWorker,bool durable)
    {
        this.researchMtaWorker=researchMtaWorker;durableMode=durable;PreserveOnWorkerFault=durable;
        try
        {
            desktop = Native.CreateDesktopW(Id, 0, 0, 0, Native.DESKTOP_ACCESS, 0);
            Native.Check(desktop != 0, "Create private desktop");
            job = Native.CreateJobObjectW(0, durable?Id+"_job":null); Native.Check(job != 0, "Create job");
            var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
            Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Set kill-on-job-close");
            StartWorker();
            RefreshInfrastructure(Native.JobProcessIds(job));
        }
        catch { Dispose(); throw; }
    }
    void StartWorker()
    {
            string pipeName = Id + "_rpc_" + Guid.NewGuid().ToString("N");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var workerExe = Path.Combine(AppContext.BaseDirectory, "LayerHost.exe");
            if(durableMode)
            {
                workerJob=Native.CreateJobObjectW(0,null);Native.Check(workerJob!=0,"Create replaceable control-worker job");
                var limits=new Native.EXTENDED_LIMIT{BasicLimitInformation=new Native.BASIC_LIMIT{LimitFlags=0x2000}};
                Native.Check(Native.SetInformationJobObject(workerJob,9,ref limits,(uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()),"Bound control-worker lifetime independently");
            }
            var process=Native.LaunchSuspended(workerExe,[researchMtaWorker?"--worker-mta":"--worker",pipeName,token],"WinSta0\\"+Id,Environment.CurrentDirectory);
            try
            {
                Native.Check(Native.AssignProcessToJobObject(durableMode?workerJob:job,process.hProcess),"Assign only the control worker to its exact job");
                Native.Check(Native.GetProcessTimes(process.hProcess,out long created,out _,out _,out _),"Read owned worker creation identity");
                WorkerProcessId=(int)process.dwProcessId;workerLifetimes[WorkerProcessId]=(created,null);infrastructureIdentity[WorkerProcessId]=created;
                Native.Check(Native.ResumeThread(process.hThread)!=uint.MaxValue,"Resume worker");
            }
            catch{Native.TerminateProcess(process.hProcess,1);throw;}
            finally{Native.CloseHandle(process.hThread);Native.CloseHandle(process.hProcess);}
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
            reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            var hello = JsonSerializer.Deserialize<JsonElement>(reader.ReadLineAsync(cancellation.Token).AsTask().GetAwaiter().GetResult() ?? throw new IOException("Missing worker handshake."));
            if (hello.GetProperty("token").GetString() != token || hello.GetProperty("desktop").GetString() != Id || hello.GetProperty("pid").GetInt32() != WorkerProcessId)
                throw new IOException("Worker authentication / desktop mismatch.");
    }

    public void RestartWorker()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (Native.JobProcessIds(job).Contains(WorkerProcessId))
            {
                nint process = Native.OpenProcess(0x0001 | 0x1000 | 0x00100000, false, (uint)WorkerProcessId);
                Native.Check(process != 0, "Open owned worker for restart");
                try
                {
                    Native.Check(Native.IsProcessInJob(process, job, out bool member) && member, "Worker must still belong to exact job");
                    Native.Check(Native.GetProcessTimes(process, out long created, out _, out _, out _) && created == workerLifetimes[WorkerProcessId].Created, "Worker creation identity must match");
                    Native.Check(Native.TerminateProcess(process, 3), "Restart only owned worker"); Native.WaitForSingleObject(process, 1000);
                    if (Native.GetProcessTimes(process, out _, out long exited, out _, out _) && exited != 0) workerLifetimes[WorkerProcessId] = (created, exited);
                }
                finally { Native.CloseHandle(process); }
            }
            DisposeConnection();
            try
            {
                workerFaulted=false;
                StartWorker();
                RefreshInfrastructure(Native.JobProcessIds(job));
                if (applicationDirectory != null) CallLocked(new("configure", Directory: applicationDirectory));
                if(persistentInputRequested)CallLocked(new("persistent-private-input",LowLatencyInput:lowLatencyInputRequested,PreciseMouseWaits:preciseMouseWaitsRequested));
            }
            catch { if(PreserveOnWorkerFault){workerFaulted=true;recoveryPending=true;}else Dispose(); throw; }
            PersistKeeper();
            // No input replay: caller must inspect state before retrying interrupted work.
        }
    }

    JsonElement CallLocked(Request request, int timeoutMs = 5000, bool recoverInitialRead = false)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if(workerFaulted)throw new LayerSuspendedException(workerFault??"Worker is paused.");
        if(recoverInitialRead&&(request.Operation!="prime-pointer"||inputAttempted))throw new InvalidOperationException("Initial pointer recovery is restricted to a read before the first input attempt.");
        if(request.Operation is "text" or "key" or "chord" or "queue-chord" or "queue-mouse" or "mouse" or "click" or "invoke" or "accessible-point-input" or "seed-private-modifiers" or "research-accessible-value" or "presented-input" or "activated-input" or "notification-input" or "select-control" or "scroll-control" or "cancel-menu" or "research-menu-character" or "research-system-menu" or "research-activation")inputAttempted=true;
        if(request.Operation is "text" or "key" or "chord" or "queue-chord" or "queue-mouse" or "mouse" or "click" or "invoke" or "accessible-point-input" or "seed-private-modifiers" or "research-accessible-value" or "presented-input" or "activated-input" or "notification-input" or "select-control" or "scroll-control" or "cancel-menu" or "show" or "hide" or "close" or "research-resize-window" or "research-system-menu" or "research-menu-character") inspectionInputEpoch++;
        request = request with { AllowedProcessIds = Native.JobProcessIds(job) };
        try
        {
            using var cancellation = new CancellationTokenSource(timeoutMs);
            writer!.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellation.Token).GetAwaiter().GetResult();
            var line = reader!.ReadLineAsync(cancellation.Token).AsTask().GetAwaiter().GetResult();
            if (line == null) throw new IOException("Worker disconnected.");
            var reply = JsonSerializer.Deserialize<Reply>(line)!;
            if (!reply.Success) { var error=new LayerOperationException(reply.Error ?? "Worker operation failed.");error.Data["workerDetails"]=reply.ErrorDetails;throw error; }
            return reply.Data;
        }
        catch (LayerOperationException) { throw; }
        catch (OperationCanceledException e)
        {
            if(recoverInitialRead){RestartWorker();throw new LayerOperationException($"Initial pointer preparation exceeded {timeoutMs} ms; only the worker was restarted, with no input replay.");}
            if(PreserveOnWorkerFault){SuspendWorker($"Operation '{request.Operation}' exceeded {timeoutMs} ms; application retained, delivery outcome uncertain.");throw new LayerSuspendedException(workerFault!);}
            Dispose(); throw new TimeoutException($"Layer operation '{request.Operation}' exceeded {timeoutMs} ms; the owned layer was terminated to contain the stall.", e);
        }
        catch(IOException error)when(PreserveOnWorkerFault){SuspendWorker(error.Message);throw new LayerSuspendedException("Worker transport failed; application retained. Do not replay input.");}
        catch(Exception error)when(PreserveOnWorkerFault){SuspendWorker(error.Message);throw new LayerSuspendedException("Worker communication failed; application retained, outcome may be unknown.");}
        catch { Dispose(); throw; } // Legacy disposable-host containment is preserved.
    }

    void CheckOwner(ControlOwner sender)
    {
        if(PreserveOnWorkerFault&&keeper!=null&&!keeper.IsAlive)throw new LayerSuspendedException("Keeper is being repaired; no new input may start.");
        if(PreserveOnWorkerFault&&(workerFaulted||recoveryPending))throw new LayerSuspendedException("Observe and reconcile the recovered layer before new input.");
        if (sender != owner) throw new InvalidOperationException($"Input rejected: {owner} owns the layer.");
    }
    public int LaunchApplication(string executable, string[]? arguments = null, string? workingDirectory = null, Dictionary<string, string>? environmentOverrides = null)
    {
        lock (gate)
        {
            if (ApplicationProcessId != 0) throw new InvalidOperationException("One application tree per layer in this prototype.");
            applicationDirectory = workingDirectory ?? Environment.CurrentDirectory;
            if(durableMode)
            {
                if(keeper==null)throw new InvalidOperationException("Retain the application job before launch.");
                var launched=keeper.Call("launch-application",new{executable=Path.GetFullPath(executable),arguments=arguments??[],directory=applicationDirectory,environment=environmentOverrides});
                ApplicationProcessId=launched.GetProperty("applicationPid").GetInt32();
                CallLocked(new("configure",Directory:applicationDirectory));
            }
            else ApplicationProcessId = CallLocked(new("launch", Executable: Path.GetFullPath(executable), Arguments: arguments, Directory: applicationDirectory, EnvironmentOverrides: environmentOverrides)).GetInt32();
            PersistKeeper();return ApplicationProcessId;
        }
    }
    public WindowInfo[] GetWindows() { lock (gate) return CallLocked(new("windows")).Deserialize<WindowInfo[]>()!; }
    internal WindowInfo[] GetInputSubtree(long window) { lock(gate) return CallLocked(new("input-subtree-windows",Window:window)).Deserialize<WindowInfo[]>()!; }
    // Presentation needs child geometry/classes, but not a synchronous WM_GETTEXT
    // round trip to every child control on every frame.
    internal WindowInfo[] GetPresentationWindows() { lock (gate) return CallLocked(new("presentation-windows")).Deserialize<WindowInfo[]>()!; }
    internal System.Text.Json.JsonElement ResearchStartTextEvents(long root){lock(gate){CheckOwner(ControlOwner.Automation);return CallLocked(new("research-start-text-events",Window:root));}}
    internal System.Text.Json.JsonElement ResearchReadTextEvents(long root){lock(gate)return CallLocked(new("research-read-text-events",Window:root));}
    internal System.Text.Json.JsonElement ResearchStopTextEvents(long root){lock(gate){CheckOwner(ControlOwner.Automation);return CallLocked(new("research-stop-text-events",Window:root));}}
    internal JsonElement ResearchWindowEvents(long root,string action){lock(gate){if(action is not ("start" or "read" or "stop"))throw new ArgumentException("Unknown event action");return CallLocked(new("research-"+action+"-window-events",Window:root));}}
    internal JsonElement ResearchEventReclamation(long root){lock(gate)return CallLocked(new("research-window-event-reclamation",Window:root));}
    internal JsonElement ResearchMeasureDcCosts(long root){lock(gate){CheckOwner(ControlOwner.Automation);return CallLocked(new("research-dc-costs",Window:root));}}
    internal JsonElement ResearchMeasurePacingWaits(){lock(gate){CheckOwner(ControlOwner.Automation);return CallLocked(new("research-pacing-waits"));}}
    internal void ResearchProbeWindowResponse(long window)
    { lock (gate) CallLocked(new("research-window-response", Window: window)); }
    public void SendKeyboardInput(long window, string text, ControlOwner sender = ControlOwner.Automation)
    {
        if (text.Length > 4096) throw new ArgumentException("Bound input batches to 4096 UTF-16 code units.");
        lock (gate) { CheckOwner(sender); CallLocked(new("text", Window: window, Text: text)); }
    }
    public void SendKey(long window, int virtualKey, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate) { CheckOwner(sender); CallLocked(new("key", Window: window, Key: virtualKey)); }
    }
    public void PostKeyboardInput(long window, string text, ControlOwner sender = ControlOwner.Automation, bool followPrivateFocus = false,bool textControlKeys=false)
    {
        if (text.Length > 4096) throw new ArgumentException("Bound text batches to 4096 code units.");
        lock (gate) { CheckOwner(sender); CallLocked(new("text", Window: window, Text: text, Posted: true, FollowPrivateFocus: followPrivateFocus,TextControlKeys:textControlKeys)); }
    }
    public void PostKey(long window, int virtualKey, ControlOwner sender = ControlOwner.Automation, bool followPrivateFocus = false)
    { lock (gate) { CheckOwner(sender); CallLocked(new("key", Window: window, Key: virtualKey, Posted: true, FollowPrivateFocus: followPrivateFocus)); } }
    public void SendKeyChord(long window, int[] keys, bool posted = true, ControlOwner sender = ControlOwner.Automation)
    {
        if (keys.Length is < 1 or > 8) throw new ArgumentException("Use a bounded 1–8 key chord.");
        lock (gate) { CheckOwner(sender); CallLocked(new("chord", Window: window, Keys: keys, Posted: posted)); }
    }
    // Experimental thread input-state sharing, confined to this owned private desktop.
    public void SendQueueStateChord(long window, int[] keys, bool posted = false, ControlOwner sender = ControlOwner.Automation, bool followFocusOnKeyUp = false, bool releaseStateBeforeKeyUp = false, bool followPrivateFocus = false,int preciseChordHoldMilliseconds=0)
    {
        if(preciseChordHoldMilliseconds!=0&&preciseChordHoldMilliseconds is <16 or >40)throw new ArgumentOutOfRangeException(nameof(preciseChordHoldMilliseconds));
        if (keys.Length is < 1 or > 8 || keys.Any(k => k is < 1 or > 255)) throw new ArgumentException("Use 1–8 valid virtual keys.");
        lock (gate) { CheckOwner(sender); CallLocked(new("queue-chord", Window: window, Keys: keys, Posted: posted, FollowFocusOnKeyUp: followFocusOnKeyUp, ReleaseStateBeforeKeyUp: releaseStateBeforeKeyUp, FollowPrivateFocus:followPrivateFocus,PreciseChordHoldMilliseconds:preciseChordHoldMilliseconds)); }
    }
    public long GetFocusedWindow(long root) { lock (gate) return CallLocked(new("focus", Window: root)).GetInt64(); }
    internal void ResearchMenuCharacter(long window, char character, bool posted = false, ControlOwner sender = ControlOwner.Automation)
    {
        if (character is < 'a' or > 'z') throw new ArgumentException("Use one lowercase menu mnemonic.");
        lock (gate) { CheckOwner(sender); CallLocked(new("research-menu-character", Window: window, Key: character, Posted: posted)); }
    }
    internal JsonElement ObserveWindowThread(long window) { lock (gate) return CallLocked(new("thread-observation", Window: window)); }
    internal JsonElement ObserveMenu(long window) { lock (gate) return CallLocked(new("menu-observation", Window: window)); }
    internal void ResearchResizeWindow(long window, int width, int height)
    { lock (gate) { CheckOwner(ControlOwner.Automation); CallLocked(new("research-resize-window", Window: window, X: width, Y: height)); } }
    internal void ResearchOpenSystemMenu(long window, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("research-system-menu", Window: window)); } }
    public void InvokeAccessibleAction(long root, string name, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("invoke", Window: root, Text: name)); } }
    // Semantic research adapter, distinct from keyboard event delivery.
    internal void ResearchSetAccessibleValue(long window, string value, ControlOwner sender = ControlOwner.Automation)
    {
        if (value.Length > 4096) throw new ArgumentException("Bound accessible research value to 4096 characters.");
        lock (gate) { CheckOwner(sender); CallLocked(new("research-accessible-value", Window: window, Text: value)); }
    }
    public void SendMouseEvent(long window, MouseMessage message, int x, int y, int buttons = 0, int wheelDelta = 0, bool posted = false, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("mouse", Window: window, X: x, Y: y, Message: (uint)message, Buttons: buttons, Delta: wheelDelta, Posted: posted)); } }
    public void SendMouseInput(long window, int x, int y, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate) { CheckOwner(sender); CallLocked(new("click", Window: window, X: x, Y: y)); }
    }
    public JsonElement SendQueueMouseSequence(long window, MouseStep[] steps, bool primeCapture = false, ControlOwner sender = ControlOwner.Automation, bool posted = false, bool warmupCapture = false, bool pacedMouse = false)
    {
        if (steps.Length is < 1 or > 32 || steps[^1].Buttons != 0 || steps.Any(s => !Enum.IsDefined(s.Message) || s.Buttons < 0 || (s.Buttons & ~0x1f) != 0)) throw new ArgumentException("Use 1–32 bounded mouse steps ending with no held buttons.");
        if (warmupCapture && (!primeCapture || posted || steps[0].Message != MouseMessage.Move)) throw new ArgumentException("Warm-up capture requires synchronous delivery beginning with a move.");
        lock (gate) { CheckOwner(sender); return CallLocked(new("queue-mouse", Window: window, Steps: steps, PrimeCapture: primeCapture, Posted: posted, WarmupCapture: warmupCapture, PacedMouse: pacedMouse)); }
    }
    public (int Start, int End) GetControlSelection(long window)
    { lock (gate) { var value = CallLocked(new("selection", Window: window)); return (value.GetProperty("start").GetInt32(), value.GetProperty("end").GetInt32()); } }
    public void SelectControlText(long window, int start, int end, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("select-control", Window: window, X: start, Y: end)); } }
    public int GetScrollPosition(long window, int bar = 2) { lock (gate) return CallLocked(new("scroll-position", Window: window, Key: bar)).GetInt32(); }
    public void NotifyScrollControl(long parent, long scrollbar, int requestCode, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("scroll-control", Window: parent, TargetWindow: scrollbar, Key: requestCode)); } }
    public void CancelOwnedMenu(long root, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) { CheckOwner(sender); CallLocked(new("cancel-menu", Window: root)); } }
    public (long Window, int X, int Y) HitTest(long root, int x, int y)
    {
        lock (gate) { var v = CallLocked(new("hit", Window: root, X: x, Y: y)); return (v.GetProperty("window").GetInt64(), v.GetProperty("x").GetInt32(), v.GetProperty("y").GetInt32()); }
    }
    internal (long Window, int X, int Y) HitTestPointer(long root, int x, int y)
    { lock (gate) { var v = CallLocked(new("pointer-hit", Window: root, X: x, Y: y)); return (v.GetProperty("window").GetInt64(), v.GetProperty("x").GetInt32(), v.GetProperty("y").GetInt32()); } }
    internal PopupSurface[] GetPopupSurfaces(long root)
    { lock (gate) return CallLocked(new("popup-surfaces", Window: root)).Deserialize<PopupSurface[]>()!; }
    internal PopupPresentation CaptureWithPopups(long root)
    { lock (gate) return CallLocked(new("capture-popups", Window: root)).Deserialize<PopupPresentation>()!; }
    internal (CaptureFrame Frame, PopupSurface[] Surfaces, Bitmap Image) CaptureSharedPreview(long root, bool direct = false, bool repaint = false, bool windowDc = false)
    {
        lock (gate)
        {
            var clock = Stopwatch.StartNew();
            if(windowDc&&(!direct||repaint))throw new ArgumentException("DC preview requires direct capture without repaint.");
            var descriptor = CallLocked(new(repaint?"capture-popups-repaint":direct ? "capture-popups-direct" : "capture-popups-shared", Window: root, Text: sharedPreview.Channel,Message:windowDc?1u:0u)).Deserialize<SharedPreview>()!;
            var image = sharedPreview.Read(descriptor);
            return (new([], descriptor.Width, descriptor.Height, clock.Elapsed.TotalMilliseconds, (windowDc?"Window DC":"PrintWindow")+" / native owned popup composition / " + (direct ? "direct mapped pixels" : "shared pixels"), NativeRenderingMilliseconds: descriptor.NativeMilliseconds, EncodingMilliseconds: 0,SourceClientX:descriptor.ClientX,SourceClientY:descriptor.ClientY), descriptor.Surfaces, image);
        }
    }
    internal async Task<(CaptureFrame Frame,PopupSurface[] Surfaces,Bitmap Image)> CaptureSharedPreviewAsync(long root,bool softwareClientOnly=false,bool windowDc=false)
    {
        await previewRequests.WaitAsync().ConfigureAwait(false);
        var clock=Stopwatch.StartNew();
        try
        {
            long ticket;EventWaitHandle ready;
            lock(gate)
            {
                ObjectDisposedException.ThrowIf(closed,this);
                previewReady??=new EventWaitHandle(false,EventResetMode.ManualReset,asynchronousPreview.Channel+"_ready");
                ready=previewReady;ready.Reset();
                ticket=CallLocked(new("begin-preview",Window:root,Text:asynchronousPreview.Channel,Key:softwareClientOnly?1:3,Message:windowDc?1u:0u)).GetInt64();
            }
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration=ThreadPool.RegisterWaitForSingleObject(ready,(_,timedOut)=>completion.TrySetResult(!timedOut),null,5000,true);
            bool signaled;
            try{signaled=await completion.Task.ConfigureAwait(false);}
            finally{registration.Unregister(null);}
            lock(gate)
            {
                if(!signaled){if(PreserveOnWorkerFault){SuspendWorker("Independent preview exceeded 5000 ms; application retained.");throw new LayerSuspendedException(workerFault!);}Dispose();throw new TimeoutException("Independent preview exceeded 5000 ms; the owned layer was terminated to contain the stall.");}
                var poll=CallLocked(new("poll-preview",PreviewTicket:ticket)).Deserialize<PreviewPoll>()!;
                if(!poll.Ready||poll.Ticket!=ticket||poll.Frame==null)throw new InvalidDataException("Signaled preview has no matching completed descriptor.");
                var descriptor=poll.Frame;var image=asynchronousPreview.Read(descriptor);
                return(new([],descriptor.Width,descriptor.Height,clock.Elapsed.TotalMilliseconds,(windowDc?"Window DC":"PrintWindow")+" / independent private preview / "+(windowDc?"client copy":softwareClientOnly?"software client":"full content")+" / direct mapped pixels",NativeRenderingMilliseconds:descriptor.NativeMilliseconds,EncodingMilliseconds:0,SourceClientX:descriptor.ClientX,SourceClientY:descriptor.ClientY),descriptor.Surfaces,image);
            }
        }
        finally{previewRequests.Release();}
    }
    internal JsonElement ObserveTextDragTarget(long root, Point first, Point last)
    { lock (gate) return CallLocked(new("text-hit", Window: root, Steps: [new(MouseMessage.Move, first.X, first.Y), new(MouseMessage.Move, last.X, last.Y)])); }
    internal JsonElement ObserveTextGeometry(long root)
    { lock (gate) return CallLocked(new("text-geometry", Window: root)); }
    internal JsonElement ObservePointerGeometry(long root, Point first, Point last,string? preferredTextAutomationId=null,long scopeWindow=0)
    { lock (gate) return CallLocked(new("pointer-geometry", Window: root, TargetWindow:scopeWindow,Text:preferredTextAutomationId,Steps: [new(MouseMessage.Move, first.X, first.Y), new(MouseMessage.Move, last.X, last.Y)])); }
    internal void ResearchStallAfterInput(long window,string text)
    {lock(gate){if(!PreserveOnWorkerFault)throw new InvalidOperationException("Use a disposable durable verification layer.");CheckOwner(ControlOwner.Automation);inputAttempted=true;inspectionInputEpoch++;CallLocked(new("research-stall-after-text",Window:window,Text:text),500);}}
    internal object PrepareInitialPointer(long root,Point point,bool simulateStall=false,int budgetMilliseconds=350)
    {
        lock(gate)
        {
            if(inputAttempted)throw new InvalidOperationException("Pointer preparation is only allowed before the first input attempt.");
            if(budgetMilliseconds is <100 or >500)throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            int previousWorker=WorkerProcessId;var clock=Stopwatch.StartNew();
            try
            {
                var geometry=CallLocked(new("prime-pointer",Window:root,Key:simulateStall?1:0,Steps:[new(MouseMessage.Move,point.X,point.Y),new(MouseMessage.Move,point.X,point.Y)]),budgetMilliseconds,recoverInitialRead:true);
                return new{completed=true,recovered=false,previousWorker,worker=WorkerProcessId,milliseconds=clock.Elapsed.TotalMilliseconds,geometry,error=(string?)null};
            }
            catch(LayerOperationException error)
            {return new{completed=false,recovered=WorkerProcessId!=previousWorker,previousWorker,worker=WorkerProcessId,milliseconds=clock.Elapsed.TotalMilliseconds,error=error.Message};}
        }
    }
    internal bool CanPrepareInitialPointer{get{lock(gate)return !closed&&!inputAttempted;}}
    internal JsonElement AccessiblePointPress(long root,int x,int y,ControlOwner sender)
    {lock(gate){CheckOwner(sender);return CallLocked(new("accessible-point-input",Window:root,X:x,Y:y));}}
    internal JsonElement EnablePrivateInputSession(bool lowLatency=false,bool preciseMouseWaits=false){lock(gate){CheckOwner(ControlOwner.Automation);var state=CallLocked(new("persistent-private-input",LowLatencyInput:lowLatency,PreciseMouseWaits:preciseMouseWaits));persistentInputRequested=true;lowLatencyInputRequested=lowLatency;preciseMouseWaitsRequested=preciseMouseWaits;PersistKeeper();return state;}}
    internal JsonElement PrivateInputSessionState(){lock(gate)return CallLocked(new("private-input-state"));}
    internal JsonElement ResearchSeedPrivateModifiers(long window,int[] keys){lock(gate){CheckOwner(ControlOwner.Automation);return CallLocked(new("seed-private-modifiers",Window:window,Keys:keys));}}
    public CaptureFrame CaptureVisualOutput(long window, CaptureBackend backend = CaptureBackend.PrintWindow)
    {
        lock (gate)
        {
            var frame = CallLocked(new("capture", Window: window, Backend: backend)).Deserialize<CaptureFrame>()!;
            if (frame.CaptureWorkerProcessId != 0 && Native.JobProcessIds(job).Contains(frame.CaptureWorkerProcessId))
            {
                nint helper = Native.OpenProcess(0x1000, false, (uint)frame.CaptureWorkerProcessId);
                if (helper != 0) { try { if (Native.GetProcessTimes(helper, out long created, out _, out _, out _)) infrastructureIdentity[frame.CaptureWorkerProcessId] = created; } finally { Native.CloseHandle(helper); } }
            }
            return frame;
        }
    }
    public void HideLayer(long root) { lock (gate) CallLocked(new("hide", Window: root)); }
    public CaptureFrame InspectHiddenWindow(long root, bool fullContent = false) => InspectHiddenWindow(root, fullContent, 150);
    public CaptureFrame InspectHiddenWindow(long root, bool fullContent, int settleMilliseconds)
    {
        if (settleMilliseconds is < 0 or > 500) throw new ArgumentException("Bound private inspection settle to 0–500 ms.");
        lock (gate) return CallLocked(new("inspect-hidden", Window: root, Key: fullContent ? 3 : 1, SettleMilliseconds: settleMilliseconds)).Deserialize<CaptureFrame>()!;
    }
    // Bounded, serialized native input with temporary presentation on this private desktop.
    // A settle interval is not an application processing acknowledgement; inspect results.
    public PresentedInteraction PresentForInput(long root, PresentedInput[] inputs, bool captureAfter = true, bool fullContent = false, int settleMilliseconds = 150, ControlOwner sender = ControlOwner.Automation, bool notifyActivation = false)
    {
        PresentedInputValidation.Check(inputs, settleMilliseconds);
        lock (gate) { CheckOwner(sender); return CallLocked(new("presented-input", Window: root, Inputs: inputs, CaptureAfter: captureAfter, Key: fullContent ? 3 : 1, SettleMilliseconds: settleMilliseconds, NotifyActivation: notifyActivation)).Deserialize<PresentedInteraction>()!; }
    }
    // Text commands only: targeted activation notifications, no wrapper show/capture.
    // Compatibility depends on the application's handling of a hidden activation.
    public PresentedInteraction SendActivatedInput(long root, PresentedInput[] inputs, ControlOwner sender = ControlOwner.Automation)
    {
        PresentedInputValidation.Check(inputs, 0);
        if (inputs.Any(input => input.Kind != PresentedInputKind.Text)) throw new ArgumentException("Input without presentation currently supports text commands only; use PresentForInput for chords and pointer interactions.");
        lock (gate) { CheckOwner(sender); return CallLocked(new("activated-input", Window: root, Inputs: inputs)).Deserialize<PresentedInteraction>()!; }
    }
    internal CaptureFrame CaptureResearch(long window, string mode)
    { lock (gate) { CheckOwner(ControlOwner.Automation); return CallLocked(new("capture-research", Window: window, Text: mode)).Deserialize<CaptureFrame>()!; } }
    internal HiddenCaptureTrial ResearchHiddenCapture(long root, string strategy, string mode, int settleMilliseconds = 150, long targetWindow = 0)
    { lock (gate) { CheckOwner(ControlOwner.Automation); return CallLocked(new("hidden-capture-research", Window: root, TargetWindow: targetWindow, Text: strategy + ":" + mode, SettleMilliseconds: settleMilliseconds)).Deserialize<HiddenCaptureTrial>()!; } }
    internal (int X, int Y) ClientOrigin(long window)
    { lock (gate) { var value = CallLocked(new("client-origin", Window: window)); return (value.GetProperty("X").GetInt32(), value.GetProperty("Y").GetInt32()); } }
    internal (int Width,int Height) ClientSize(long window)
    {lock(gate){var value=CallLocked(new("client-size",Window:window));return(value.GetProperty("Width").GetInt32(),value.GetProperty("Height").GetInt32());}}
    internal JsonElement ObserveAccessible(long window, string? automationId = null)
        => ObservePassive(window,automationId,false);
    JsonElement ObservePassive(long window,string? automationId,bool menus)
    {
        PassiveInspection service; long ticket;
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(closed,this);
            service = passiveInspection ??= new PassiveInspection(Id);
            if(legacyInspectionTicket is {} previous&&(legacyInspectionEpoch!=inspectionInputEpoch||legacyInspectionOwnership!=ownershipRevision))
            {
                if(!service.Finished(previous))
                    return JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),errors=Array.Empty<string>(),truncated=true,inspectionStatus="stale",window,note="Input or ownership changed during this read; old nodes are withheld."});
                legacyInspectionTicket=null;
            }
            if(legacyInspectionTicket is {} existing && legacyInspectionTarget == (window,automationId,menus)) ticket=existing;
            else
            {
                if(legacyInspectionTicket is {} pending && !service.Finished(pending))
                    return JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),errors=Array.Empty<string>(),truncated=true,inspectionStatus="busy",window,note="Another passive read is pending. No stale nodes from that target are substituted."});
                try{ticket=service.Begin(new(menus?"accessible-menu-observation":"accessible-observation",Window:window,Text:automationId,AllowedProcessIds:Native.JobProcessIds(job)));}
                catch(InspectionBusyException){return JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),errors=Array.Empty<string>(),truncated=true,inspectionStatus="busy",window});}
                legacyInspectionTicket=ticket;legacyInspectionTarget=(window,automationId,menus);legacyInspectionEpoch=inspectionInputEpoch;legacyInspectionOwnership=ownershipRevision;
            }
        }
        var sample=service.Sample(ticket,100);
        lock(gate)
        {
            bool stale=legacyInspectionEpoch!=inspectionInputEpoch||legacyInspectionOwnership!=ownershipRevision;
            if(service.Finished(ticket)&&legacyInspectionTicket==ticket) legacyInspectionTicket=null;
            if(stale)return JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),errors=Array.Empty<string>(),truncated=true,inspectionStatus="stale",window,note="Input or ownership changed during this read; old nodes are withheld."});
        }
        return sample;
    }
    internal long BeginAccessibleInspection(long window,string? automationId=null,bool allVisibleMatches=false)
    { lock(gate) { ObjectDisposedException.ThrowIf(closed,this); return (passiveInspection??=new PassiveInspection(Id)).Begin(new("accessible-observation",Window:window,Text:automationId,Key:allVisibleMatches?1:0,AllowedProcessIds:Native.JobProcessIds(job))); } }
    internal long BeginAccessibleMenuInspection(long window)
    { lock(gate) { ObjectDisposedException.ThrowIf(closed,this); return (passiveInspection??=new PassiveInspection(Id)).Begin(new("accessible-menu-observation",Window:window,AllowedProcessIds:Native.JobProcessIds(job))); } }
    internal JsonElement PollAccessibleInspection(long ticket)
    { lock(gate) { ObjectDisposedException.ThrowIf(closed,this); return (passiveInspection??throw new InvalidOperationException("No inspection has started")).Poll(ticket); } }
    internal long BeginScopedPointInspection(long root,int x,int y)
    {lock(gate){ObjectDisposedException.ThrowIf(closed,this);return (pointInspection??=new PassiveInspection(Id)).Begin(new("scoped-accessible-point",Window:root,X:x,Y:y,AllowedProcessIds:Native.JobProcessIds(job)));}}
    internal JsonElement PollScopedPointInspection(long ticket)
    {lock(gate){ObjectDisposedException.ThrowIf(closed,this);return (pointInspection??throw new InvalidOperationException("No point inspection has started")).Poll(ticket);}}
    internal JsonElement ObserveAccessibleMenus(long window)
        => ObservePassive(window,null,true);
    internal JsonElement ResearchActivation(long root, string mode)
    { lock (gate) { CheckOwner(ControlOwner.Automation); return CallLocked(new("research-activation", Window: root, Text: mode)); } }
    // Explicit research backend: framework visibility differs from native visibility
    // only during this serialized transaction. Ordinary viewer paths never select it.
    internal PresentedInteraction ResearchHiddenInteraction(long root, PresentedInput[] inputs, bool captureAfter = false, bool fullContent = true, int settleMilliseconds = 0, ControlOwner sender = ControlOwner.Automation)
    {
        PresentedInputValidation.Check(inputs, settleMilliseconds);
        lock (gate) { CheckOwner(sender); return CallLocked(new("notification-input", Window: root, Inputs: inputs, CaptureAfter: captureAfter, Key: fullContent ? 3 : 1, SettleMilliseconds: settleMilliseconds)).Deserialize<PresentedInteraction>()!; }
    }
    public void ShowLayer(long root) { lock (gate) CallLocked(new("show", Window: root)); }
    public void RequestAttention(string reason) { lock (gate) AttentionReason = reason; }
    public void TakeHumanControl() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); if (owner != ControlOwner.Human){PersistKeeper(ControlOwner.Human,ownershipRevision+1);ownershipRevision++;} owner = ControlOwner.Human; } }
    public void ReturnToAutomation() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); if (owner != ControlOwner.Automation){PersistKeeper(ControlOwner.Automation,ownershipRevision+1);ownershipRevision++;} owner = ControlOwner.Automation; AttentionReason = null; } }
    internal long BeginVisualObservation(ControlOwner sender)
    { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); CheckOwner(sender); return ownershipRevision; } }
    internal void ValidateVisualObservation(long revision)
    { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); if (revision != ownershipRevision) throw new InvalidOperationException("Visual observation interrupted by input ownership change."); } }
    public int[] GetProcessIds() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); return Native.JobProcessIds(job).Concat(workerJob==0?[]:Native.JobProcessIds(workerJob)).Concat(keeper?.IsAlive==true?new[]{keeper.Pid}:[]).Distinct().ToArray(); } }
    // Controlled research only. All operations share the normal layer command gate.
    internal T ResearchTransaction<T>(ControlOwner? sender, Func<T> action)
    { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); if (sender != null) CheckOwner(sender.Value); return action(); } }
    internal Process StartGpuResearchHelper(bool dynamic = false, bool qiv = false)
        => StartResearchHelper(qiv ? "artifacts/gpu-qiv-build/GpuResearchHost.exe" : dynamic ? "artifacts/gpu-dynamic-build/GpuResearchHost.exe" : "artifacts/gpu-research-build/GpuResearchHost.exe");
    internal Process StartQivGuardHelper() => StartResearchHelper("artifacts/gpu-qiv-build/QivGuardHost.exe");
    Process StartResearchHelper(string relative)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            string exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relative));
            var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            try
            {
                Native.Check(Native.AssignProcessToJobObject(job, process.Handle), "Own disposable research helper before commands");
                Native.Check(Native.GetProcessTimes(process.Handle, out long created, out _, out _, out _), "Identify research helper"); infrastructureIdentity[process.Id] = created;
                return process;
            }
            catch { if (!process.HasExited) process.Kill(); process.Dispose(); throw; }
        }
    }
    void RefreshInfrastructure(int[] members)
    {
        var identities = ProcessCensus.Read(members);
        string console = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        foreach (var entry in identities)
        {
            if (entry.Pid == ApplicationProcessId) continue;
            if (entry.CreationFileTime == null || !string.Equals(entry.ImagePath, console, StringComparison.OrdinalIgnoreCase)) continue;
            bool parentIdentity = infrastructureIdentity.TryGetValue(entry.ParentPid, out long trustedCreated) && entry.CreationFileTime >= trustedCreated && identities.Any(parent => parent.Pid == entry.ParentPid && parent.CreationFileTime == trustedCreated);
            if (!parentIdentity && workerLifetimes.TryGetValue(entry.ParentPid, out var worker) && worker.Exited != null) parentIdentity = entry.CreationFileTime >= worker.Created && entry.CreationFileTime <= worker.Exited;
            if (parentIdentity) infrastructureIdentity[entry.Pid] = entry.CreationFileTime.Value;
        }
        // A PID alone can be reused. Unknown processes stay application-owned; only an
        // explicitly registered worker/helper or its verified system console is excluded.
        infrastructure = identities.Where(entry => entry.CreationFileTime != null && infrastructureIdentity.TryGetValue(entry.Pid, out long created) && created == entry.CreationFileTime).Select(entry => entry.Pid).ToArray();
    }
    public int[] GetInfrastructureProcessIds() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); RefreshInfrastructure(Native.JobProcessIds(job)); return infrastructure.ToArray(); } }
    public bool CloseGracefully(TimeSpan timeout)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            TakeHumanControl(); // Persist the handoff before a save prompt can outlive this controller.
            foreach (var root in GetWindows().Where(w => w.Parent == 0 && w.Owner == 0 && w.Width > 100 && w.Height > 50).OrderByDescending(w => w.Visible))
            {
                // Do not send WM_CLOSE to owned modal/save popups; let the application decide.
                try { CallLocked(new("close", Window: root.Handle)); }
                catch (LayerOperationException e) when (e.Message == "Window no longer exists.") { /* Application closed between enumeration and request. */ }
                break;
            }
        }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            lock (gate)
            {
                if (closed) return true;
                int[] members = Native.JobProcessIds(job); RefreshInfrastructure(members);
                if (members.Except(infrastructure).Count() == 0) { Dispose(); return true; }
            }
            Thread.Sleep(50);
        }
        return false; // Save prompt/hung application preserved; no implicit termination.
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;
            passiveInspection?.Dispose(); passiveInspection=null;
            pointInspection?.Dispose();pointInspection=null;
            // Durable disposal detaches; explicit Destroy ends the application.
            keeperWatch?.Dispose();keeperWatch=null;ReleaseKeeper();keeper?.Dispose();
            if (job != 0) { Native.CloseHandle(job); job = 0; }
            DisposeConnection();
            if (desktop != 0) { Native.CloseDesktop(desktop); desktop = 0; }
        }
    }
    void DisposeConnection()
    {
        if(workerJob!=0){Native.CloseHandle(workerJob);workerJob=0;} // Kills control/capture workers, never the application job.
        sharedPreview.Dispose();
        asynchronousPreview.Dispose();
        previewReady?.Set(); // Wake a capture interrupted by close or restart.
        if(closed){previewReady?.Dispose();previewReady=null;}
        try { writer?.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        reader?.Dispose(); pipe?.Dispose(); writer = null; reader = null; pipe = null;
    }
}
public sealed class LayerOperationException(string message) : Exception(message);

public sealed class LayerSuspendedException(string message):Exception(message);

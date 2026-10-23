using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Optional adapter around the existing viewer/layer. No physical input APIs.
internal sealed partial class DemoForm
{
    readonly Dictionary<long,(long Root,long Revision,long Input)> agentInspectionContexts=[];
    (string Token,long Revision,long Input,long Root)? agentRecoveryObservation;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AgentBackgroundStart {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AgentDurableLayer {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AgentFaultVerification {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? AgentRecoveredKeeper {get;set;}
    internal void AgentFinishStartup(){preview=false;}
    internal void AgentDestroyJob()=>layer?.Destroy(); // Layer lock is independent of the viewer STA.
    internal async Task AgentAbortAsync(bool force=true){automation.Stop();await Task.Run(()=>{if(force)layer?.Destroy();else layer?.Dispose();});allowClose=true;Close();}
    protected override bool ShowWithoutActivation=>AgentBackgroundStart||base.ShowWithoutActivation;
    internal object AgentState()
    {
        bool closed=disposed||layer==null||layer.Closed;int[] owned=closed?[]:layer!.GetProcessIds();
        return new{keeperReady=layer?.KeeperReady,keeperDescriptor=layer?.KeeperDescriptor,keeperPid=layer?.KeeperPid,workerFaulted=layer?.WorkerFaulted,recoveryPending=layer?.RecoveryPending,workerFault=layer?.WorkerFault,workerPid=layer?.WorkerProcessId,state=closed?"closed":layer!.WorkerFaulted?"paused_worker_fault":!layer.KeeperReady?"paused_keeper_fault":layer.RecoveryPending?"paused_reconciliation":!owned.Contains(layer!.ApplicationProcessId)?"application_root_exited":inputTarget==0?"no_surface":"ready",layerId=layer?.Id,applicationPid=layer?.ApplicationProcessId,ownedProcessIds=owned,owner=layer?.Owner.ToString(),revision=layer?.OwnershipRevision,root,inputSequence,previewVersion,viewerVisible=Visible,previewVisible=preview,attention=layer?.AttentionReason};
    }

    // Runs on this form's STA, using the same command gate as human actions.
    // Unlike toolbar Execute, errors must reach the automation caller.
    internal async Task<JsonElement> AgentExecuteAsync(JsonElement request,AgentArtifacts artifacts)
    {
        await commands.WaitAsync();bool inputStarted=false;
        try
        {
            if(disposed||layer==null||layer.Closed)throw new AgentException("session_closed","The owned application session is closed.");
            string op=request.GetProperty("op").GetString()!;
            void Revision()
            {
                if(!request.TryGetProperty("expectedRevision",out var value)||value.GetInt64()!=layer.OwnershipRevision)
                    throw new AgentException("stale_revision","Observe the current session revision before input or an ownership change.");
            }
            void Automation(){Revision();if(layer.Owner!=ControlOwner.Automation)throw new AgentException("human_owns_input","Human control currently owns this application.");if(AgentDurableLayer&&(layer.WorkerFaulted||layer.RecoveryPending||!layer.KeeperReady))throw new AgentException("layer_suspended","Recover and reconcile this retained layer before starting new input.");}
            void CancelPointer(){ownershipEpoch++;pointerGesture.Clear();pendingHover=null;canvas.Capture=false;pendingTextBatch=null;automation.Stop();}
            object? result=null;
            switch(op)
            {
                case "status":break;
                case "research-stop-keeper":
                    if(!AgentDurableLayer||!AgentFaultVerification)throw new AgentException("unsupported_operation","Use only the disposable verification owner.");
                    Revision();await Task.Run(layer.ResearchStopKeeper);result=new{injected=true};break;
                case "research-close-viewer":
                    if(!AgentDurableLayer||!AgentFaultVerification)throw new AgentException("unsupported_operation","Use only the disposable verification owner.");
                    Close();result=new{viewerCloseRequested=true};break;
                case "research-stall-owner-ui":
                    if(!AgentDurableLayer||!AgentFaultVerification)throw new AgentException("unsupported_operation","Use only the disposable verification owner.");
                    Thread.Sleep(Timeout.Infinite);break;
                case "research-stall-after-input":
                    if(!AgentDurableLayer||!AgentFaultVerification)throw new AgentException("unsupported_operation","Fault injection is only available in the disposable verification owner.");
                    Automation();string probe=request.GetProperty("text").GetString()!;if(probe.Length is <1 or >64)throw new AgentException("invalid_request","Bound disposable fault text.");
                    inputStarted=true;await Task.Run(()=>layer.ResearchStallAfterInput(request.GetProperty("window").GetInt64(),probe));break;
                case "repair-keeper":
                    Revision();if(!AgentDurableLayer)throw new AgentException("unsupported_operation","Use a durable layer.");await Task.Run(layer.RepairKeeper);result=new{keeperReady=layer.KeeperReady};break;
                case "recover":
                    Revision();if(!AgentDurableLayer)throw new AgentException("unsupported_operation","Recovery requires a durable layer owner.");
                    if(!layer.WorkerFaulted)throw new AgentException("invalid_state","Only a suspended worker may be recovered.");
                    CancelPointer();await Task.Run(layer.RestartWorker);agentRecoveryObservation=null;
                    result=new{recovered=true,delivery="No input replayed",requiresReconciliation=true};break;
                case "reconcile":
                    Revision();if(!AgentDurableLayer||!layer.RecoveryPending||layer.WorkerFaulted)throw new AgentException("invalid_state","Recover and observe the paused layer first.");
                    if(layer.Owner!=ControlOwner.Automation)throw new AgentException("human_owns_input","Reconciliation cannot take Human ownership.");
                    if(agentRecoveryObservation is not {} proof||proof.Token!=request.GetProperty("observationToken").GetString()||proof.Revision!=layer.OwnershipRevision||proof.Input!=inputSequence||proof.Root!=root||!AgentHost.Bool(request,"continue"))throw new AgentException("stale_observation","Explicitly reconcile a fresh post-recovery observation before continuing.");
                    layer.AcceptRecovery();agentRecoveryObservation=null;result=new{resumed=true,note="Caller accepted observed application state; previous input was not replayed."};break;
                case "inspect-begin":
                    if(root==0||inputTarget==0)throw new AgentException("no_surface","Observe an owned surface before inspection.");
                    string? automationId=request.TryGetProperty("automationId",out var selectedId)?selectedId.GetString():null;
                    if(automationId!=null&&automationId.Length is <1 or >256)throw new AgentException("invalid_request","Use an automation ID of 1–256 characters.");
                    long inspectionTicket;
                    try{inspectionTicket=layer.BeginAccessibleInspection(root,automationId);}
                    catch(InvalidOperationException error){throw new AgentException("inspection_busy",error.Message);}
                    agentInspectionContexts.TryAdd(inspectionTicket,(root,layer.OwnershipRevision,inputSequence));
                    while(agentInspectionContexts.Count>32)agentInspectionContexts.Remove(agentInspectionContexts.Keys.Min());
                    var startedContext=agentInspectionContexts[inspectionTicket];
                    result=new{ticket=inspectionTicket,status="pending",context=new{root=startedContext.Root,revision=startedContext.Revision,inputSequence=startedContext.Input}};break;
                case "inspect-poll":
                    long polledTicket=request.GetProperty("ticket").GetInt64();
                    if(!agentInspectionContexts.TryGetValue(polledTicket,out var context))throw new AgentException("invalid_ticket","Inspection ticket does not belong to this session or has expired.");
                    result=new{inspection=layer.PollAccessibleInspection(polledTicket),stale=context.Root!=root||context.Revision!=layer.OwnershipRevision||context.Input!=inputSequence,
                        context=new{root=context.Root,revision=context.Revision,inputSequence=context.Input},note="Passive sampled evidence; a completed read is not an input completion guarantee. Changed input, ownership or surface marks it stale."};break;
                case "surface":
                    Automation();long selected=request.GetProperty("window").GetInt64();
                    var surfacesNow=await Task.Run(layer.GetPresentationWindows);
                    if(!surfacesNow.Any(w=>w.Handle==selected&&w.Parent==0&&w.Visible&&w.Enabled&&w.Width>100&&w.Height>50&&w.ClassName!="#32768"&&!string.IsNullOrWhiteSpace(w.Text)))
                        throw new AgentException("invalid_target","Select a visible, enabled, titled top-level window owned by this session.");
                    nativeSurfaceRoot=preferredSurface=selected;await RefreshCoreAsync(false);
                    result=new{selected,inspectedRoot=root};break;
                case "observe":
                    bool wasPreview=preview;preview=true;
                    try{await RefreshCoreAsync(false,onlyWindowChange:inputTarget!=0&&!AgentHost.Bool(request,"capture")&&!AgentHost.Bool(request,"accessibility"),detailed:AgentHost.Bool(request,"accessibility"));}finally{preview=wasPreview;}
                    var list=await Task.Run(layer.GetPresentationWindows);
                    if(inputTarget==0)throw new AgentException("no_surface","There is no unambiguous usable owned window. Inspect status or observe again after the application changes.");
                    var origin=root==0?(X:0,Y:0):await Task.Run(()=>layer.ClientOrigin(root));
                    string? accessibility=null;object? image=null;
                    if(AgentHost.Bool(request,"accessibility"))
                    {
                        string file=Path.Combine(reportDirectory!,"viewer-accessible.json");
                        accessibility=await artifacts.WriteAsync("accessibility","json",await File.ReadAllBytesAsync(file));
                    }
                    if(AgentHost.Bool(request,"capture"))
                    {
                        if(canvas.Image==null)throw new AgentException("capture_unavailable","The application has no captured surface.");
                        using var bitmap=new Bitmap(canvas.Image);
                        byte[] bytes=await Task.Run(()=>{using var memory=new MemoryStream();bitmap.Save(memory,ImageFormat.Png);return memory.ToArray();});
                        string path=await artifacts.WriteAsync("frame","png",bytes);
                        image=new{path,sha256=Convert.ToHexString(SHA256.HashData(bytes)),width=bitmap.Width,height=bitmap.Height,coordinates="source client pixels"};
                    }
                    string? recoveryToken=null;
                    if(AgentDurableLayer&&layer.RecoveryPending){recoveryToken=Guid.NewGuid().ToString("N");agentRecoveryObservation=(recoveryToken,layer.OwnershipRevision,inputSequence,root);}
                    result=new{recoveryToken,windows=list.OrderBy(w=>w.Handle==root?0:w.Parent==0?1:2).Take(256).Select(w=>w with{Text=w.Text[..Math.Min(w.Text.Length,256)],ClassName=w.ClassName[..Math.Min(w.ClassName.Length,128)]}).ToArray(),truncated=list.Length>256,windowTitleLimit=256,sourceClientOrigin=new{origin.X,origin.Y},image,accessibilityPath=accessibility,capturedInputSequence=inputSequence,note="Observation is sampled application state; titles are bounded USER captions. Request accessibility for control text. Input delivery is not application completion."};break;
                case "input":
                    Automation();
                    if(AgentDurableLayer&&(!request.TryGetProperty("expectedInputSequence",out var observedInput)||observedInput.GetInt64()!=inputSequence))throw new AgentException("stale_observation","Act only on a fresh observation of this layer's input sequence.");
                    long target=request.GetProperty("window").GetInt64();
                    string validationMode=request.TryGetProperty("validationMode",out var validation)?validation.GetString()!:"full-window-scan";
                    if(validationMode is not ("full-window-scan" or "target-subtree"))throw new AgentException("invalid_request","Unknown input validation mode. Use full-window-scan or target-subtree.");
                    var validationClock=System.Diagnostics.Stopwatch.StartNew();
                    // Keep the proven synchronous interrogation by default. A
                    // presentation-only replacement lost rapid VS edits in the
                    // hidden session experiment; its exact cause is not proven.
                    WindowInfo[] owned;
                    try{owned=await Task.Run(()=>validationMode=="target-subtree"?layer.GetInputSubtree(target):layer.GetWindows());}
                    catch(LayerOperationException error)when(validationMode!="full-window-scan"){throw new AgentException("invalid_target",error.Message);}
                    double validationMilliseconds=validationClock.Elapsed.TotalMilliseconds;
                    var window=owned.SingleOrDefault(w=>w.Handle==target);
                    if(window==null||!window.Visible||!window.Enabled)throw new AgentException("invalid_target","Input requires a currently visible, enabled window in this owned session.");
                    long parent=window.Parent;var ancestors=new HashSet<long>();
                    while(parent!=0)
                    {
                        var ancestor=owned.SingleOrDefault(w=>w.Handle==parent);
                        if(!ancestors.Add(parent)||ancestor==null||!ancestor.Enabled||!ancestor.Visible)throw new AgentException("invalid_target","The target's current parent hierarchy is unavailable or disabled by a dialog.");
                        parent=ancestor.Parent;
                    }
                    string kind=request.GetProperty("kind").GetString()!;
                    if(kind=="text")
                    {
                        string value=request.GetProperty("text").GetString()!;
                        if(value.Length is <1 or >4096)throw new AgentException("invalid_request","Text must contain 1–4096 UTF-16 code units.");
                        inputStarted=true;await Task.Run(()=>layer.PostKeyboardInput(target,value,followPrivateFocus:true,textControlKeys:AgentDurableLayer));
                    }
                    else if(kind=="key")
                    {
                        int key=request.GetProperty("key").GetInt32();if(key is <1 or >255)throw new AgentException("invalid_request","Use a valid Windows virtual key.");
                        inputStarted=true;
                        if(key is >=0x70 and <=0x87&&key is not (0x78 or 0x79))
                            await Task.Run(()=>layer.SendQueueStateChord(target,[key],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true));
                        else await Task.Run(()=>layer.PostKey(target,key,followPrivateFocus:true));
                    }
                    else if(kind=="chord")
                    {
                        int[] keys=request.GetProperty("keys").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                        if(keys.Length is <1 or >8||keys.Any(k=>k is <1 or >255))throw new AgentException("invalid_request","Use 1–8 valid Windows virtual keys.");
                        // Preserve tested pacing for transitions, including F7.
                        int hold=keys.Any(k=>k is >=0x70 and <=0x87)?0:chordHoldPolicy;
                        inputStarted=true;await Task.Run(()=>layer.SendQueueStateChord(target,keys,posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:hold));
                    }
                    else if(kind=="click")
                    {
                        int x=request.GetProperty("x").GetInt32(),y=request.GetProperty("y").GetInt32();
                        string deliveryMode=request.TryGetProperty("deliveryMode",out var mode)?mode.GetString()!:"synchronous";
                        if(deliveryMode is not ("synchronous" or "posted"))throw new AgentException("invalid_request","Click deliveryMode must be synchronous or posted.");
                        var bounds=await Task.Run(()=>layer.ClientSize(target));
                        if(x<0||y<0||x>=bounds.Width||y>=bounds.Height||x>32767||y>32767)throw new AgentException("invalid_request","Click must be inside the target client bounds.");
                        inputStarted=true;await Task.Run(()=>layer.SendQueueMouseSequence(target,[new(MouseMessage.Move,x,y),new(MouseMessage.LeftDown,x,y,1),new(MouseMessage.LeftUp,x,y)],primeCapture:true,posted:deliveryMode=="posted"));
                    }
                    else throw new AgentException("unsupported_input","Supported input kinds are text, key, chord and click.");
                    inputSequence++;ScheduleRefresh(true);result=new{delivery="forwarded_unverified",inputSequence,validationMode,validationMilliseconds,note="Observe the actual application before deciding whether to retry."};break;
                case "ownership":
                    Revision();string owner=request.GetProperty("owner").GetString()!;
                    if(owner is not ("Human" or "Automation"))throw new AgentException("invalid_request","Owner must be Human or Automation.");
                    CancelPointer();desiredOwner=Enum.Parse<ControlOwner>(owner);
                    if(desiredOwner==ControlOwner.Human)await Task.Run(layer.TakeHumanControl);else await Task.Run(layer.ReturnToAutomation);
                    SetStatus(desiredOwner==ControlOwner.Human?"Human owns input. Inspect and interact with this running application.":"Automation owns input; picture is read-only.");break;
                case "attention":
                    string reason=request.GetProperty("reason").GetString()!;
                    if(string.IsNullOrWhiteSpace(reason)||reason.Length>1024)throw new AgentException("invalid_request","Supply an attention reason of 1–1024 characters.");
                    layer.RequestAttention(reason);SetStatus("Attention requested: "+reason);break;
                case "viewer":
                    if(AgentHost.Bool(request,"visible")){Show();preview=true;await RefreshAsync();}else{Hide();preview=false;canvas.Image?.Dispose();canvas.Image=null;canvas.Invalidate();}
                    break;
                case "close":
                    Revision();CancelPointer();desiredOwner=ControlOwner.Human;
                    bool force=AgentHost.Bool(request,"force");
                    bool closed=force?await Task.Run(()=>{layer.Destroy();return true;}):await Task.Run(()=>layer.CloseGracefully(TimeSpan.FromSeconds(2)));
                    result=new{closed,forced=force,needsHuman=!closed,reason=closed?"closed":layer.GetProcessIds().Contains(layer.ApplicationProcessId)?"application_or_prompt_running":"owned_processes_remaining"};
                    if(closed){allowClose=true;Close();}else{if(AgentDurableLayer){Show();preview=true;}await RefreshAsync();SetStatus("Human owns input. Resolve the application's save prompt, or explicitly force close this disposable session.");}
                    break;
                default:throw new AgentException("unsupported_operation","Unknown session operation.");
            }
            return JsonSerializer.SerializeToElement(new{session=AgentState(),result});
        }
        catch(AgentException){throw;}
        catch(LayerSuspendedException error){throw new AgentException("layer_suspended",error.Message,inputStarted?"unknown":"not_started",error);}
        catch(LayerOperationException error)when(error.Message=="Window no longer exists."&&request.GetProperty("op").GetString()=="observe")
        {throw new AgentException("observation_changed","A window closed while it was being observed. This read-only observation may be repeated.",inner:error);}
        catch(LayerOperationException error)when(request.GetProperty("op").GetString()=="observe"&&error.Message is "Copy owned private client to mapped DIB" or "Owned private window DC unavailable.")
        {throw new AgentException("capture_unavailable","No valid captured frame was obtained. Re-observe the application; this failure does not acknowledge input completion.",inner:error);}
        catch(Exception error){throw new AgentException("operation_failed",error.Message,inputStarted?"unknown":"not_started",error);}
        finally{commands.Release();ArmPreviewDeadline();}
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

internal sealed class AgentException(string code,string message,string outcome="not_started",Exception? inner=null):Exception(message,inner)
{internal string Code=>code;internal string Outcome=>outcome;}

internal sealed class AgentArtifacts(string directory,long maxBytes=64*1024*1024,int maxFiles=256,bool rolling=false)
{
    long bytes;int files;bool initialized;readonly Queue<(string Path,int Bytes)> retained=[];
    string Journal=>Path.Combine(directory,".retention.json");
    void SaveJournal()=>SnapshotFiles.ReplaceAsync(Journal,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retained.Select(p=>new{name=Path.GetFileName(p.Path),bytes=p.Bytes})))).GetAwaiter().GetResult();
    void InitializeStore()
    {
        if(initialized||!rolling)return;
        Directory.CreateDirectory(directory);
        if(File.Exists(Journal))
        {
            if(new FileInfo(Journal).Length>65536)throw new AgentException("artifact_index_invalid","Retention index is oversized; application files are untouched.");
            using var doc=JsonDocument.Parse(SnapshotFiles.ReadText(Journal));
            foreach(var item in doc.RootElement.EnumerateArray())
            {
                string name=item.GetProperty("name").GetString()!;
                if(name!=Path.GetFileName(name)||!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[a-z][a-z0-9-]{0,31}-[a-f0-9]{32}\.(png|json|bin)$"))throw new AgentException("artifact_index_invalid","Invalid retained observation name; no deletion attempted.");
                string path=Path.Combine(directory,name);if(!File.Exists(path))continue;
                long length=new FileInfo(path).Length;if(length>16*1024*1024)throw new AgentException("artifact_index_invalid","Oversized retained observation; no deletion attempted.");
                retained.Enqueue((path,(int)length));bytes+=length;files++;
            }
        }
        else if(Directory.EnumerateFiles(directory).Any(path=>!Path.GetFileName(path).StartsWith(".retention",StringComparison.Ordinal)))throw new AgentException("artifact_index_missing","Existing unindexed files are retained; choose a fresh managed observation store.");
        initialized=true;
    }
    internal async Task<string> WriteAsync(string prefix,string extension,byte[] data)
    {
        InitializeStore();
        if(!System.Text.RegularExpressions.Regex.IsMatch(prefix,@"^[a-z][a-z0-9-]{0,31}$")||extension is not ("png" or "json" or "bin"))throw new ArgumentException("Use a bounded managed observation name.");
        if(rolling&&data.Length<=16*1024*1024&&data.Length<=maxBytes)
            while((data.Length>maxBytes-bytes||files>=maxFiles)&&retained.Count>0)
            {
                var oldest=retained.Peek();
                if(Path.GetDirectoryName(Path.GetFullPath(oldest.Path))!=Path.GetFullPath(directory))throw new InvalidOperationException("Observation rotation escaped its owned directory.");
                try{File.Delete(oldest.Path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){throw new AgentException("artifact_retention_blocked","Owned observation rotation was denied; retained files were not bypassed.",inner:error);}
                retained.Dequeue();bytes-=oldest.Bytes;files--;SaveJournal();
            }
        if(data.Length>16*1024*1024||data.Length>maxBytes-bytes||files>=maxFiles)throw new AgentException("artifact_budget","The host's observation budget is exhausted (maximum 64 MiB / 256 files).");
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,prefix+"-"+Guid.NewGuid().ToString("N")+"."+extension);
        // Reserve in the atomic retention index before creating the file: a
        // process crash cannot strand an unindexed partial observation.
        if(rolling){retained.Enqueue((path,data.Length));bytes+=data.Length;files++;SaveJournal();}
        try{await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,true);await stream.WriteAsync(data);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){throw new AgentException("artifact_write_failed","Observation storage failed; application remains running.",inner:error);}
        if(!rolling){bytes+=data.Length;files++;}return path;
    }
}

// Local JSONL child-process interface. Commands are deliberately serial; owned
// applications, viewers and capture workers continue independently between calls.
internal static class AgentHost
{
    internal static bool Bool(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.True;
    static string Required(JsonElement e,string key)=>e.GetProperty(key).GetString()??throw new AgentException("invalid_request","Missing string: "+key);
    internal static int Run(string output,DurableLayerTransport? transport=null,RecoveredLayerSeed? recovered=null)
    {
        if(transport==null)GpuBridgeVerification.PrepareOutput(output);ApplicationConfiguration.Initialize();
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        var sessions=new Dictionary<string,AgentSession>();var seen=new Dictionary<string,string>();var responses=new Dictionary<string,string>();var order=new Queue<string>();var artifacts=new AgentArtifacts(recovered?.Artifacts??Path.Combine(output,"observations"),rolling:transport!=null);
        string logDirectory=Path.GetDirectoryName(recovered?.Artifacts??Path.Combine(output,"observations"))!;
        long highWater=0;bool shutdownRequested=false,replaceOwnerRequested=false;
        try
        {
            if(recovered!=null)
            {
                var session=new AgentSession(recovered.SessionId,recovered.Request,recovered.ReportDirectory,true,false,recovered.Keeper);
                sessions.Add(recovered.SessionId,session);session.StartAsync().GetAwaiter().GetResult();
            }
            do
            {
            using var connection=transport?.Accept();
            TextReader input=connection?.Reader??Console.In;TextWriter sink=connection?.Writer??Console.Out;
            try{
            while(!shutdownRequested)
            {
                string? line=ReadLine(input);if(line==null)break;
                string? id=null;string op="invalid";string? sessionId=null;string response;bool remember=false;var watch=Stopwatch.StartNew();
                try
                {
                    if(line.Length>65536)throw new AgentException("request_too_large","Requests are limited to 65536 characters.");
                    using var document=JsonDocument.Parse(line);var request=document.RootElement;
                    if(request.ValueKind!=JsonValueKind.Object||request.GetProperty("protocol").GetInt32()!=(transport==null?1:2))throw new AgentException("protocol_version","Use the advertised protocol version.");
                    id=Required(request,"id");if(id.Length is <1 or >128)throw new AgentException("invalid_request","Request IDs must contain 1–128 characters.");
                    op=Required(request,"op");
                    if(transport!=null)
                    {
                        if(id=="0"&&op=="hello")
                        {
                            sink.WriteLine(JsonSerializer.Serialize(new{protocol=2,id,ok=true,data=new{epoch=transport.Epoch,nextSequence=highWater+1,storageWarning=transport.StorageWarning,reserveFreeMiB=512,reserveCommitMiB=1024,inputKinds=new[]{"text","key","chord","click"},captureModes=new[]{"window-dc","print-window"},transport="current-user named pipe",disconnectPolicy="Client disconnect preserves the layer",maxActiveSessions=1,maxRequests=(long?)null,responseCache=32,observationRetention=new{maxBytes=64*1024*1024,maxFiles=256,rotation=true},operations=new[]{"create","list","status","observe","inspect-begin","inspect-poll","surface","input","attention","ownership","viewer","recover","repair-keeper","reconcile","replace-owner","close","shutdown"},delivery="forwarded_unverified; reconcile unknown outcomes before new input"}}));continue;
                        }
                        if(op=="hello")throw new AgentException("invalid_request","Hello uses id 0.");
                        if(!request.TryGetProperty("epoch",out var epoch)||epoch.GetString()!=transport.Epoch)throw new AgentException("stale_host_epoch","Reconnect and reconcile current application state before issuing commands.");
                        if(!long.TryParse(id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out long sequence)||sequence<1||sequence==long.MaxValue||sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)!=id)throw new AgentException("invalid_request","Protocol 2 IDs are canonical positive sequence numbers.");
                        if(sequence<=highWater&&!responses.ContainsKey(id))throw new AgentException("result_expired","Historical outcome is no longer retained. This sequence will never execute again.","unknown");
                        if(sequence>highWater+1)throw new AgentException("sequence_gap","Use the next sequence returned by hello.");
                    }
                    string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)));
                    if(seen.TryGetValue(id,out var previous))
                    {
                        if(previous!=hash)throw new AgentException("id_conflict","This request ID already names a different request.");
                        if(responses.TryGetValue(id,out var cached)){sink.WriteLine(cached);continue;}
                        throw new AgentException("result_expired","The cached response expired. The original outcome is unavailable. The command will not execute again; inspect current application state.","unknown");
                    }
                    if(transport==null&&seen.Count>=4096)throw new AgentException("request_budget","This host has reached 4096 unique requests. Close its sessions and start a new host.");
                    seen.Add(id,hash);if(transport!=null)highWater=long.Parse(id,System.Globalization.CultureInfo.InvariantCulture);remember=true;object? data;
                    if(op=="hello")data=new{protocol=1,transport="stdio JSONL",maxSessions=4,maxRequests=4096,responseCache=32,operations=new[]{"create","list","status","observe","inspect-begin","inspect-poll","surface","input","attention","ownership","viewer","close"},inputKinds=new[]{"text","key","chord","click"},inputValidationModes=new[]{"full-window-scan","target-subtree"},clickDeliveryModes=new[]{"synchronous","posted"},captureModes=new[]{"window-dc","print-window"},disconnectPolicy="EOF/host exit closes all owned sessions; unsaved changes can be lost",delivery="forwarded_unverified; observe actual results before retrying",coordinates="Target window client pixels; observation includes source client origin",capabilities=new{privateDesktop=true,humanViewer=true,virtualDevices=false,universalGpuCapture=false,liveDrag=false}};
                    else if(op=="replace-owner"&&transport!=null)
                    {
                        sessionId=Required(request,"session");if(!sessions.TryGetValue(sessionId,out var replacement)||replacement.KeeperDescriptor==null)throw new AgentException("unknown_session","No retained application keeper is available for this session.");
                        using var keeper=LayerKeeperClient.Load(replacement.KeeperDescriptor);if(replacement.Closed)keeper.Call("attach");keeper.Call("ping");
                        data=new{replacing=true,session=sessionId,keeperDescriptor=replacement.KeeperDescriptor,note="Owner will exit; application retained. Adopt and reconcile before input."};replaceOwnerRequested=true;
                    }
                    else if(op=="shutdown"&&transport!=null)
                    {
                        if(sessions.Values.Any(s=>!s.Closed||s.HasRetainedApplication)&&!Bool(request,"force"))throw new AgentException("applications_running","Close running layers normally before stopping their owner, or explicitly request force.");
                        foreach(var item in sessions.Values)item.Stop(Bool(request,"force"));shutdownRequested=true;replaceOwnerRequested=Bool(request,"force");data=new{stopped=true};
                    }
                    else if(op=="list")data=sessions.Select(p=>new{id=p.Key,state=p.Value.HasRetainedApplication?"retained_without_owner":p.Value.Closed?"closed":p.Value.OwnerUnresponsive?"owner_unresponsive":"ready"}).ToArray();
                    else if(op=="create")
                    {
                        if(transport!=null)foreach(string closedId in sessions.Where(p=>p.Value.Closed&&!p.Value.HasRetainedApplication).Select(p=>p.Key).ToArray())sessions.Remove(closedId);
                        if(sessions.Values.Count(s=>!s.Closed||s.HasRetainedApplication)>=(transport==null?4:1)||(transport==null&&sessions.Count>=16))throw new AgentException("session_budget","Use at most four active sessions and sixteen total sessions per host.");
                        if(transport!=null)LayerCapacity.RequireRoom();
                        sessionId=Guid.NewGuid().ToString("N");var session=new AgentSession(sessionId,request,Path.Combine(output,"session-"+sessionId),transport!=null,transport?.FaultTesting==true);
                        try{session.StartAsync().GetAwaiter().GetResult();sessions.Add(sessionId,session);data=new{id=sessionId,observation=session.InvokeAsync(JsonSerializer.SerializeToElement(new{op="status"}),artifacts).GetAwaiter().GetResult()};}
                        catch(Exception error){session.Dispose();if(transport!=null&&session.Started)sessions.TryAdd(sessionId,session);if(session.Started)throw new AgentException("startup_failed",error.Message,"unknown",error);throw;}
                    }
                    else
                    {
                        sessionId=Required(request,"session");
                        if(!sessions.TryGetValue(sessionId,out var session))throw new AgentException("unknown_session","The session does not belong to this host.");
                        if(session.Closed)
                        {
                            if(op!="status")throw new AgentException("session_closed","This session is closed.");
                            data=new{session=new{state=session.HasRetainedApplication?"retained_without_owner":"closed",keeperDescriptor=session.KeeperDescriptor}};
                        }
                        else
                        {
                            try{data=session.InvokeAsync(request.Clone(),artifacts).GetAwaiter().GetResult();}
                            catch(AgentException error)when(op=="status"&&error.Code=="session_closed"){data=new{session=new{state="closed"}};}
                        }
                    }
                    response=JsonSerializer.Serialize(new{protocol=transport==null?1:2,id,ok=true,data});
                }
                catch(Exception error)
                {
                    var known=error as AgentException;
                    response=JsonSerializer.Serialize(new{protocol=transport==null?1:2,id,ok=false,error=new{code=known?.Code??(error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException?"invalid_request":"operation_failed"),message=error.Message,outcome=known?.Outcome??"not_started",session=sessionId,keeperDescriptor=sessionId!=null&&sessions.TryGetValue(sessionId,out var retained)?retained.KeeperDescriptor:null}});
                }
                if(remember&&id!=null){responses[id]=response;order.Enqueue(id);while(order.Count>32){var expired=order.Dequeue();responses.Remove(expired);if(transport!=null)seen.Remove(expired);}}
                try
                {
                if(transport!=null&&File.Exists(Path.Combine(logDirectory,"requests.jsonl"))&&new FileInfo(Path.Combine(logDirectory,"requests.jsonl")).Length>2*1024*1024)File.Move(Path.Combine(logDirectory,"requests.jsonl"),Path.Combine(logDirectory,"requests.previous.jsonl"),true);
                File.AppendAllText(Path.Combine(logDirectory,"requests.jsonl"),JsonSerializer.Serialize(new{id=id?[..Math.Min(id.Length,128)],op=op[..Math.Min(op.Length,32)],session=sessionId?[..Math.Min(sessionId.Length,64)],milliseconds=watch.Elapsed.TotalMilliseconds})+Environment.NewLine);
                }
                catch(Exception error)when(transport!=null&&error is IOException or UnauthorizedAccessException){transport.StorageWarning="Diagnostic log unavailable; application retained. "+error.Message;}
                sink.WriteLine(response);
                if(replaceOwnerRequested)Environment.Exit(0); // Keeper retains the job; never dispose/replay pending application work.
            }
            }catch(IOException)when(transport!=null){if(replaceOwnerRequested)Environment.Exit(0); /* Committed outcomes retained; never replay input. */ }
            }while(transport!=null&&!shutdownRequested);
            return 0;
        }
        finally{foreach(var session in sessions.Values)session.Dispose();}
    }
    internal static string? ReadLine(TextReader reader)
    {
        var text=new StringBuilder();bool overflow=false;int c;
        while((c=reader.Read())!=-1&&c!='\n'){if(text.Length<65537)text.Append((char)c);else overflow=true;}
        if(c==-1&&text.Length==0)return null;
        if(overflow)return new string(' ',65537);
        return text.ToString().TrimEnd('\r');
    }

    sealed class AgentSession(string id,JsonElement request,string output,bool durable=false,bool faultTesting=false,string? recoveredKeeper=null):IDisposable
    {
        readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource ended=new(TaskCreationOptions.RunContinuationsAsynchronously);
        volatile DemoForm? form;Thread? thread;volatile bool closed,ownerUnresponsive;
        internal bool OwnerUnresponsive=>ownerUnresponsive;
        internal bool HasRetainedApplication
        {
            get=>durable&&Closed&&KeeperDescriptor!=null&&LayerKeeperClient.HasApplication(KeeperDescriptor);
        }
        internal string? KeeperDescriptor {get;private set;}
        internal bool Closed=>closed||form?.IsDisposed==true;
        internal bool Started=>thread!=null;
        internal async Task StartAsync()
        {
            // Resolve/validate configuration before creating any application.
            string profile=request.TryGetProperty("profile",out var p)?p.GetString()!:"private-dc";
            if(profile is not ("private-dc" or "visual-studio"))throw new AgentException("invalid_request","Profile must be private-dc or visual-studio.");
            string captureMode=request.TryGetProperty("captureMode",out var c)?c.GetString()!:"window-dc";
            if(captureMode is not ("window-dc" or "print-window"))throw new AgentException("invalid_request","captureMode must be window-dc or print-window.");
            Directory.CreateDirectory(output);
            if(durable&&recoveredKeeper==null)File.WriteAllText(Path.Combine(output,"create.json"),JsonSerializer.Serialize(new{sessionId=id,request}));
            string exe;string[] args;VisualStudioResearch.Setup? setup=null;string? setupFile=null;
            if(profile=="visual-studio")
            {
                setupFile=Path.GetFullPath(Required(request,"setupFile"));setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
                exe=setup.Executable;args=setup.Arguments.Where(a=>a!="/SafeMode").ToArray();int log=Array.IndexOf(args,"/Log");args[log+1]=Path.Combine(output,"ActivityLog.xml");args=[..args,setup.First];
            }
            else
            {
                string raw=Required(request,"executable");if(!Path.IsPathFullyQualified(raw))throw new AgentException("invalid_request","Use an absolute installed executable path.");
                exe=Path.GetFullPath(raw);if(!File.Exists(exe))throw new AgentException("application_missing","The requested executable does not exist.");
                args=request.TryGetProperty("arguments",out var a)?a.EnumerateArray().Select(v=>v.GetString()!).ToArray():[];
                if(args.Length>64||args.Any(a=>a==null||a.Length>4096))throw new AgentException("invalid_request","Use at most 64 bounded arguments.");
            }
            thread=new Thread(()=>
            {
                try
                {
                    using var lease=setup==null?null:VisualStudioFreshProfile.Lease(setup,setupFile!);
                    using var viewer=new DemoForm(exe,args,hidden:true,backend:CaptureBackend.PrintWindowFullContent,queuePointer:true,qaReportDirectory:output,postedShortcuts:true,fitPreview:true,startupTimeoutMilliseconds:60000,releaseStateBeforeKeyUp:true,nativePopupPointer:true,responsivePreview:true,directPreview:true,accessibleModalInput:true,lowLatencyInput:true,softwareClientPreview:true,windowDcPreview:captureMode=="window-dc",captureOriginReport:true,asyncStatusReports:true,preciseMouseWaits:true,fastPopupGeometry:true,incrementalPaint:true,deadlinePreview:true,deadlineFirstMilliseconds:4,splitPreviewClips:true,preciseChordHoldMilliseconds:24,postedFunctionKeys:true){AgentBackgroundStart=true,AgentDurableLayer=durable,AgentFaultVerification=faultTesting,AgentRecoveredKeeper=recoveredKeeper};
                    form=viewer;viewer.StartupCompleted+=error=>{viewer.AgentFinishStartup();var state=JsonSerializer.SerializeToElement(viewer.AgentState());if(state.TryGetProperty("keeperDescriptor",out var keeper)&&keeper.ValueKind==JsonValueKind.String)KeeperDescriptor=keeper.GetString();if(error==null)ready.TrySetResult();else ready.TrySetException(new AgentException("startup_failed",error));};
                    Application.Run(viewer);
                }
                catch(Exception error){ready.TrySetException(error);}
                finally{closed=true;ready.TrySetException(new AgentException("session_closed","Viewer closed before startup completed."));ended.TrySetResult();}
            }){IsBackground=true,Name="VIAVW agent session "+id};thread.SetApartmentState(ApartmentState.STA);thread.Start();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(75));
        }
        internal Task<JsonElement> InvokeAsync(JsonElement command,AgentArtifacts artifacts)
        {
            var completion=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);var viewer=form;
            if(ownerUnresponsive)return Task.FromException<JsonElement>(new AgentException("owner_unresponsive","Owner UI stopped responding; replace its owner without closing the retained application.","unknown"));
            if(closed||viewer==null||viewer.IsDisposed)return Task.FromException<JsonElement>(new AgentException("session_closed","Viewer is closed."));
            try{viewer.BeginInvoke((Action)(async()=>{try{completion.TrySetResult(await viewer.AgentExecuteAsync(command,artifacts));}catch(Exception error){completion.TrySetException(error);}}));}
            catch(Exception error){completion.TrySetException(error);}
            return CompleteOrClosedAsync();
            async Task<JsonElement> CompleteOrClosedAsync()
            {
                string op=command.GetProperty("op").GetString()!;
                using var deadlineCancellation=new CancellationTokenSource();
                var deadline=Task.Delay(op=="recover"?20000:10000,deadlineCancellation.Token);
                await Task.WhenAny(completion.Task,ended.Task,deadline);
                if(deadline.IsCompleted&&!completion.Task.IsCompleted&&!ended.Task.IsCompleted){ownerUnresponsive=true;throw new AgentException("owner_unresponsive","Owner UI deadline exceeded; keep the application and replace this owner. No input replay.","unknown");}
                deadlineCancellation.Cancel();
                if(completion.Task.IsCompleted)return await completion.Task;
                throw new AgentException("session_closed","The viewer closed during this request; inspect before retrying.",command.GetProperty("op").GetString()=="input"?"unknown":"not_started");
            }
        }
        public void Dispose()=>Stop(false);
        internal void Stop(bool force)
        {
            var viewer=form;
            if(force&&durable){if(HasRetainedApplication)LayerKeeperClient.Discard(KeeperDescriptor!);else viewer?.AgentDestroyJob();return;}
            if(!closed&&viewer!=null&&!viewer.IsDisposed)try{viewer.BeginInvoke((Action)(async()=>await viewer.AgentAbortAsync(force||!durable)));}catch(InvalidOperationException){}
            if(thread!=null&&!thread.Join(8000))throw new TimeoutException("Owned session viewer did not stop within eight seconds.");
        }
    }
}

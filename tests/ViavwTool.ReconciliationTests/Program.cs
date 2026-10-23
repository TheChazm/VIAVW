using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using ViavwTool;

var checks=new Dictionary<string,bool>();
void Check(string name,bool value){checks.Add(name,value);if(!value)throw new Exception(name);}
async Task<ToolFailure> Reject(Func<Task<JsonObject>> operation,string code)
{
    try{await operation();}catch(ToolFailure error){Check(code+" #"+checks.Count,error.Code==code);return error;}
    throw new Exception("Expected rejection: "+code);
}
await using var peer=new ModalPeer();
var tool=new ToolService(peer.Root);
Task<JsonObject> Call(string name,string? observation=null,bool confirm=true,string reason="Synthetic modal state inspected",int key=123)
{
    if(name is not ("viavw_observe" or "viavw_input" or "viavw_reconcile"))throw new Exception("Unsafe test operation");
    JsonObject args=new(){["layer"]=peer.Layer,["capture"]=false};
    if(observation!=null)args["observation"]=observation;
    if(name=="viavw_input"){args["kind"]="key";args["key"]=key;}
    if(name=="viavw_reconcile"){args["confirmContext"]=confirm;args["reason"]=reason;}
    return tool.Execute(name,args);
}
async Task<string> Observe()=> (await Call("viavw_observe"))["data"]!["observation"]!.GetValue<string>();
bool Uncertain()=>Files.Read(peer.RecordPath)["uncertainInput"]?.GetValue<bool>()==true;
string token=await Observe();
var unknown=await Reject(()=>Call("viavw_input",token),"modal_transition_unknown");
Check("unknown outcome retained",unknown.Outcome=="unknown"&&Uncertain());
Check("intent persisted before the fake modal dispatch",peer.IntentPersisted&&peer.Inputs==1);
await Reject(()=>Call("viavw_input",token),"stale_observation");
tool=new ToolService(peer.Root); // actual secret/catalog reconnect, not a copied fence
token=await Observe();
await Reject(()=>Call("viavw_input",token),"input_uncertain");
Check("reconnect did not replay F12",peer.Inputs==1&&Uncertain());
await Reject(()=>Call("viavw_reconcile",token,false),"context_required");
await Reject(()=>Call("viavw_reconcile",token,true," "),"context_required");
await Reject(()=>Call("viavw_reconcile",token+"0"),"stale_observation");
foreach(string field in new[]{"revision","inputSequence","root"})
{
    token=await Observe();long old=peer.State[field]!.GetValue<long>();peer.State[field]=old+1;
    await Reject(()=>Call("viavw_reconcile",token),"stale_observation");peer.State[field]=old;
}
token=await Observe();peer.State["owner"]="Human";
await Reject(()=>Call("viavw_reconcile",token),"human_owns_input");peer.State["owner"]="Automation";
foreach(string field in new[]{"workerFaulted","keeperReady"})
{
    peer.State[field]=field=="workerFaulted";token=await Observe();
    await Reject(()=>Call("viavw_reconcile",token),"layer_suspended");peer.State[field]=field!="workerFaulted";
}
Check("all rejected reconciliation paths preserve fence and avoid input",Uncertain()&&peer.Inputs==1&&peer.Reconciles==0);
token=await Observe();var resumed=await Call("viavw_reconcile",token);
Check("accepted observed modal clears persistent fence",!Uncertain()&&resumed["data"]!["result"]!["resumed"]!.GetValue<bool>());
Check("reconciliation never replays input",peer.Inputs==1&&peer.Reconciles==0);
Check("sanitized confirmation reason persisted",Files.Read(peer.RecordPath)["lastReconciliationReason"]!.GetValue<string>()=="Synthetic modal state inspected");
await Reject(()=>Call("viavw_reconcile",token),"invalid_state");
peer.Mode="ack";token=await Observe();await Call("viavw_input",token,key:27);
Check("explicit Escape decision dispatched once without F12 replay",peer.Inputs==2&&!Uncertain()&&peer.InputKeys.SequenceEqual(new[]{123,27}));

// A multiline operation forwards its first segment, then receives not_started.
// The higher-level outcome must be unknown, because the prefix already arrived.
peer.Mode="partial";token=await Observe();int before=peer.Inputs;
var partial=await Reject(()=>tool.Execute("viavw_input",new(){["layer"]=peer.Layer,["observation"]=token,["kind"]="text",["text"]="prefix\nnever-sent"}),"partial_input");
Check("partial operation retains unknown fence",partial.Outcome=="unknown"&&Uncertain()&&peer.Inputs==before+2);
Check("partial operation did not deliver trailing segment",!peer.DeliveredText.Contains("never-sent"));
tool=new ToolService(peer.Root);token=await Observe();await Reject(()=>Call("viavw_input",token),"input_uncertain");
Check("partial reconnect blocks additional dispatch",peer.Inputs==before+2);

// Recovered owner reconciliation itself can be uncertain; it must not clear the
// durable local fence until the owner acknowledges the explicit reconciliation.
peer.State["recoveryPending"]=true;peer.Mode="reconcile-unknown";token=await Observe();
await Reject(()=>Call("viavw_reconcile",token),"reconcile_unknown");
Check("unknown recovery response preserves fence",Uncertain()&&peer.Reconciles==1);
peer.Mode="ack";token=await Observe();await Call("viavw_reconcile",token);
Check("fresh explicit recovery decision clears fence without input replay",!Uncertain()&&peer.Reconciles==2&&peer.Inputs==before+2);
Check("no ownership or launch commands requested",peer.Operations.All(op=>op is "hello" or "observe" or "status" or "input" or "reconcile"));
Console.WriteLine(new JsonObject{["passed"]=true,["checks"]=JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(checks)),["assertions"]=checks.Count,["guiApplicationsLaunched"]=0,["excelGuiAcceptance"]="pending; fake modal peer only"}.ToJsonString());

sealed class ModalPeer : IAsyncDisposable
{
    internal string Root{get;}=Path.Combine(Path.GetTempPath(),"VIAVW-reconciliation-"+Guid.NewGuid().ToString("N"));
    internal string Layer{get;}=Guid.NewGuid().ToString("N");
    internal string RecordPath=>Path.Combine(Root,"artifacts","tool-layers",Layer,"tool-record.json");
    internal JsonObject State{get;}=new(){["revision"]=0L,["inputSequence"]=0L,["root"]=777L,["owner"]="Automation",["workerFaulted"]=false,["keeperReady"]=true,["recoveryPending"]=false,["state"]="ready"};
    internal string Mode="modal-unknown";
    internal int Inputs,Reconciles;
    internal bool IntentPersisted;
    internal List<string> Operations{get;}=[];
    internal List<string> DeliveredText{get;}=[];
    internal List<int> InputKeys{get;}=[];
    readonly string pipeName="VIAVW_reconciliation_test_"+Guid.NewGuid().ToString("N");
    readonly CancellationTokenSource stop=new();
    readonly Task work;
    int partialInputs;
    internal ModalPeer()
    {
        string host=Path.Combine(Root,"src","LayerHost","bin","Runtime","net10.0-windows","LayerHost.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);File.WriteAllText(host,"NONEXECUTABLE SYNTHETIC TEST FIXTURE");
        Files.Write(Path.Combine(Root,"package.json"),new(){["hostAssemblySha256"]=ToolService.Hash(host)});
        Files.Write(RecordPath,new(){["session"]="fake-owned-session"});
        using var current=Process.GetCurrentProcess();
        Files.Write(Path.Combine(Path.GetDirectoryName(RecordPath)!,"owner","layer-owner.json"),new(){["pipe"]=pipeName,["pid"]=current.Id,["createdFileTime"]=current.StartTime.ToFileTimeUtc()});
        work=Run();
    }
    async Task Run()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                using var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader=new StreamReader(server,Encoding.UTF8,false,4096,true);
                using var writer=new StreamWriter(server,new UTF8Encoding(false),4096,true){AutoFlush=true};
                while(await reader.ReadLineAsync(stop.Token) is {} line)
                {
                    var request=JsonNode.Parse(line)!.AsObject();string op=request["op"]!.GetValue<string>();Operations.Add(op);
                    JsonObject data=new(){["session"]=State.DeepClone(),["result"]=new JsonObject{["recoveryToken"]="fake-fresh-recovery"}};
                    JsonObject? error=null;
                    if(op=="hello")data=new(){["epoch"]="same-fake-owner-epoch",["nextSequence"]=1L};
                    else if(op=="input")
                    {
                        Inputs++;IntentPersisted|=Files.Read(RecordPath)["uncertainInput"]!.GetValue<bool>();
                        if(request["key"] is {} key)InputKeys.Add(key.GetValue<int>());
                        if(Mode=="partial"&&++partialInputs==2)error=Error("input_rejected","not_started");
                        else
                        {
                            if(request["text"] is {} text)DeliveredText.Add(text.GetValue<string>());
                            State["inputSequence"]=State["inputSequence"]!.GetValue<long>()+1;
                            if(Mode=="modal-unknown")error=Error("modal_transition_unknown","unknown");
                            data["session"]=State.DeepClone();
                        }
                    }
                    else if(op=="reconcile")
                    {
                        Reconciles++;
                        if(Mode=="reconcile-unknown")error=Error("reconcile_unknown","unknown");
                        else{State["recoveryPending"]=false;data["session"]=State.DeepClone();data["result"]=new JsonObject{["resumed"]=true};}
                    }
                    else if(op is not ("observe" or "status"))throw new Exception("Unexpected operation "+op);
                    var response=new JsonObject{["protocol"]=2,["id"]=request["id"]!.DeepClone(),["ok"]=error==null};
                    if(error==null)response["data"]=data;else response["error"]=error;
                    await writer.WriteLineAsync(response.ToJsonString());
                }
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
    }
    static JsonObject Error(string code,string outcome)=>new(){["code"]=code,["message"]="Synthetic peer outcome; no application operated",["outcome"]=outcome};
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();await work.WaitAsync(TimeSpan.FromSeconds(4));stop.Dispose();
        // Exact task-created root only. Preserve failure fixtures if unexpected files appear.
        foreach(string path in new[]{RecordPath,Path.Combine(Path.GetDirectoryName(RecordPath)!,".control.lock"),Path.Combine(Path.GetDirectoryName(RecordPath)!,"owner","layer-owner.json"),Path.Combine(Root,"artifacts","tool-layers","observation-key.bin"),Path.Combine(Root,"package.json"),Path.Combine(Root,"src","LayerHost","bin","Runtime","net10.0-windows","LayerHost.dll")})
            File.Delete(path);
        foreach(string directory in Directory.EnumerateDirectories(Root,"*",SearchOption.AllDirectories).OrderByDescending(d=>d.Length))Directory.Delete(directory,false);
        Directory.Delete(Root,false);
    }
}

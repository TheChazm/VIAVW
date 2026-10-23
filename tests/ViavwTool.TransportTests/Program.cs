using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using ViavwTool;

var checks=new Dictionary<string,bool>();
void Check(string name,bool condition){checks[name]=condition;if(!condition)throw new Exception(name);}
async Task<ToolFailure> Failure(Func<Task<JsonObject>> action)
{
    try{await action();}catch(ToolFailure error){return error;}
    throw new Exception("Expected typed failure");
}
string? failure=null;
try
{
    foreach(string mode in new[]{"wrong-id","bad-json","wrong-protocol","wrong-protocol-rejection","missing-ok","bad-error","eof","timeout"})
    {
        await using var peer=new Peer(mode);
        await using var connection=new OwnerConnection(peer.Descriptor,TimeSpan.FromMilliseconds(500));
        await connection.Connect();
        var first=await Failure(()=>connection.Call("input"));
        Check(mode+": ambiguous reply is typed unknown",first.Code=="transport_unknown"&&first.Outcome=="unknown");
        var second=await Failure(()=>connection.Call("input"));
        Check(mode+": second operation rejected before dispatch",second.Code=="transport_unusable"&&second.Outcome=="unknown"&&peer.Inputs==1);
    }
    foreach(string outcome in new[]{"not_started","unknown"})
    {
        await using var peer=new Peer("owner-error",outcome);
        await using var connection=new OwnerConnection(peer.Descriptor);
        await connection.Connect();
        var error=await Failure(()=>connection.Call("input"));
        Check(outcome+": well-framed owner error preserved",error.Code=="human_owns_input"&&error.Outcome==outcome);
        var observed=await connection.Call("status");
        Check(outcome+": follow-up inspection remains usable",observed["state"]!.GetValue<string>()=="observed"&&peer.Inputs==1);
    }
    await using(var peer=new Peer("valid"))
    {
        var connection=new OwnerConnection(peer.Descriptor);
        var early=await Failure(()=>connection.Call("input"));
        Check("uninitialized connection sends nothing",early.Code=="transport_not_connected"&&peer.Inputs==0);
        await connection.Connect();
        await connection.Call("status");await connection.Call("status");
        Check("ordinary ordered responses remain usable",peer.Ids.SequenceEqual(new[]{"0","1","2"}));
        await connection.DisposeAsync();await connection.DisposeAsync();
        var closed=await Failure(()=>connection.Call("input"));
        Check("disposed connection rejects new dispatch",closed.Code=="transport_unusable"&&peer.Inputs==0);
    }
    foreach(string mode in new[]{"bad-hello","bad-hello-sequence"})
    {
        await using var peer=new Peer(mode);
        await using var connection=new OwnerConnection(peer.Descriptor);
        ToolFailure? problem=null;
        try{await connection.Connect();}catch(ToolFailure error){problem=error;}
        Check(mode+": handshake failure is typed unknown",problem?.Code=="transport_unknown"&&problem.Outcome=="unknown");
        var blocked=await Failure(()=>connection.Call("input"));
        Check(mode+": no call after invalid handshake",blocked.Code=="transport_unusable"&&peer.Inputs==0);
    }
}
catch(Exception error){failure=error.ToString();}
var result=new JsonObject{["passed"]=failure==null&&checks.Values.All(v=>v),["failure"]=failure,
    ["checks"]=new JsonObject(checks.Select(p=>KeyValuePair.Create<string,JsonNode?>(p.Key,JsonValue.Create(p.Value)))),
    ["scope"]="Actual linked production transport against in-process fake pipes only; no GUI, application layer, live service or input device.",
    ["realApplicationsLaunched"]=0};
Console.WriteLine(result.ToJsonString());
return failure==null?0:1;

sealed class Peer:IAsyncDisposable
{
    readonly NamedPipeServerStream server;
    readonly CancellationTokenSource stop=new();
    readonly Task work;
    readonly string mode,outcome;
    int inputs;
    internal int Inputs=>Volatile.Read(ref inputs);
    internal List<string> Ids {get;}=[];
    internal JsonObject Descriptor {get;}
    internal Peer(string mode,string outcome="not_started")
    {
        this.mode=mode;this.outcome=outcome;
        string name="VIAVW_transport_test_"+Guid.NewGuid().ToString("N");
        Descriptor=new(){["pipe"]=name};
        server=new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        work=Run();
    }
    async Task Run()
    {
        try
        {
            await server.WaitForConnectionAsync(stop.Token);
            using var reader=new StreamReader(server,Encoding.UTF8,false,4096,true);
            using var writer=new StreamWriter(server,new UTF8Encoding(false),4096,true){AutoFlush=true};
            while(await reader.ReadLineAsync(stop.Token) is {} line)
            {
                var request=JsonNode.Parse(line)!.AsObject();
                string id=request["id"]!.GetValue<string>(),op=request["op"]!.GetValue<string>();Ids.Add(id);
                var data=new JsonObject{["state"]="observed"};
                if(op=="hello")
                {
                    data=new(){["epoch"]=Guid.NewGuid().ToString("N"),["nextSequence"]=1};
                    if(mode=="bad-hello")data.Remove("epoch");
                    if(mode=="bad-hello-sequence")data["nextSequence"]=0;
                }
                if(op=="input")Interlocked.Increment(ref inputs);
                if(op=="input"&&Inputs==1)
                {
                    if(mode=="eof"){server.Disconnect();return;}
                    if(mode=="timeout")await Task.Delay(TimeSpan.FromSeconds(5),stop.Token);
                    if(mode=="bad-json"){await writer.WriteLineAsync("{broken");continue;}
                }
                var reply=new JsonObject{["protocol"]=2,["id"]=id,["ok"]=true,["data"]=data};
                if(op=="input"&&Inputs==1)
                {
                    if(mode=="wrong-id")reply["id"]="other";
                    if(mode=="wrong-protocol")reply["protocol"]=99;
                    if(mode=="missing-ok")reply.Remove("ok");
                    if(mode=="bad-error"){reply["ok"]=false;reply["error"]=new JsonObject{["code"]=3};}
                    if(mode=="owner-error"||mode=="wrong-protocol-rejection")
                    {
                        if(mode=="wrong-protocol-rejection")reply["protocol"]=1;
                        reply["ok"]=false;
                        reply["error"]=new JsonObject{["code"]="human_owns_input",["message"]="Simulated application rejection; no actual application.",["outcome"]=outcome};
                    }
                }
                await writer.WriteLineAsync(reply.ToJsonString());
            }
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException){ }
        catch(InvalidOperationException)when(!server.IsConnected){ /* Expected fake EOF writer flush. */ }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();server.Dispose();
        await work.WaitAsync(TimeSpan.FromSeconds(3));stop.Dispose();
    }
}

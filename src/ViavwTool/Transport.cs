using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace ViavwTool;

internal sealed class ToolFailure(string code,string message,string outcome="not_started"):Exception(message)
{
    internal string Code {get;}=code;
    internal string Outcome {get;}=outcome;
}

internal sealed class OwnerConnection : IAsyncDisposable
{
    readonly NamedPipeClientStream pipe;
    readonly StreamReader reader;
    readonly StreamWriter writer;
    readonly TimeSpan responseTimeout;
    long next;
    bool connected,failed,disposed;
    internal string Epoch {get;private set;}="";
    internal OwnerConnection(JsonObject descriptor,TimeSpan? responseTimeout=null)
    {
        this.responseTimeout=responseTimeout??TimeSpan.FromSeconds(90);
        if(this.responseTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(responseTimeout));
        pipe=new(".",descriptor["pipe"]!.GetValue<string>(),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        reader=new(pipe,Encoding.UTF8,false,4096,true);
        writer=new(pipe,new UTF8Encoding(false),4096,true);
    }
    void Available()
    {
        if(failed||disposed)throw new ToolFailure("transport_unusable","This owner connection is closed or has an uncertain reply. No new request was sent. Reconnect, observe and reconcile; never replay unknown input.","unknown");
    }
    ToolFailure Unknown(Exception error)
    {
        // A late reply can belong to the previous request. Never write another
        // operation to that stream or interpret a later frame as its outcome.
        failed=true;pipe.Dispose();
        return new ToolFailure("transport_unknown",error.Message+" Application retained. This connection is unusable; reconnect, observe and reconcile without replaying input.","unknown");
    }
    static T Value<T>(JsonNode? node,string field)
    {
        if(node is not JsonValue value||!value.TryGetValue<T>(out var result))throw new IOException("Invalid owner reply field: "+field);
        return result!;
    }
    internal async Task Connect()
    {
        Available();
        if(connected)throw new ToolFailure("already_connected","Owner connection is already initialized.");
        try
        {
            await pipe.ConnectAsync(3000);writer.AutoFlush=true;
            var hello=await Exchange(new(){["protocol"]=2,["id"]="0",["op"]="hello"});
            Epoch=Value<string>(hello["epoch"],"epoch");next=Value<long>(hello["nextSequence"],"nextSequence");
            if(string.IsNullOrWhiteSpace(Epoch)||next<1)throw new IOException("Invalid owner handshake epoch/sequence.");
            connected=true;
        }
        catch(ToolFailure){failed=true;pipe.Dispose();throw;}
        catch(Exception error)when(error is IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {throw Unknown(error);}
    }
    async Task<JsonObject> Exchange(JsonObject request)
    {
        Available();
        try
        {
            await writer.WriteLineAsync(request.ToJsonString());
            var line=await reader.ReadLineAsync().WaitAsync(responseTimeout);
            var response=JsonNode.Parse(line??throw new IOException("Owner disconnected."))?.AsObject()??throw new IOException("Empty owner reply.");
            if(Value<int>(response["protocol"],"protocol")!=2||Value<string>(response["id"],"id")!=request["id"]!.GetValue<string>())throw new IOException("Misframed owner response.");
            if(Value<bool>(response["ok"],"ok"))
            {
                if(!response.ContainsKey("data"))throw new IOException("Owner reply has no data field.");
                return response["data"] is JsonObject data?data.DeepClone().AsObject():new JsonObject{["items"]=response["data"]?.DeepClone()};
            }
            var error=response["error"]?.AsObject()??throw new IOException("Owner reply has no error object.");
            string code=Value<string>(error["code"],"error.code"),message=Value<string>(error["message"],"error.message");
            string outcome=error["outcome"]==null?"unknown":Value<string>(error["outcome"],"error.outcome");
            if(string.IsNullOrWhiteSpace(code)||outcome is not ("not_started" or "unknown"))throw new IOException("Invalid owner error envelope.");
            throw new ToolFailure(code,message,outcome);
        }
        // A well-framed owner error is different from a broken transport. It
        // leaves the stream usable for inspection; input policy remains above it.
        catch(ToolFailure){throw;}
        catch(Exception error)when(error is IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {throw Unknown(error);}
    }
    internal Task<JsonObject> Call(string operation,string? session=null,JsonObject? fields=null)
    {
        Available();
        if(!connected)throw new ToolFailure("transport_not_connected","Initialize the owner connection before sending requests.");
        var request=fields?.DeepClone().AsObject()??new JsonObject();
        request["protocol"]=2;request["id"]=(next++).ToString(System.Globalization.CultureInfo.InvariantCulture);request["epoch"]=Epoch;request["op"]=operation;
        if(session!=null)request["session"]=session;
        return Exchange(request);
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;
        try{await writer.DisposeAsync();}catch(IOException){}catch(InvalidOperationException){}
        reader.Dispose();await pipe.DisposeAsync();
    }
}

internal static class Files
{
    internal static JsonObject Read(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return JsonNode.Parse(stream)!.AsObject();
    }
    internal static void Write(string path,JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,value.ToJsonString(),new UTF8Encoding(false));
        try
        {
            for(int attempt=0;;attempt++)
                try{if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);return;}
                catch(IOException)when(attempt<20){Thread.Sleep(20);}
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static bool Alive(JsonObject descriptor)
    {
        try
        {
            using var process=Process.GetProcessById(descriptor["pid"]!.GetValue<int>());
            return !process.HasExited&&process.StartTime.ToFileTimeUtc()==descriptor["createdFileTime"]!.GetValue<long>();
        }
        catch(ArgumentException){return false;}
    }
}

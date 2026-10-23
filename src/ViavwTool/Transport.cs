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
    long next;
    internal string Epoch {get;private set;}="";
    internal OwnerConnection(JsonObject descriptor)
    {
        pipe=new(".",descriptor["pipe"]!.GetValue<string>(),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        reader=new(pipe,Encoding.UTF8,false,4096,true);
        writer=new(pipe,new UTF8Encoding(false),4096,true);
    }
    internal async Task Connect()
    {
        await pipe.ConnectAsync(3000);
        writer.AutoFlush=true;
        var hello=await Exchange(new(){["protocol"]=2,["id"]="0",["op"]="hello"});
        Epoch=hello["epoch"]!.GetValue<string>();next=hello["nextSequence"]!.GetValue<long>();
    }
    async Task<JsonObject> Exchange(JsonObject request)
    {
        try
        {
            await writer.WriteLineAsync(request.ToJsonString());
            var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
            var response=JsonNode.Parse(line??throw new IOException("Owner disconnected."))!.AsObject();
            if(response["id"]!.GetValue<string>()!=request["id"]!.GetValue<string>())throw new IOException("Misframed owner response.");
            if(response["ok"]!.GetValue<bool>())return response["data"] is JsonObject data?data.DeepClone().AsObject():(new JsonObject{["items"]=response["data"]?.DeepClone()});
            var error=response["error"]!;
            throw new ToolFailure(error["code"]!.GetValue<string>(),error["message"]!.GetValue<string>(),error["outcome"]?.GetValue<string>()??"unknown");
        }
        catch(ToolFailure){throw;}
        catch(Exception error)when(error is IOException or TimeoutException)
        {throw new ToolFailure("transport_unknown",error.Message+" Application retained. Observe and reconcile; never replay this input.","unknown");}
    }
    internal Task<JsonObject> Call(string operation,string? session=null,JsonObject? fields=null)
    {
        var request=fields?.DeepClone().AsObject()??new JsonObject();
        request["protocol"]=2;request["id"]=(next++).ToString(System.Globalization.CultureInfo.InvariantCulture);request["epoch"]=Epoch;request["op"]=operation;
        if(session!=null)request["session"]=session;
        return Exchange(request);
    }
    public async ValueTask DisposeAsync(){try{await writer.DisposeAsync();}catch(IOException){}catch(InvalidOperationException){}reader.Dispose();await pipe.DisposeAsync();}
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

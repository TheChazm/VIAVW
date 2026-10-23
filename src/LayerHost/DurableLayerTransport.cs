using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace LayerHost;
// Explicit per-layer owner. No Windows service installation or global listener.
// Each owner is a separate process; disconnecting a controller closes no app.
internal sealed class DurableLayerTransport
{
    readonly string output,pipe="VIAVW_layer_"+Guid.NewGuid().ToString("N");
    internal string? StorageWarning {get;set;}
    internal string Epoch {get;}=Guid.NewGuid().ToString("N");
    internal bool FaultTesting {get;}
    bool descriptorPublished;
    internal DurableLayerTransport(string output,bool faultTesting=false,bool adopt=false)
    {
        FaultTesting=faultTesting;this.output=output;
        if(!adopt||!Directory.Exists(output)||!Directory.EnumerateFileSystemEntries(output).Any())GpuBridgeVerification.PrepareOutput(output);
        else
        {
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(output).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Reuse only an owned layer control directory.");
            using var previous=JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"layer-owner.json")));var info=previous.RootElement;
            try{using var process=Process.GetProcessById(info.GetProperty("pid").GetInt32());if(!process.HasExited&&process.StartTime.ToUniversalTime()==info.GetProperty("createdUtc").GetDateTime().ToUniversalTime())throw new InvalidOperationException("Existing layer owner is still alive.");}catch(ArgumentException){}
        }
    }
    internal Connection Accept()
    {
        var server=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try
        {
            using var process=Process.GetCurrentProcess();
            string descriptor=Path.Combine(output,"layer-owner.json");
            if(!descriptorPublished)
            {
                SnapshotFiles.ReplaceTextAsync(descriptor,JsonSerializer.Serialize(new{schema=1,protocol=2,pipe,epoch=Epoch,pid=process.Id,createdUtc=process.StartTime.ToUniversalTime(),createdFileTime=process.StartTime.ToFileTimeUtc(),executable=Environment.ProcessPath,disconnectPolicy="preserve running application",ownerCrashPolicy="Durable application job retained by per-layer keeper; adoption requires fresh reconciliation"},new JsonSerializerOptions{WriteIndented=true}),maxRetries:50).GetAwaiter().GetResult();
                descriptorPublished=true;
            }
            server.WaitForConnection();return new Connection(server);
        }
        catch{server.Dispose();throw;}
    }
    internal sealed class Connection(NamedPipeServerStream stream):IDisposable
    {
        internal StreamReader Reader {get;}=new(stream,Encoding.UTF8,false,4096,true);
        internal StreamWriter Writer {get;}=new(stream,new UTF8Encoding(false),4096,true){AutoFlush=true};
        public void Dispose(){try{Writer.Dispose();}catch(IOException){}Reader.Dispose();stream.Dispose();}
    }
    internal static int Run(string output,bool faultTesting=false)=>AgentHost.Run(output,new DurableLayerTransport(output,faultTesting));
    internal static int Adopt(string output,string keeper)=>AgentHost.Run(output,new DurableLayerTransport(output,adopt:true),RecoveredLayerSeed.Load(keeper));
    internal static int Connect(string descriptor)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(descriptor));var d=doc.RootElement;
        if(d.GetProperty("protocol").GetInt32()!=2)throw new ArgumentException("Use a layer owner descriptor.");
        using var pipe=new NamedPipeClientStream(".",d.GetProperty("pipe").GetString()!,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        pipe.Connect(3000);using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        string? line;while((line=AgentHost.ReadLine(Console.In))!=null)
        {
            writer.WriteLine(line);string response=reader.ReadLine()??throw new IOException("Layer owner disconnected; outcome may be unknown.");Console.WriteLine(response);
        }
        return 0;
    }
}

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace LayerHost;
internal sealed class LayerKeeperClient:IDisposable
{
    readonly string pipe;
    nint processHandle;
    internal bool IsAlive=>processHandle!=0&&Native.WaitForSingleObject(processHandle,0)!=0;
    public void Dispose(){if(processHandle!=0){Native.CloseHandle(processHandle);processHandle=0;}}
    internal int Pid {get;}
    internal string Descriptor {get;}
    LayerKeeperClient(string descriptor,string pipe,int pid,long expectedCreated)
    {
        Descriptor=descriptor;this.pipe=pipe;Pid=pid;processHandle=Native.OpenProcess(0x1|0x100000|0x1000,false,(uint)pid);Native.Check(processHandle!=0,"Hold keeper process identity");
        try{Native.Check(Native.GetProcessTimes(processHandle,out long actual,out _,out _,out _)&&actual==expectedCreated,"Keeper creation identity must match its descriptor");}
        catch{Dispose();throw;}
    }
    internal void StopForVerification(nint job)
    {if(IsAlive)Native.Check(Native.TerminateProcess(processHandle,3),"Stop exact held keeper process identity");}
    internal static LayerKeeperClient Wait(string folder,int pid)
    {
        string descriptor=Path.Combine(folder,"keeper.json");var clock=Stopwatch.StartNew();
        while(!File.Exists(descriptor)){if(clock.ElapsedMilliseconds>5000)throw new IOException("Keeper startup failed; application launch was not attempted.");Thread.Sleep(10);}
        var client=Load(descriptor);if(client.Pid!=pid){client.Dispose();throw new IOException("Keeper identity mismatch.");}return client;
    }
    internal static LayerKeeperClient Load(string descriptor)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"))+Path.DirectorySeparatorChar;
        for(int depth=0;depth<4;depth++)
        {
            descriptor=Path.GetFullPath(descriptor);if(!descriptor.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use an owned keeper descriptor under VIAVW artifacts.");
            using var json=JsonDocument.Parse(SnapshotFiles.ReadText(descriptor));
            if(json.RootElement.TryGetProperty("keeperDescriptor",out var pointer)){descriptor=pointer.GetString()!;continue;}
            return new(descriptor,json.RootElement.GetProperty("pipe").GetString()!,json.RootElement.GetProperty("pid").GetInt32(),json.RootElement.GetProperty("createdFileTime").GetInt64());
        }
        throw new ArgumentException("Keeper locator cycle.");
    }
    internal static bool HasApplication(string descriptor)
    {
        try
        {
            using var keeper=Load(descriptor);if(!keeper.IsAlive)return false;
            using var metadata=JsonDocument.Parse(SnapshotFiles.ReadText(keeper.Descriptor));var data=metadata.RootElement;
            nint job=Native.OpenJobObjectW(4,false,data.GetProperty("job").GetString()!);
            if(job==0)return Marshal.GetLastWin32Error()!=2;
            try
            {
                var infrastructure=data.GetProperty("snapshot").GetProperty("infrastructure").EnumerateArray().ToDictionary(v=>v.GetProperty("pid").GetInt32(),v=>v.GetProperty("created").GetInt64());
                foreach(int pid in Native.JobProcessIds(job))
                {
                    if(!infrastructure.TryGetValue(pid,out long expected))return true;
                    nint process=Native.OpenProcess(0x1000,false,(uint)pid);if(process==0)continue;
                    try{if(!Native.GetProcessTimes(process,out long created,out _,out _,out _)||created!=expected)return true;}finally{Native.CloseHandle(process);}
                }
                return false;
            }
            finally{Native.CloseHandle(job);}
        }
        catch(System.ComponentModel.Win32Exception error)when(error.NativeErrorCode==87){return false;}
        catch{return true;} // Unverifiable retained state must not be discarded.
    }
    internal static int Discard(string descriptor)
    {
        using var client=Load(descriptor);using var metadata=JsonDocument.Parse(File.ReadAllText(client.Descriptor));
        nint job=Native.OpenJobObjectW(4,false,metadata.RootElement.GetProperty("job").GetString()!);Native.Check(job!=0,"Open exact owned job for explicit discard confirmation");
        try
        {
            client.Call("attach");
            try{client.Call("stop",force:true);}catch(Exception error)when(error is IOException or TimeoutException){/* Self-termination can close the reply pipe; verify the job itself. */}
            var clock=Stopwatch.StartNew();while(Native.JobProcessIds(job).Length!=0&&clock.ElapsedMilliseconds<3000)Thread.Sleep(10);
            if(Native.JobProcessIds(job).Length!=0)throw new IOException("Explicit discard outcome is uncertain; owned processes still remain.");
            Console.WriteLine("Owned disposable layer discarded; its exact job is empty.");return 0;
        }
        finally{Native.CloseHandle(job);}
    }
    internal JsonElement Call(string op,object? snapshot=null,bool force=false)
    {
        using var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);client.Connect(3000);
        Native.Check(Native.GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out uint server)&&server==Pid,"Keeper pipe must belong to its held process identity");
        using var reader=new StreamReader(client,Encoding.UTF8,false,4096,true);using var writer=new StreamWriter(client,new UTF8Encoding(false),4096,true){AutoFlush=true};
        writer.WriteLine(JsonSerializer.Serialize(new{op,snapshot,force}));string line=reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult()??throw new IOException("Keeper disconnected.");
        using var result=JsonDocument.Parse(line);if(!result.RootElement.GetProperty("ok").GetBoolean())throw new InvalidOperationException(result.RootElement.GetProperty("error").GetString());return result.RootElement.GetProperty("data").Clone();
    }
}
internal static class LayerKeeper
{
    static bool IdentityAlive(int pid,long created)
    {
        nint process=Native.OpenProcess(0x1000|0x100000,false,(uint)pid);if(process==0){if(Marshal.GetLastWin32Error()==87)return false;throw new InvalidOperationException("Owner death could not be verified; no ownership transfer is permitted.");}
        try{return Native.GetProcessTimes(process,out long actual,out _,out _,out _)&&actual==created&&Native.WaitForSingleObject(process,0)!=0;}finally{Native.CloseHandle(process);}
    }
    static long Created(int pid)
    {
        nint process=Native.OpenProcess(0x1000,false,(uint)pid);Native.Check(process!=0,"Read layer owner identity");try{Native.Check(Native.GetProcessTimes(process,out long created,out _,out _,out _),"Read layer owner creation");return created;}finally{Native.CloseHandle(process);}
    }
    internal static int Run(string folder,string desktop,int owner)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(folder).StartsWith(root,StringComparison.OrdinalIgnoreCase)||!desktop.StartsWith("VIAVW_",StringComparison.Ordinal)||desktop.Length!=38)throw new ArgumentException("Use an owned private layer.");
        nint job=Native.OpenJobObjectW(0xD,false,desktop+"_job"),desk=Native.OpenDesktopW(desktop,0,false,1);
        Native.Check(job!=0&&desk!=0,"Hold existing owned job and private desktop");
        string pipe="VIAVW_keeper_"+Guid.NewGuid().ToString("N"),epoch=Guid.NewGuid().ToString("N");long ownerCreated=Created(owner);JsonElement? snapshot=null;bool stopped=false;
        void Publish()
        {
            string path=Path.Combine(folder,"keeper.json");SnapshotFiles.ReplaceTextAsync(path,JsonSerializer.Serialize(new{schema=1,pipe,epoch,pid=Environment.ProcessId,createdFileTime=Created(Environment.ProcessId),desktop,job=desktop+"_job",ownerPid=owner,ownerCreated,snapshot},new JsonSerializerOptions{WriteIndented=true}),maxRetries:50).GetAwaiter().GetResult();
        }
        try
        {
            bool published=false;
            while(!stopped)
            {
                using var server=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                if(!published){Publish();published=true;}
                using var idle=new CancellationTokenSource(1000);
                try{server.WaitForConnectionAsync(idle.Token).GetAwaiter().GetResult();}
                catch(OperationCanceledException)
                {
                    if(owner==0||!IdentityAlive(owner,ownerCreated))
                    {
                        var known=snapshot?.GetProperty("infrastructure").EnumerateArray().ToDictionary(v=>v.GetProperty("pid").GetInt32(),v=>v.GetProperty("created").GetInt64())??[];
                        bool appsRemain=false;
                        foreach(int pid in Native.JobProcessIds(job))
                        {
                            if(!known.TryGetValue(pid,out long expected)){appsRemain=snapshot!=null;break;}
                            try{if(Created(pid)!=expected){appsRemain=true;break;}}catch{appsRemain=true;break;}
                        }
                        if(!appsRemain){Native.TerminateJobObject(job,0);break;}
                    }
                    continue;
                }
                using var connection=new DurableLayerTransport.Connection(server);var reader=connection.Reader;var writer=connection.Writer;
                try
                {
                    Native.Check(Native.GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(),out uint caller),"Identify local keeper caller");
                    string? line=reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();if(line==null)continue;if(line.Length>65536)throw new ArgumentException("Bound keeper metadata.");
                    using var request=JsonDocument.Parse(line);var r=request.RootElement;string op=r.GetProperty("op").GetString()!;
                    if(op=="attach")
                    {
                        if(owner!=0&&IdentityAlive(owner,ownerCreated))throw new InvalidOperationException("Layer already has a live owner; disconnecting a client does not transfer ownership.");
                        if(snapshot==null)throw new InvalidOperationException("Layer startup metadata is incomplete; no application is relaunched.");
                        owner=(int)caller;ownerCreated=Created(owner);Publish();
                    }
                    else if(owner!=(int)caller||!IdentityAlive(owner,ownerCreated))throw new InvalidOperationException("Only the exact current owner may change or stop this layer.");
                    if(op=="launch-application")
                    {
                        if(snapshot is {} existing&&existing.GetProperty("applicationPid").GetInt32()!=0)throw new InvalidOperationException("One application per retained layer; no relaunch on reconnect.");
                        var launch=r.GetProperty("snapshot");string executable=launch.GetProperty("executable").GetString()!;
                        if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable))throw new ArgumentException("Use the installed absolute application executable.");
                        string[] args=launch.GetProperty("arguments").EnumerateArray().Select(v=>v.GetString()!).ToArray();if(args.Length>64||args.Any(v=>v.Length>4096))throw new ArgumentException("Bound launch arguments.");
                        string directory=launch.GetProperty("directory").GetString()!;
                        var environment=launch.GetProperty("environment").ValueKind==JsonValueKind.Object?launch.GetProperty("environment").Deserialize<Dictionary<string,string>>():null;
                        var child=Native.LaunchSuspended(executable,args,"WinSta0\\"+desktop,directory,environment);bool running=false;
                        try
                        {
                            Native.Check(Native.AssignProcessToJobObject(job,child.hProcess),"Assign application before its first instruction");
                            Native.Check(Native.GetProcessTimes(child.hProcess,out long created,out _,out _,out _),"Read application creation identity");
                            Native.Check(Native.ResumeThread(child.hThread)!=uint.MaxValue,"Run retained application");running=true;
                            if(snapshot is {} prior)
                            {
                                var fields=prior.Deserialize<Dictionary<string,JsonElement>>()!;fields["applicationPid"]=JsonSerializer.SerializeToElement((int)child.dwProcessId);fields["applicationDirectory"]=JsonSerializer.SerializeToElement(directory);snapshot=JsonSerializer.SerializeToElement(fields);Publish();
                            }
                            writer.WriteLine(JsonSerializer.Serialize(new{ok=true,data=new{applicationPid=(int)child.dwProcessId,created}}));continue;
                        }
                        catch{if(!running)Native.TerminateProcess(child.hProcess,3);throw;}
                        finally{Native.CloseHandle(child.hThread);Native.CloseHandle(child.hProcess);}
                    }
                    if(op=="update")
                    {
                        var incoming=r.GetProperty("snapshot");
                        if(snapshot is {} current&&current.GetProperty("applicationPid").GetInt32()!=0)
                        {
                            var fields=incoming.Deserialize<Dictionary<string,JsonElement>>()!;fields["applicationPid"]=current.GetProperty("applicationPid").Clone();fields["applicationDirectory"]=current.GetProperty("applicationDirectory").Clone();snapshot=JsonSerializer.SerializeToElement(fields);
                        }
                        else snapshot=incoming.Clone();
                        Publish();
                    }
                    else if(op=="release"){owner=0;ownerCreated=0;Publish();}
                    else if(op=="stop")
                    {
                        bool force=r.TryGetProperty("force",out var f)&&f.ValueKind==JsonValueKind.True;
                        var allowed=snapshot?.GetProperty("infrastructure").EnumerateArray().ToDictionary(v=>v.GetProperty("pid").GetInt32(),v=>v.GetProperty("created").GetInt64())??[];
                        if(!force&&Native.JobProcessIds(job).Any(pid=>!allowed.TryGetValue(pid,out long expected)||Created(pid)!=expected))throw new InvalidOperationException("Application processes remain; ordinary keeper stop refuses to discard them.");
                        if(force)Native.Check(Native.TerminateJobObject(job,0),"Explicitly close owned application job");stopped=true;
                    }
                    else if(op is not ("attach" or "update" or "release" or "ping"))throw new InvalidOperationException("Unknown keeper operation.");
                    writer.WriteLine(JsonSerializer.Serialize(new{ok=true,data=new{desktop,job=desktop+"_job",snapshot,keeperPid=Environment.ProcessId}}));
                }
                catch(Exception error)
                {try{writer.WriteLine(JsonSerializer.Serialize(new{ok=false,error=error.Message}));}catch(IOException){}}
            }
            return 0;
        }
        finally{Native.CloseHandle(job);Native.CloseDesktop(desk);}
    }
}
internal static partial class Native
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern nint OpenJobObjectW(uint access,bool inherit,string name);
    [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateJobObject(nint job,uint exit);
    [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetNamedPipeServerProcessId(nint pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetNamedPipeClientProcessId(nint pipe,out uint pid);
}

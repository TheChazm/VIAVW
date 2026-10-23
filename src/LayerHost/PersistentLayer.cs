using System.Diagnostics;
using System.Text.Json;
namespace LayerHost;
public sealed partial class ApplicationLayer
{
    LayerKeeperClient? keeper;
    System.Threading.Timer? keeperWatch;string? keeperLocator;int keeperRepairAttempts;
    internal string? KeeperDescriptor=>keeperLocator??keeper?.Descriptor;
    internal int? KeeperPid=>keeper?.Pid;
    internal bool KeeperReady=>!durableMode||keeper?.IsAlive==true;
    internal void RepairKeeper(){lock(gate){if(keeper?.IsAlive==true)return;keeperRepairAttempts=0;SpawnKeeper(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(keeper!.Descriptor)!)!,"guardian-replacement-"+Guid.NewGuid().ToString("N")));}}
    internal void ResearchStopKeeper(){lock(gate){if(keeper==null)throw new InvalidOperationException("No keeper");keeper.StopForVerification(job);}}
    internal void EnableKeeper(string folder)
    {lock(gate){if(keeper!=null)throw new InvalidOperationException("Keeper already attached.");SpawnKeeper(folder);StartKeeperWatch();}}
    void SpawnKeeper(string folder)
    {
        GpuBridgeVerification.PrepareOutput(folder);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string arg in new[]{"--layer-keeper",folder,Id,Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Keeper did not start.");
        int pid=process.Id;long created=process.StartTime.ToFileTimeUtc();
        var next=LayerKeeperClient.Wait(folder,pid);keeper?.Dispose();keeper=next;infrastructureIdentity[pid]=created;
        keeperLocator=Path.Combine(Path.GetDirectoryName(folder)!,"keeper-current.json");PersistKeeper();
        SnapshotFiles.ReplaceTextAsync(keeperLocator,JsonSerializer.Serialize(new{keeperDescriptor=keeper.Descriptor}),maxRetries:50).GetAwaiter().GetResult();
    }
    void StartKeeperWatch()
    {
        keeperWatch=new System.Threading.Timer(_=>
        {
            lock(gate)
            {
                if(closed||keeper==null||keeper.IsAlive||keeperRepairAttempts>=3)return;
                keeperRepairAttempts++;
                try{SpawnKeeper(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(keeper.Descriptor)!)!,"guardian-replacement-"+Guid.NewGuid().ToString("N")));keeperRepairAttempts=0;}
                catch(Exception error){AttentionReason="Keeper repair failed; application is retained by this owner. "+error.Message;}
            }
        },null,1000,1000);
    }
    void PersistKeeper(ControlOwner? nextOwner=null,long? nextRevision=null)
    {
        if(keeper==null)return;
        var members=Native.JobProcessIds(job).Concat(workerJob==0?[]:Native.JobProcessIds(workerJob)).Concat(keeper.IsAlive?new[]{keeper.Pid}:[]).ToHashSet();foreach(int old in infrastructureIdentity.Keys.Where(pid=>!members.Contains(pid)).ToArray())infrastructureIdentity.Remove(old);
        foreach(int old in workerLifetimes.OrderBy(p=>p.Value.Created).Take(Math.Max(0,workerLifetimes.Count-32)).Select(p=>p.Key).ToArray())if(old!=WorkerProcessId)workerLifetimes.Remove(old);
        keeper.Call("update",new{applicationPid=ApplicationProcessId,applicationDirectory,workerPid=WorkerProcessId,workerCreated=workerLifetimes.GetValueOrDefault(WorkerProcessId).Created,owner=(nextOwner??owner).ToString(),revision=nextRevision??ownershipRevision,persistentInputRequested,lowLatencyInputRequested,preciseMouseWaitsRequested,infrastructure=infrastructureIdentity.Select(p=>new{pid=p.Key,created=p.Value}).ToArray(),createRecord=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(keeper.Descriptor)!)!,"create.json")});
    }
    void ReleaseKeeper()
    {
        if(keeper==null)return;
        if(job==0){try{keeper.Call("release");}catch(Exception error){Debug.WriteLine(error.Message);}return;}
        try
        {
            var members=Native.JobProcessIds(job);RefreshInfrastructure(members);PersistKeeper();
            if(members.Except(infrastructure).Any())keeper.Call("release");else keeper.Call("stop");
        }
        catch(Exception error){System.Diagnostics.Debug.WriteLine("Keeper detach retained application: "+error.Message);try{keeper.Call("release");}catch(Exception releaseError){System.Diagnostics.Debug.WriteLine(releaseError.Message);}}
    }
    internal JsonElement ConfigureRecoveredInput(bool lowLatency,bool preciseMouseWaits)
    {
        lock(gate)
        {
            if(!PreserveOnWorkerFault||!recoveryPending||workerFaulted)throw new InvalidOperationException("Only configure a paused recovered layer.");
            var state=CallLocked(new("persistent-private-input",LowLatencyInput:lowLatency,PreciseMouseWaits:preciseMouseWaits));
            persistentInputRequested=true;lowLatencyInputRequested=lowLatency;preciseMouseWaitsRequested=preciseMouseWaits;PersistKeeper();return state;
        }
    }
    internal void Destroy()
    {
        lock(gate)
        {
            if(closed)return;
            if(job!=0)Native.Check(Native.TerminateJobObject(job,0),"Explicitly terminate only owned layer job");
            if(keeper!=null)
            {
                try{keeper.Call("stop",force:true);}catch(Exception error){Debug.WriteLine(error.Message);keeper.StopForVerification(job);}
                keeper.Dispose();keeper=null;
            }
            Dispose();
        }
    }
    internal ApplicationLayer(string descriptor)
    {
        researchMtaWorker=false;durableMode=true;PreserveOnWorkerFault=true;recoveryPending=true;
        keeperLocator=descriptor;keeper=LayerKeeperClient.Load(descriptor);var attachment=keeper.Call("attach");var data=attachment.GetProperty("snapshot");
        Id=attachment.GetProperty("desktop").GetString()!;
        try
        {
            job=Native.OpenJobObjectW(0xD,false,attachment.GetProperty("job").GetString()!);Native.Check(job!=0,"Reopen retained exact layer job");
            desktop=Native.OpenDesktopW(Id,0,false,Native.DESKTOP_ACCESS);Native.Check(desktop!=0,"Reopen retained private desktop");
            ApplicationProcessId=data.GetProperty("applicationPid").GetInt32();
            if(ApplicationProcessId==0||!Native.JobProcessIds(job).Contains(ApplicationProcessId))throw new InvalidOperationException("Original application root is no longer alive; no application is relaunched.");
            foreach(var entry in data.GetProperty("infrastructure").EnumerateArray())infrastructureIdentity[entry.GetProperty("pid").GetInt32()]=entry.GetProperty("created").GetInt64();
            int oldWorker=data.GetProperty("workerPid").GetInt32();long created=data.GetProperty("workerCreated").GetInt64();
            if(Native.JobProcessIds(job).Contains(oldWorker))
            {
                nint old=Native.OpenProcess(0x0001|0x1000|0x100000,false,(uint)oldWorker);Native.Check(old!=0,"Open retained worker identity");
                try{Native.Check(Native.GetProcessTimes(old,out long actual,out _,out _,out _)&&actual==created&&Native.IsProcessInJob(old,job,out bool member)&&member,"Validate exact old worker before replacing");Native.Check(Native.TerminateProcess(old,3),"Replace only retained worker");Native.WaitForSingleObject(old,1000);}finally{Native.CloseHandle(old);}
            }
            applicationDirectory=data.GetProperty("applicationDirectory").GetString();owner=Enum.Parse<ControlOwner>(data.GetProperty("owner").GetString()!);ownershipRevision=data.GetProperty("revision").GetInt64()+1;
            persistentInputRequested=data.GetProperty("persistentInputRequested").GetBoolean();lowLatencyInputRequested=data.GetProperty("lowLatencyInputRequested").GetBoolean();preciseMouseWaitsRequested=data.GetProperty("preciseMouseWaitsRequested").GetBoolean();
            StartWorker();RefreshInfrastructure(Native.JobProcessIds(job));
            if(applicationDirectory!=null)CallLocked(new("configure",Directory:applicationDirectory));
            if(persistentInputRequested)CallLocked(new("persistent-private-input",LowLatencyInput:lowLatencyInputRequested,PreciseMouseWaits:preciseMouseWaitsRequested));
            AttentionReason="Owner replaced; observe and reconcile actual application state before input.";PersistKeeper();StartKeeperWatch();
        }
        catch{Dispose();throw;}
    }
}
internal sealed record RecoveredLayerSeed(string SessionId,JsonElement Request,string Keeper,string Artifacts,string ReportDirectory)
{
    internal static RecoveredLayerSeed Load(string descriptor)
    {
        using var client=LayerKeeperClient.Load(descriptor);using var keeper=JsonDocument.Parse(File.ReadAllText(client.Descriptor));
        string file=keeper.RootElement.GetProperty("snapshot").GetProperty("createRecord").GetString()!;
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(file).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid owned launch record.");
        using var record=JsonDocument.Parse(File.ReadAllText(file));return new(record.RootElement.GetProperty("sessionId").GetString()!,record.RootElement.GetProperty("request").Clone(),descriptor,Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(file)!)!,"observations"),Path.GetDirectoryName(file)!);
    }
}

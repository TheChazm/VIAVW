using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
namespace LayerHost;
internal static class WindowEventVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var monitor=new ResearchDesktopMonitor();
        var app=manager.CreateLayer();var peer=manager.CreateLayer();var checks=new List<object>();var cycles=new List<object>();string? failure=null;int[] owned=[];
        void Check(string name,bool value){checks.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)throw new InvalidOperationException(name);}
        void Wait(Func<bool> test,string why){var t=Stopwatch.StartNew();while(!test()){if(t.ElapsedMilliseconds>5000)throw new TimeoutException(why);Thread.Sleep(10);}}
        WindowInfo Root(ApplicationLayer layer)=>layer.GetPresentationWindows().Single(w=>w.Parent==0&&w.Visible&&w.ClassName=="7-Zip::FM");
        WindowInfo OpenCopy(ApplicationLayer layer,long root)
        {
            layer.SendQueueStateChord(root,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:24);
            layer.SendQueueStateChord(root,[0x74],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);
            WindowInfo? modal=null;Wait(()=>{modal=layer.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770");return modal!=null;},"Actual Copy dialog missing");return modal!;
        }
        void Cancel(ApplicationLayer layer,WindowInfo modal){layer.PostKey(modal.Handle,0x1B,followPrivateFocus:true);Wait(()=>!layer.GetPresentationWindows().Any(w=>w.Handle==modal.Handle&&w.Visible),"Copy dialog did not close");}
        try
        {
            string archive=Path.Combine(output,"source.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){using var file=new StreamWriter(zip.CreateEntry("disposable.txt").Open());file.Write("Owned event experiment");}
            foreach(var layer in new[]{app,peer})
            {
                layer.LaunchApplication(@"C:\Program Files\7-Zip\7zFM.exe",[archive]);Wait(()=>layer.GetPresentationWindows().Any(w=>w.Parent==0&&w.Visible&&w.ClassName=="7-Zip::FM"),"Installed archive not ready");
                layer.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:true);var list=layer.GetPresentationWindows().First(w=>w.ClassName=="SysListView32");
                layer.SendQueueMouseSequence(list.Handle,[new(MouseMessage.LeftDown,70,54,1),new(MouseMessage.LeftUp,70,54)],primeCapture:true,posted:true);
            }
            long root=Root(app).Handle,peerRoot=Root(peer).Handle;owned=app.GetProcessIds().Concat(peer.GetProcessIds()).ToArray();
            app.ResearchWindowEvents(root,"start");Thread.Sleep(100);
            var peerModal=OpenCopy(peer,peerRoot);Thread.Sleep(50);var before=app.ResearchWindowEvents(root,"read");
            Check("subscription records only exact first application PID",before.GetProperty("pid").GetInt32()==app.ApplicationProcessId);
            Check("peer modal HWND is absent from event records",!before.GetProperty("records").EnumerateArray().Any(e=>e.GetProperty("window").GetInt64()==peerModal.Handle));Cancel(peer,peerModal);
            int initial=before.GetProperty("events").GetArrayLength();var modal=OpenCopy(app,root);JsonElement actual=default;
            Wait(()=>{actual=app.ResearchWindowEvents(root,"read");return actual.GetProperty("events").GetArrayLength()>initial;},"Owned application emitted no observed events");
            Check("actual owned dialog produces passive notifications",actual.GetProperty("records").EnumerateArray().Any(e=>e.GetProperty("window").GetInt64()==modal.Handle));
            Cancel(app,modal);var stopped=app.ResearchWindowEvents(root,"stop");File.WriteAllText(Path.Combine(output,"observed-events.json"),stopped.GetRawText());
            Check("subscription is removed on request",stopped.GetProperty("removed").GetBoolean()&&stopped.GetProperty("error").ValueKind==JsonValueKind.Null);
            using var worker=Process.GetProcessById(app.WorkerProcessId);int beforeHandles=worker.HandleCount;
            for(int i=0;i<16;i++)
            {
                var clock=Stopwatch.StartNew();app.ResearchWindowEvents(root,"start");var removed=app.ResearchWindowEvents(root,"stop");
                Check("observer lifecycle "+i,removed.GetProperty("registered").GetBoolean()&&removed.GetProperty("removed").GetBoolean()&&removed.GetProperty("error").ValueKind==JsonValueKind.Null);
                cycles.Add(new{i,milliseconds=clock.Elapsed.TotalMilliseconds});
            }
            worker.Refresh();int beforeReclamation=worker.HandleCount;var reclaimed=app.ResearchEventReclamation(root);
            File.WriteAllText(Path.Combine(output,"resource-observation.json"),JsonSerializer.Serialize(new{beforeHandles,beforeReclamation,reclaimed,cycles,note="Every native subscription was explicitly removed. Explicit research-only collection tests whether diagnostic thread-wrapper handles are reclaimable; production does not force collection."}));
            Check("diagnostic thread handles are reclaimable",reclaimed.GetProperty("handles").GetInt32()<=beforeHandles+8);
            Check("both ordinary applications remain alive",app.GetProcessIds().Contains(app.ApplicationProcessId)&&peer.GetProcessIds().Contains(peer.ApplicationProcessId));
            Check("ordinary graceful close succeeds",app.CloseGracefully(TimeSpan.FromSeconds(3))&&peer.CloseGracefully(TimeSpan.FromSeconds(3)));
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);}
        finally{manager.Dispose();}
        bool cleanup=owned.All(pid=>!CompatibilityVerification.Alive(pid));var physical=monitor.Complete(owned);bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"window-events-verification.json"),JsonSerializer.Serialize(new{passed,failure,checks,cycles,cleanup,owned,physical},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

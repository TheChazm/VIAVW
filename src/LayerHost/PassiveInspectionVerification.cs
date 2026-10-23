using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace LayerHost;

internal static class PassiveInspectionVerification
{
    internal static int Run(string output)
    {
        bool cleaned=false;
        if(Directory.Exists(output))throw new IOException("Use a fresh evidence directory.");Directory.CreateDirectory(output);
        var checks=new List<object>();var timings=new List<object>();var pids=new HashSet<int>();string? failure=null;
        using var manager=new LayerManager();ApplicationLayer? app=null,peer=null;
        void Check(string name,bool passed){checks.Add(new{name,passed});Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed)throw new InvalidOperationException(name);}
        void Wait(Func<bool> predicate,int milliseconds=5000){var watch=Stopwatch.StartNew();while(!predicate()){if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException("Application state did not become ready");Thread.Sleep(20);}}
        JsonElement Poll(PassiveInspection service,long ticket){JsonElement value=default;Wait(()=>{value=service.Poll(ticket);return value.GetProperty("status").GetString() is "complete" or "timed_out" or "failed";});return value;}
        Request Read(ApplicationLayer layer,long root)=>new("accessible-observation",Window:root,AllowedProcessIds:layer.GetProcessIds());
        try
        {
            string archive=Path.Combine(output,"sample.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("greeting.txt").Open());writer.Write("Disposable passive inspection verification.");}
            string exe=@"C:\Program Files\7-Zip\7zFM.exe";app=manager.CreateLayer();app.LaunchApplication(exe,[archive]);peer=manager.CreateLayer();peer.LaunchApplication(exe,[archive]);
            foreach(int ownedPid in app.GetProcessIds().Concat(peer.GetProcessIds()))pids.Add(ownedPid);
            WindowInfo? main=null,peerMain=null;Wait(()=>{main=app.GetPresentationWindows().SingleOrDefault(w=>w.Parent==0&&w.Visible&&w.ClassName=="7-Zip::FM");peerMain=peer.GetPresentationWindows().SingleOrDefault(w=>w.Parent==0&&w.Visible&&w.ClassName=="7-Zip::FM");return main!=null&&peerMain!=null;});
            foreach(int pid in app.GetProcessIds().Concat(peer.GetProcessIds()))pids.Add(pid);
            app.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:true);peer.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:true);
            using(var healthy=new PassiveInspection(app.Id))
            {
                int firstPid=0;
                for(int i=0;i<6;i++)
                {
                    var watch=Stopwatch.StartNew();long ticket=healthy.Begin(Read(app,main!.Handle));double begin=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();var initial=healthy.Poll(ticket);double poll=watch.Elapsed.TotalMilliseconds;
                    var result=Poll(healthy,ticket);int helper=result.GetProperty("workerPid").GetInt32();pids.Add(helper);
                    if(i==0)firstPid=helper;else Check("reuse helper "+i,helper==firstPid);
                    Check("actual installed 7-Zip UIA "+i,result.GetProperty("status").GetString()=="complete"&&result.GetProperty("result").GetProperty("nodes").GetArrayLength()>0);
                    timings.Add(new{phase="healthy",i,beginMilliseconds=begin,pollMilliseconds=poll,result});
                    Check("nonblocking begin and poll "+i,begin<100&&poll<100);
                }
                long foreign=healthy.Begin(Read(app,peerMain!.Handle));var denied=Poll(healthy,foreign);
                Check("foreign private desktop rejected before UIA",denied.GetProperty("status").GetString()=="failed");
            }
            foreach(var ready in new[]{app,peer})
            {
                var list=ready.GetPresentationWindows().First(w=>w.ClassName=="SysListView32");
                ready.SendQueueMouseSequence(list.Handle,[new(MouseMessage.LeftDown,70,54,1),new(MouseMessage.LeftUp,70,54)],primeCapture:true,posted:true);
            }
            using(var stuck=new PassiveInspection(app.Id,stall:true))
            {
                var watch=Stopwatch.StartNew();long ticket=stuck.Begin(Read(app,main!.Handle));double begin=watch.Elapsed.TotalMilliseconds;
                JsonElement running=default;Wait(()=>{running=stuck.Poll(ticket);return running.GetProperty("workerPid").GetInt32()!=0;});pids.Add(running.GetProperty("workerPid").GetInt32());
                Check("fault helper is pending",running.GetProperty("status").GetString()=="pending");
                watch.Restart();app.SendQueueStateChord(main!.Handle,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);app.SendQueueStateChord(main.Handle,[0x74],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);double input=watch.Elapsed.TotalMilliseconds;
                WindowInfo? dialog=null;Wait(()=>{dialog=app.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770");return dialog!=null;});
                Check("actual Copy dialog opens during stalled inspection",dialog!.Text.Contains("Copy",StringComparison.OrdinalIgnoreCase));
                app.PostKey(dialog.Handle,0x1B,followPrivateFocus:true);
                Wait(()=>!app.GetPresentationWindows().Any(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770"));
                watch.Restart();peer.SendQueueStateChord(peerMain!.Handle,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);peer.SendQueueStateChord(peerMain.Handle,[0x74],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);double peerInput=watch.Elapsed.TotalMilliseconds;
                Wait(()=>peer.GetPresentationWindows().Any(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770"));
                var peerDialog=peer.GetPresentationWindows().First(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770");peer.PostKey(peerDialog.Handle,0x1B,followPrivateFocus:true);
                var stopped=Poll(stuck,ticket);File.WriteAllText(Path.Combine(output,"fault.json"),stopped.GetRawText());
                Check("stalled helper reaches bounded deadline",stopped.GetProperty("status").GetString()=="timed_out"&&stopped.GetProperty("milliseconds").GetDouble()<2500);
                Check("same application and peer survive inspection timeout",!app.Closed&&!peer.Closed&&app.GetProcessIds().Contains(app.ApplicationProcessId)&&peer.GetProcessIds().Contains(peer.ApplicationProcessId));
                Check("input avoids inspection deadline",input<300&&peerInput<300&&begin<100);
                timings.Add(new{phase="stalled",beginMilliseconds=begin,inputMilliseconds=input,peerInputMilliseconds=peerInput,result=stopped});
            }
            var recovering=app.BeginAccessibleInspection(main!.Handle);JsonElement recovered=default;Wait(()=>{recovered=app.PollAccessibleInspection(recovering);return recovered.GetProperty("status").GetString()!="pending"&&recovered.GetProperty("status").GetString()!="partial";});pids.Add(recovered.GetProperty("workerPid").GetInt32());
            Check("fresh inspection works after isolated stall",recovered.GetProperty("status").GetString()=="complete");
            app.SendQueueStateChord(main.Handle,[0x74],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);Wait(()=>app.GetPresentationWindows().Any(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770"));
            var after=app.GetPresentationWindows().First(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770");app.PostKey(after.Handle,0x1B,followPrivateFocus:true);
            Wait(()=>!app.GetPresentationWindows().Any(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770"));
            Check("same application still responds after timeout",!app.Closed);
            Check("graceful close both ordinary apps",app.CloseGracefully(TimeSpan.FromSeconds(3))&&peer.CloseGracefully(TimeSpan.FromSeconds(3)));
        }
        catch(Exception error){failure=error.ToString();Console.WriteLine(failure);}
        finally
        {
            manager.Dispose();Thread.Sleep(200);var survivors=pids.Where(pid=>{try{return !Process.GetProcessById(pid).HasExited;}catch(ArgumentException){return false;}}).ToArray();
            cleaned=survivors.Length==0;
            File.WriteAllText(Path.Combine(output,"inspection-verification.json"),JsonSerializer.Serialize(new{passed=failure==null&&survivors.Length==0,failure,checks,timings,ownedProcessIds=pids,survivors,providerDeadlineMilliseconds=PassiveInspection.ProviderDeadlineMilliseconds},new JsonSerializerOptions{WriteIndented=true}));
        }
        return failure==null&&cleaned?0:1;
    }
}

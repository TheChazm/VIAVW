using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
namespace LayerHost;
internal static class InitialPointerVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var monitor=new ResearchDesktopMonitor();
        var app=manager.CreateLayer();var peer=manager.CreateLayer();var owned=new HashSet<int>();var checks=new List<object>();string? failure=null;object? recovery=null;
        void Check(string name,bool value){checks.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)throw new InvalidOperationException(name);}
        void Wait(Func<bool> test,string why){var clock=Stopwatch.StartNew();while(!test()){if(clock.ElapsedMilliseconds>5000)throw new TimeoutException(why);Thread.Sleep(10);}}
        try
        {
            string archive=Path.Combine(output,"source.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("disposable.txt").Open());writer.Write("Initial pointer recovery fixture");}
            foreach(var layer in new[]{app,peer})
            {
                layer.LaunchApplication(@"C:\Program Files\7-Zip\7zFM.exe",[archive]);foreach(int pid in layer.GetProcessIds())owned.Add(pid);
                Wait(()=>layer.GetPresentationWindows().Any(w=>w.ClassName=="7-Zip::FM"&&w.Parent==0&&w.Visible),"Installed archive did not become ready");layer.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:true);
            }
            var main=app.GetPresentationWindows().First(w=>w.ClassName=="7-Zip::FM"&&w.Parent==0&&w.Visible);int worker=app.WorkerProcessId;long focus=app.GetFocusedWindow(main.Handle);
            recovery=app.PrepareInitialPointer(main.Handle,new Point(main.Width/2,main.Height/2),simulateStall:true,budgetMilliseconds:120);var result=JsonSerializer.SerializeToElement(recovery);foreach(int pid in app.GetProcessIds())owned.Add(pid);
            Check("injected read deadline recovers only worker",!result.GetProperty("completed").GetBoolean()&&result.GetProperty("recovered").GetBoolean()&&app.WorkerProcessId!=worker);
            Check("recovery keeps original app and peer alive",app.GetProcessIds().Contains(app.ApplicationProcessId)&&peer.GetProcessIds().Contains(peer.ApplicationProcessId));
            Check("recovery preserves actual source HWND and focus",app.GetPresentationWindows().Any(w=>w.Handle==main.Handle&&w.Text==main.Text)&&app.GetFocusedWindow(main.Handle)==focus);
            Check("bounded initial-read recovery",result.GetProperty("milliseconds").GetDouble()<1500);
            var list=app.GetPresentationWindows().First(w=>w.ClassName=="SysListView32");app.SendQueueMouseSequence(list.Handle,[new(MouseMessage.LeftDown,70,54,1),new(MouseMessage.LeftUp,70,54)],primeCapture:true,posted:true);
            app.SendQueueStateChord(main.Handle,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:24);
            app.SendQueueStateChord(main.Handle,[0x74],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);
            WindowInfo? modal=null;Wait(()=>{modal=app.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.ClassName=="#32770");return modal!=null;},"Recovered app did not open actual Copy dialog");
            Check("actual application input works after recovery",modal!.Text.Contains("Copy",StringComparison.OrdinalIgnoreCase));
            int current=app.WorkerProcessId;bool denied=false;try{app.PrepareInitialPointer(main.Handle,new Point(10,10),simulateStall:true,budgetMilliseconds:120);}catch(InvalidOperationException){denied=true;}
            Check("preparation is rejected after any input attempt",denied&&app.WorkerProcessId==current&&app.GetPresentationWindows().Any(w=>w.Handle==modal.Handle));
            app.PostKey(modal.Handle,0x1B,followPrivateFocus:true);Wait(()=>!app.GetPresentationWindows().Any(w=>w.Handle==modal.Handle&&w.Visible),"Copy dialog did not cancel");
            Check("both installed apps close normally",app.CloseGracefully(TimeSpan.FromSeconds(3))&&peer.CloseGracefully(TimeSpan.FromSeconds(3)));
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);}
        finally{manager.Dispose();}
        bool cleanup=owned.All(pid=>!CompatibilityVerification.Alive(pid));var physical=monitor.Complete(owned.ToArray());bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"initial-pointer-verification.json"),JsonSerializer.Serialize(new{passed,failure,checks,recovery,cleanup,owned,physical,note="Only a pre-input read may restart its worker on timeout. Standard input/capture containment and no-replay rules remain unchanged. Recovery itself is not guaranteed when a new worker cannot start."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

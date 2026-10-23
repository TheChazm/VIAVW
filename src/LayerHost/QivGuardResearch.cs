using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class QivGuardResearch
{
    internal static long WaitRoot(ApplicationLayer layer,int pid)
    {
        var clock=Stopwatch.StartNew();do{var w=layer.GetWindows().FirstOrDefault(w=>w.ProcessId==pid&&w.Parent==0&&w.ClassName=="QuickImageViewer_"+layer.Id);if(w!=null)return w.Handle;Thread.Sleep(40);}while(clock.ElapsedMilliseconds<6000);throw new TimeoutException("No dedicated qIV root.");
    }
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var layer=manager.CreateLayer();using var physical=new ResearchDesktopMonitor();
        QivCursorGuardSession? guard=null;GpuBridgeResearchSession? gpu=null;var checks=new List<object>();var frames=new List<object>();bool passed=true,graceful=false;string? failure=null;int pid=0;long root=0;int[] owned=[];string registry=QivResearch.RegistryState();
        void Check(string name,bool value,object? detail=null){passed&=value;checks.Add(new{name,passed=value,detail});Console.WriteLine((value?"PASS ":"FAIL ")+name);}
        JsonElement Rejected(string command)
        {
            using var helper=layer.StartQivGuardHelper();var errors=helper.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(5000);
            try{helper.StandardInput.WriteLine(command);helper.StandardInput.Flush();string line=helper.StandardOutput.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult()??throw new InvalidOperationException("Missing negative guard result.");if(!helper.WaitForExit(1000))throw new TimeoutException("Negative installer exit deadline.");using var doc=JsonDocument.Parse(line);return doc.RootElement.Clone();}
            finally{if(!helper.HasExited){helper.Kill();helper.WaitForExit(1000);}}
        }
        JsonElement Capture(string name,bool second,bool shared=false)
        {
            string path=Path.Combine(output,name+".png");var clock=Stopwatch.StartNew();int attempts=0;JsonElement pixels=default;object? metrics=null;
            do{if(shared)metrics=gpu!.Capture(path,true);else{var f=layer.InspectHiddenWindow(root,true,0);File.WriteAllBytes(path,f.Png);metrics=new{f.CaptureMilliseconds,f.NativeRenderingMilliseconds,f.EncodingMilliseconds};}pixels=QivResearch.Pixels(path);attempts++;if(pixels.GetProperty(second?"magenta":"red").GetInt32()>1000)break;Thread.Sleep(40);}while(clock.ElapsedMilliseconds<3000);
            bool hidden=!layer.GetWindows().First(w=>w.Handle==root).Visible;
            Check(name,hidden&&pixels.GetProperty(second?"magenta":"red").GetInt32()>1000,new{pixels,attempts});frames.Add(new{name,shared,pixels,attempts,metrics,guard=guard!.State});return pixels;
        }
        try
        {
            string ini=QivResearch.Prepare(output,layer.Id);pid=layer.LaunchApplication(QivResearch.Executable,QivResearch.Arguments(output,ini),output);root=WaitRoot(layer,pid);
            long created=ProcessCensus.Read([pid]).Single().CreationFileTime!.Value;string mapping="Local\\VIAVW_QIV_GUARD_"+Guid.NewGuid().ToString("N");
            var wrongCreation=Rejected($"install\t{pid}\t{root}\t{created+1}\t{layer.Id}\t{mapping}");Check("native installer rejects changed creation identity",!wrongCreation.GetProperty("ok").GetBoolean()&&wrongCreation.GetProperty("stage").GetString()=="source-creation-and-session",wrongCreation);
            var inputDesktop=Rejected($"install\t{pid}\t{root}\t{created}\tDefault\t{mapping}");Check("native installer rejects input desktop",!inputDesktop.GetProperty("ok").GetBoolean(),inputDesktop);
            using(var wrong=manager.CreateLayer())
            {
                string folder=Path.Combine(output,"rejected-target");Directory.CreateDirectory(folder);int wrongPid=wrong.LaunchApplication(Program.FindProbe(),workingDirectory:folder);
                var refusal=Rejected($"install\t{wrongPid}\t0\t0\t{wrong.Id}\t{mapping}");Check("native installer rejects ordinary executable",!refusal.GetProperty("ok").GetBoolean()&&refusal.GetProperty("stage").GetString()=="source-path-and-hash",refusal);owned=owned.Concat(wrong.GetProcessIds()).Distinct().ToArray();
            }
            var clock=Stopwatch.StartNew();guard=new(layer,root);Check("guard-only installer exits without graphics modules or GPU bridge",!CompatibilityVerification.Alive(guard.InstallerPid)&&!guard.Setup.GetProperty("helperGraphicsModules").GetBoolean()&&!guard.Setup.GetProperty("gpuBridgeLoaded").GetBoolean(),new{guard.Setup,totalSetupMs=clock.Elapsed.TotalMilliseconds,guard.State});
            layer.HideLayer(root);var first=Capture("native-first",false);guard.Probe();Check("refusal probe increments installed-import counters",guard.State.GetProperty("clipRequests").GetInt32()==guard.State.GetProperty("clipSlots").GetInt32()&&guard.State.GetProperty("warpRequests").GetInt32()==guard.State.GetProperty("warpSlots").GetInt32(),guard.State);
            QivResearch.Wheel(layer,root,-120);Capture("native-next",true);layer.TakeHumanControl();bool rejected=false;try{QivResearch.Wheel(layer,root,120);}catch(InvalidOperationException){rejected=true;}Check("guard-only native input ownership",rejected);
            for(int i=0;i<3;i++)QivResearch.Wheel(layer,root,120,8,ControlOwner.Human);var zoomed=Capture("human-zoom",true);
            int clips=guard.State.GetProperty("clipRequests").GetInt32();layer.SendQueueMouseSequence(root,[new(MouseMessage.LeftDown,320,180,1),new(MouseMessage.Move,410,230,1),new(MouseMessage.LeftUp,410,230)],false,ControlOwner.Human);Capture("human-pan",true);
            Check("ordinary mouse confinement requests still denied",guard.State.GetProperty("clipRequests").GetInt32()>clips,guard.State);
            layer.RestartWorker();Capture("worker-recovered",true);Check("owner and application survive native worker recovery",layer.ApplicationProcessId==pid&&layer.Owner==ControlOwner.Human);
            for(int cycle=0;cycle<3;cycle++)
            {
                gpu=new(layer,root,ResearchGpuTarget.Qiv);Capture("shared-"+cycle,true,true);Check("GPU capture reuses separate verified guard "+cycle,gpu.CursorGuardState.GetProperty("installed").GetInt32()==1&&gpu.CursorGuardState.GetProperty("clipRequests").GetInt32()==0&&guard.Verify().GetProperty("gpuBridgeLoaded").GetBoolean());
                int reader=gpu.HelperPid;gpu.StopAdapter();gpu.Dispose();gpu=null;Capture("native-after-shared-"+cycle,true);Check("reader exits while standalone guard survives "+cycle,!CompatibilityVerification.Alive(reader)&&guard.Verify().GetProperty("ok").GetBoolean());
            }
            layer.ReturnToAutomation();QivResearch.Wheel(layer,root,120);Capture("automation-resumed",false);guard.Verify();
        }
        catch(Exception exception){passed=false;failure=exception.ToString();File.WriteAllText(Path.Combine(output,"failure.txt"),failure);}
        finally{gpu?.Dispose();if(!layer.Closed){owned=owned.Concat(layer.GetProcessIds()).Distinct().ToArray();try{graceful=layer.CloseGracefully(TimeSpan.FromSeconds(3));}catch{}}layer.Dispose();guard?.Dispose();Thread.Sleep(250);}
        bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));var observation=physical.Complete(owned);Check("registry unchanged and owned cleanup",cleanup&&registry==QivResearch.RegistryState(),new{cleanup,graceful});Check("sampled physical desktop containment",observation.GetProperty("passed").GetBoolean(),observation);
        File.WriteAllText(Path.Combine(output,"guard-research.json"),JsonSerializer.Serialize(new{passed,failure,checks,frames,pid,root,owned,cleanup,graceful,note="Standalone exact-process cursor denial; installer and DLL have no D3D/DXGI dependency. Probe calls only verified replacement functions. GPU sharing is added later and reuses the same guard; ordinary executable unchanged. Physical human acceptance pending."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

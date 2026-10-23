using System.Diagnostics;
using System.Text.Json;
namespace LayerHost;
internal static class VisualStudioPointResearch
{
    internal static int Run(string output,string setupFile,bool mta=false)
    {
        GpuBridgeVerification.PrepareOutput(output);var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        using var lease=VisualStudioFreshProfile.Lease(setup,setupFile);using var manager=new LayerManager();using var layer=mta?manager.CreateResearchMtaLayer():manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();
        string? failure=null;int[] owned=[];long root=0;var samples=new List<object>();JsonElement editor=default;object? baseline=null,hinted=null,scoped=null;bool sourceUnchanged=false,focusUnchanged=false,edited=false;
        void Wait(Func<bool> test,string why){var timer=Stopwatch.StartNew();while(!test()){if(timer.ElapsedMilliseconds>60000)throw new TimeoutException(why);Thread.Sleep(20);}}
        string Normalize(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ","");
        bool TextIs(string expected)
        {
            var read=layer.ObserveAccessible(root,"WpfTextView");
            editor=read.GetProperty("nodes").EnumerateArray().FirstOrDefault(n=>n.GetProperty("id").GetString()=="WpfTextView"&&!n.GetProperty("offscreen").GetBoolean()&&Normalize(n.GetProperty("text").GetString()??"")==expected);
            return editor.ValueKind==JsonValueKind.Object;
        }
        try
        {
            layer.LaunchApplication(setup.Executable,[..setup.Arguments.Where(a=>a!="/SafeMode"),setup.First],output);
            Wait(()=>{root=layer.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.Text.Contains("Greeting.cs",StringComparison.Ordinal))?.Handle??0;return root!=0;},"Installed editor did not start");
            string original=File.ReadAllText(setup.First);Wait(()=>TextIs(original),"Actual source text missing");
            var origin=layer.ClientOrigin(root);int x=(int)editor.GetProperty("x").GetDouble()+200-origin.X,y=(int)editor.GetProperty("y").GetDouble()+8-origin.Y;
            long focus=layer.GetFocusedWindow(root);
            var watch=Stopwatch.StartNew();var geometry=layer.ObservePointerGeometry(root,new(x,y),new(x,y));baseline=new{roundTripMilliseconds=watch.Elapsed.TotalMilliseconds,geometry};
            layer.RestartWorker();watch.Restart();var hint=layer.ObservePointerGeometry(root,new(x,y),new(x,y),"WpfTextView");hinted=new{roundTripMilliseconds=watch.Elapsed.TotalMilliseconds,geometry=hint};
            layer.RestartWorker();watch.Restart();var native=layer.HitTestPointer(root,x,y);double hitMilliseconds=watch.Elapsed.TotalMilliseconds;var scopeGeometry=layer.ObservePointerGeometry(root,new(x,y),new(x,y),scopeWindow:native.Window);scoped=new{roundTripMilliseconds=watch.Elapsed.TotalMilliseconds,hitMilliseconds,native,geometry=scopeGeometry};
            for(int trial=0;trial<8;trial++)
            {
                watch.Restart();long ticket=layer.BeginScopedPointInspection(root,x,y);JsonElement result=default;
                Wait(()=>{result=layer.PollScopedPointInspection(ticket);return result.GetProperty("status").GetString() is "complete" or "failed" or "timed_out";},"Scoped point read did not finish");
                samples.Add(new{trial,roundTripMilliseconds=watch.Elapsed.TotalMilliseconds,result});
            }
            focusUnchanged=layer.GetFocusedWindow(root)==focus;Wait(()=>TextIs(original),"Passive point reads altered source text");sourceUnchanged=true;
            layer.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:true);var hit=layer.HitTestPointer(root,x,y);
            layer.SendQueueMouseSequence(hit.Window,[new(MouseMessage.Move,hit.X,hit.Y),new(MouseMessage.LeftDown,hit.X,hit.Y,1),new(MouseMessage.LeftUp,hit.X,hit.Y)],primeCapture:true);
            layer.SendQueueStateChord(root,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:24);
            string marker="// Scoped point research preserves actual editing.";layer.PostKeyboardInput(root,marker,followPrivateFocus:true);Wait(()=>TextIs(marker),"Normal native fallback editing failed after passive probes");
            layer.SendQueueStateChord(root,[0x11,0x53],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:24);Wait(()=>File.ReadAllText(setup.First)==marker,"Actual saved source mismatch");edited=true;
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);}
        finally{if(!layer.Closed){owned=layer.GetProcessIds();try{layer.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}layer.Dispose();}
        bool cleanup=owned.All(pid=>!CompatibilityVerification.Alive(pid));var physical=monitor.Complete(owned);
        bool completed=failure==null&&cleanup&&sourceUnchanged&&focusUnchanged&&edited&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"point-research.json"),JsonSerializer.Serialize(new{completed,failure,mta,baseline,hinted,scoped,samples,sourceUnchanged,focusUnchanged,edited,cleanup,owned,physical,note="Scoped read-only MSAA classification, not a new input backend. Actual editing/saving uses the preserved native path. Cold helper startup and warm requests are reported separately."},new JsonSerializerOptions{WriteIndented=true}));return completed?0:1;
    }
}

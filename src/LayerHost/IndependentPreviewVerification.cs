using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// A controlled fixture is used here for deterministic capture stalls and
// lifecycle races. Installed Visual Studio supplies the compatibility evidence.
internal static class IndependentPreviewVerification
{
    internal static int Run(string output,bool windowDc=false,bool preciseMouseWaits=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var manager=new LayerManager();using var monitor=new ResearchDesktopMonitor();
        var checks=new Dictionary<string,bool>();var owned=new HashSet<int>();var timings=new List<object>();string? failure=null;
        void Check(string name,bool passed){checks[name]=passed;Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed)throw new InvalidOperationException(name);}
        byte[] Hash(Bitmap image)
        {
            var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{var bytes=new byte[image.Width*image.Height*4];for(int y=0;y<image.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,bytes,y*image.Width*4,image.Width*4);return SHA256.HashData(bytes);}
            finally{image.UnlockBits(data);}
        }
        void WaitText(ApplicationLayer layer,long edit,string expected)
        {
            var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<3000){if(layer.GetWindows().Single(w=>w.Handle==edit).Text==expected)return;Thread.Sleep(10);}throw new TimeoutException("Actual fixture edit did not match "+expected);
        }
        try
        {
            using(var layer=manager.CreateLayer())
            {
                layer.LaunchApplication(Program.FindProbe(),[],output);long root=CompatibilityVerification.WaitRoot(layer,"Disposable application").Handle;
                layer.EnablePrivateInputSession(lowLatency:true,preciseMouseWaits:preciseMouseWaits);long edit=layer.GetWindows().First(w=>w.ClassName.Contains("EDIT",StringComparison.OrdinalIgnoreCase)).Handle;
                layer.PostKeyboardInput(edit,"INITIAL");WaitText(layer,edit,"INITIAL");
                var initial=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc).GetAwaiter().GetResult();using var first=initial.Image;var initialHash=Hash(first);
                var captureOrigin=layer.ClientOrigin(root);Check("capture carries its source client origin",initial.Frame.SourceClientX==captureOrigin.X&&initial.Frame.SourceClientY==captureOrigin.Y);
                Check("actual private capture dimensions",first.Width>100&&first.Height>100);
                var inFlight=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc);layer.TakeHumanControl();bool rejected=false;
                try{layer.PostKeyboardInput(edit,"WRONG");}catch(InvalidOperationException){rejected=true;}
                Check("automation rejected while human owns an in-flight frame",rejected);
                layer.PostKeyboardInput(edit," HUMAN",ControlOwner.Human);WaitText(layer,edit,"INITIAL HUMAN");using var concurrent=inFlight.GetAwaiter().GetResult().Image;
                Check("human input completes during capture without changing ownership",layer.Owner==ControlOwner.Human);
                using(var synchronous=layer.CaptureSharedPreview(root,true).Image)Check("synchronous and asynchronous maps coexist",synchronous.Size==first.Size);
                Check("published bitmap remains immutable after other captures",Hash(first).SequenceEqual(initialHash));
                var a=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc);var b=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc);
                using(var one=a.GetAwaiter().GetResult().Image)using(var two=b.GetAwaiter().GetResult().Image)Check("concurrent callers complete through bounded serialized capture",one.Size==two.Size);
                var interruptedCapture=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc);layer.RestartWorker();
                bool interruptionContained=false;
                try{using var completed=interruptedCapture.GetAwaiter().GetResult().Image;interruptionContained=completed.Width>0;}
                catch(LayerOperationException error)when(error.Message=="Preview ticket is no longer current."){interruptionContained=!layer.Closed;}
                Check("in-flight capture restart leaves the application layer usable",interruptionContained);
                var state=layer.PrivateInputSessionState();
                Check("worker restart restores fast private input and human ownership",state.GetProperty("Enabled").GetBoolean()&&state.GetProperty("LowLatency").GetBoolean()&&layer.Owner==ControlOwner.Human);
                layer.PostKeyboardInput(edit," RESTART",ControlOwner.Human);WaitText(layer,edit,"INITIAL HUMAN RESTART");
                if(preciseMouseWaits)Check("worker restart restores precise pacing",layer.PrivateInputSessionState().GetProperty("PreciseMouseWaits").GetBoolean());
                using(var recovered=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc).GetAwaiter().GetResult().Image)Check("independent mapping recovers after worker restart",recovered.Size==first.Size);
                layer.ReturnToAutomation();layer.ResearchResizeWindow(root,900,600);
                using(var resized=layer.CaptureSharedPreviewAsync(root,windowDc:windowDc).GetAwaiter().GetResult().Image)Check("independent capture follows source resize",resized.Width!=first.Width||resized.Height!=first.Height);
                foreach(int id in layer.GetProcessIds())owned.Add(id);
                layer.Dispose();
                Check("owned normal capture tree released",owned.All(id=>!CompatibilityVerification.Alive(id)));
            }
            using(var stalled=manager.CreateLayer())
            using(var witness=manager.CreateLayer())
            {
                witness.LaunchApplication(Program.FindProbe(),[],output);CompatibilityVerification.WaitRoot(witness,"Disposable application");
                long witnessEdit=witness.GetWindows().First(w=>w.ClassName.Contains("EDIT",StringComparison.OrdinalIgnoreCase)).Handle;
                foreach(int id in witness.GetProcessIds())owned.Add(id);
                stalled.LaunchApplication(Program.FindProbe(),["--hang-print"],output);
                // A deliberately hung print can also prevent WM_GETTEXT and
                // completion of ShowWindow. Read stored captions/geometry only.
                long root=0;var startup=Stopwatch.StartNew();
                while(root==0&&startup.ElapsedMilliseconds<5000){root=stalled.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Width>100)?.Handle??0;Thread.Sleep(10);}
                if(root==0)throw new InvalidOperationException("Stall fixture has no owned top-level window.");
                long edit=stalled.GetPresentationWindows().First(w=>w.ClassName.Contains("EDIT",StringComparison.OrdinalIgnoreCase)).Handle;
                foreach(int id in stalled.GetProcessIds())owned.Add(id);
                var captureClock=Stopwatch.StartNew();var capture=stalled.CaptureSharedPreviewAsync(root);Thread.Sleep(150);
                var inputClock=Stopwatch.StartNew();stalled.PostKeyboardInput(edit,"QUEUED WHILE PRINT IS STALLED");double inputMilliseconds=inputClock.Elapsed.TotalMilliseconds;
                Check("posted input RPC remains responsive during a stalled print",inputMilliseconds<500);
                bool timeout=false;try{using var ignored=capture.GetAwaiter().GetResult().Image;}catch(TimeoutException){timeout=true;}
                timings.Add(new{stalledPrintInputRpcMilliseconds=inputMilliseconds,containmentMilliseconds=captureClock.Elapsed.TotalMilliseconds,note="The deliberately hung source cannot process the posted text. This check measures worker input submission and containment, not application responsiveness."});
                witness.PostKeyboardInput(witnessEdit,"OTHER LAYER STILL WORKS");WaitText(witness,witnessEdit,"OTHER LAYER STILL WORKS");
                Check("stalled background capture faults only its owned layer",timeout&&stalled.Closed&&!witness.Closed);
            }
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(failure);}
        finally{manager.Dispose();}
        var physical=monitor.Complete(owned.ToArray());bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));
        bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"independent-preview.json"),JsonSerializer.Serialize(new{passed,failure,windowDc,checks,timings,cleanup,owned,physical},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class VisualStudioSourceTimingResearch
{
    internal static int Run(string output,string setupFile,bool observeEvents,bool singleCharacter=false,bool synchronousChords=false,int preciseChordHoldMilliseconds=0,bool measureDcCosts=false,bool observeWindowEvents=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        using var lease=VisualStudioFreshProfile.Lease(setup,setupFile);
        using var manager=new LayerManager();using var layer=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();
        string? failure=null;long root=0;int[] owned=[];bool observing=false;object? eventStop=null;JsonElement? dcCosts=null;var trials=new List<object>();int regionX=0,regionY=0;int regionWidth=singleCharacter?64:20;int[]? glyphMask=null;int requiredPixels=30;using var pacing=new ResearchWaitableTimer();
        string Normalize(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ","");
        JsonElement[] Editors()=>layer.ObserveAccessible(root,"WpfTextView").GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("id").GetString()=="WpfTextView").Select(n=>n.Clone()).ToArray();
        void Wait(Func<bool> test,string reason,int timeout=30000){var t=Stopwatch.StartNew();do{if(test())return;Thread.Sleep(30);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException(reason);}
        JsonElement[] verifiedEditors=[];
        void TextIs(string expected)=>Wait(()=>{var sample=Editors();if(!sample.Any(n=>Normalize(n.GetProperty("text").GetString()??"")==expected))return false;verifiedEditors=sample;return true;},"Actual disposable IDE text did not match");
        JsonElement Events(string action)=>observeWindowEvents?layer.ResearchWindowEvents(root,action):action=="start"?layer.ResearchStartTextEvents(root):action=="stop"?layer.ResearchStopTextEvents(root):layer.ResearchReadTextEvents(root);
        byte[] Region(Bitmap image)
        {
            var result=new byte[regionWidth*14*4];var data=image.LockBits(new Rectangle(regionX,regionY,regionWidth,14),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{for(int y=0;y<14;y++)Marshal.Copy(data.Scan0+y*data.Stride,result,y*regionWidth*4,regionWidth*4);}finally{image.UnlockBits(data);}return result;
        }
        int Changed(byte[] before,byte[] after){int count=0;foreach(int index in glyphMask??Enumerable.Range(0,before.Length/4).ToArray()){int i=index*4;if(Math.Abs(before[i]-after[i])+Math.Abs(before[i+1]-after[i+1])+Math.Abs(before[i+2]-after[i+2])>75)count++;}return count;}
        string marker=singleCharacter?"W":"WMWMWMWMWMWMWMWMWMWM";
        try
        {
            string[] args=setup.Arguments.Where(a=>a!="/SafeMode").ToArray();int log=Array.IndexOf(args,"/Log");args[log+1]=Path.Combine(output,"ActivityLog.xml");
            layer.LaunchApplication(setup.Executable,[..args,setup.First],output);
            Wait(()=>{root=layer.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.Text.Contains("Greeting.cs",StringComparison.Ordinal))?.Handle??0;return root!=0;},"Installed IDE document not ready",90000);
            layer.EnablePrivateInputSession(lowLatency:true);TextIs(File.ReadAllText(setup.First));
            var editor=verifiedEditors.Where(n=>!n.GetProperty("offscreen").GetBoolean()).OrderByDescending(n=>n.GetProperty("width").GetDouble()*n.GetProperty("height").GetDouble()).First();
            var origin=layer.ClientOrigin(root);int x=(int)editor.GetProperty("x").GetDouble()-origin.X+200,y=(int)editor.GetProperty("y").GetDouble()-origin.Y+8;
            var hit=layer.HitTestPointer(root,x,y);layer.SendQueueMouseSequence(hit.Window,[new(MouseMessage.Move,hit.X,hit.Y),new(MouseMessage.LeftDown,hit.X,hit.Y,1),new(MouseMessage.LeftUp,hit.X,hit.Y)],primeCapture:true);
            for(int trial=0;trial<7;trial++)
            {
                layer.SendQueueStateChord(root,[0x11,0x41],posted:!synchronousChords,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:preciseChordHoldMilliseconds);layer.PostKeyboardInput(root,"//",followPrivateFocus:true);TextIs("//");Thread.Sleep(250);
                editor=verifiedEditors.First(n=>Normalize(n.GetProperty("text").GetString()??"")=="//");origin=layer.ClientOrigin(root);regionX=(int)editor.GetProperty("x").GetDouble()-origin.X+(singleCharacter?2:80);regionY=(int)editor.GetProperty("y").GetDouble()-origin.Y+(singleCharacter?1:3);
                if((observeEvents||observeWindowEvents)&&!observing){Events("start");observing=true;Thread.Sleep(250);}
                if(observing)
                {
                    long previous=Events("read").GetProperty("lastQpc").GetInt64();
                    Wait(()=>{Thread.Sleep(100);long now=Events("read").GetProperty("lastQpc").GetInt64();bool quiet=previous==now;previous=now;return quiet;},"Text event stream did not settle",3000);
                }
                var initial=layer.CaptureSharedPreviewAsync(root,windowDc:true).GetAwaiter().GetResult();byte[] baseline;
                using(initial.Image)baseline=Region(initial.Image);
                if(singleCharacter&&glyphMask==null)
                {
                    layer.PostKeyboardInput(root,"W",followPrivateFocus:true);TextIs("//W");Thread.Sleep(100);
                    var calibration=layer.CaptureSharedPreviewAsync(root,windowDc:true).GetAwaiter().GetResult();byte[] after;
                    using(calibration.Image){after=Region(calibration.Image);calibration.Image.Save(Path.Combine(output,"calibration-W.png"),ImageFormat.Png);}
                    glyphMask=SingleGlyphMask.Build(baseline,after,regionWidth,14);requiredPixels=(int)Math.Ceiling(glyphMask.Length*.75);
                    File.WriteAllText(Path.Combine(output,"glyph-calibration.json"),JsonSerializer.Serialize(new{regionX,regionY,regionWidth,height=14,glyphMask,requiredPixels}));
                    layer.SendQueueStateChord(root,[0x11,0x41],posted:!synchronousChords,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:preciseChordHoldMilliseconds);layer.PostKeyboardInput(root,"//",followPrivateFocus:true);TextIs("//");Thread.Sleep(100);
                    var reset=layer.CaptureSharedPreviewAsync(root,windowDc:true).GetAwaiter().GetResult();using(reset.Image)baseline=Region(reset.Image);
                }
                long start=Stopwatch.GetTimestamp();var clock=Stopwatch.StartNew();layer.PostKeyboardInput(root,marker,followPrivateFocus:true);double forwarded=clock.Elapsed.TotalMilliseconds;
                var captures=new List<object>();double? firstFresh=null;int changed=0;Bitmap? final=null;
                while(clock.ElapsedMilliseconds<1500)
                {
                    double before=clock.Elapsed.TotalMilliseconds;var capture=layer.CaptureSharedPreviewAsync(root,windowDc:true).GetAwaiter().GetResult();double returned=clock.Elapsed.TotalMilliseconds;
                    using(capture.Image){changed=Changed(baseline,Region(capture.Image));if(changed>=requiredPixels)final=new Bitmap(capture.Image);}
                    captures.Add(new{requestMilliseconds=before,returnMilliseconds=returned,changedPixels=changed,capture.Frame.CaptureMilliseconds});
                    if(changed>=requiredPixels){firstFresh=returned;break;}if(singleCharacter)pacing.Wait(1);else Thread.Sleep(1);
                }
                if(firstFresh==null)throw new TimeoutException("Source-only capture did not receive actual new glyph pixels");
                using(final){final!.Save(Path.Combine(output,$"trial-{trial}.png"),ImageFormat.Png);}
                Thread.Sleep(100);JsonElement? notifications=observing?Events("read"):null;
                double[] received=notifications?.GetProperty("events").EnumerateArray().Select(e=>e.GetInt64()).Where(q=>q>=start).Select(q=>(q-start)*1000.0/Stopwatch.Frequency).ToArray()??[];
                trials.Add(new{trial,startQpc=start,forwardedMilliseconds=forwarded,firstFreshMilliseconds=firstFresh,eventReceiptMilliseconds=received,captures,regionX,regionY});
                TextIs("//"+marker);
            }
            if(observing){eventStop=Events("stop");observing=false;}
            layer.SendQueueStateChord(root,[0x11,0x53],posted:!synchronousChords,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true,preciseChordHoldMilliseconds:preciseChordHoldMilliseconds);Wait(()=>File.ReadAllText(setup.First)=="//"+marker,"Source-only text did not save exactly");
            if(measureDcCosts)dcCosts=layer.ResearchMeasureDcCosts(root);
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);if(root!=0)try{File.WriteAllText(Path.Combine(output,"failure-editor.json"),layer.ObserveAccessible(root,"WpfTextView").GetRawText());}catch{}}
        finally
        {
            if(!layer.Closed){if(observing)try{eventStop=Events("stop");}catch{}owned=layer.GetProcessIds();try{layer.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}layer.Dispose();
        }
        var physical=monitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool completed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"source-timing.json"),JsonSerializer.Serialize(new{completed,failure,observeEvents,observeWindowEvents,singleCharacter,synchronousChords,preciseChordHoldMilliseconds,trials,eventStop,dcCosts,cleanup,owned,physical,note=(singleCharacter?"Seven individually submitted W characters":"Seven source-only bursts")+" with shared-DC capture polling and no viewer/report pipeline. Actual complete text and final saved file verified separately. Passive callback receipt is not a source-processing timestamp or a render acknowledgement. Observer registration and explicit UIA text reads are outside measured capture loops. A completed run may report zero callbacks."},new JsonSerializerOptions{WriteIndented=true}));return completed?0:1;
    }
}

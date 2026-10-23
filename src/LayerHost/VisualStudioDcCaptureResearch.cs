using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Separate capture experiment. No viewer/backend changes, window reparenting,
// styles, desktop switching or input devices. Test DC freshness before any
// PrintWindow call so a previously printed image cannot masquerade as live DC.
internal static class VisualStudioDcCaptureResearch
{
    internal static int Run(string output,string setupFile)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        using var lease=VisualStudioFreshProfile.Lease(setup,setupFile);
        using var manager=new LayerManager();using var layer=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();
        string? failure=null;int[] owned=[];long root=0;int regionX=0,regionY=0;object? findings=null;
        string Normalize(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ","");
        JsonElement[] Editors()=>layer.ObserveAccessible(root,"WpfTextView").GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("id").GetString()=="WpfTextView").Select(n=>n.Clone()).ToArray();
        void Wait(Func<bool> test,string reason,int timeout=30000){var t=Stopwatch.StartNew();do{if(test())return;Thread.Sleep(30);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException(reason);}
        void TextIs(string expected)=>Wait(()=>Editors().Any(n=>Normalize(n.GetProperty("text").GetString()??"")==expected),"Actual editor did not contain expected research text");
        Bitmap Decode(CaptureFrame frame){using var stream=new MemoryStream(frame.Png);using var decoded=Image.FromStream(stream);return new Bitmap(decoded);}
        Color[] Region(Bitmap image)=>Enumerable.Range(0,280).Select(i=>image.GetPixel(regionX+i%20,regionY+i/20)).ToArray();
        int Changed(Color[] baseline,Bitmap image)=>Region(image).Zip(baseline).Count(pair=>Math.Abs(pair.First.R-pair.Second.R)+Math.Abs(pair.First.G-pair.Second.G)+Math.Abs(pair.First.B-pair.Second.B)>75);
        try
        {
            string[] args=setup.Arguments.Where(a=>a!="/SafeMode").ToArray();int log=Array.IndexOf(args,"/Log");args[log+1]=Path.Combine(output,"ActivityLog.xml");
            layer.LaunchApplication(setup.Executable,[..args,setup.First],output);
            Wait(()=>{root=layer.GetPresentationWindows().FirstOrDefault(w=>w.Parent==0&&w.Visible&&w.Text.Contains("Greeting.cs",StringComparison.Ordinal))?.Handle??0;return root!=0;},"Installed IDE did not open its document",90000);
            layer.EnablePrivateInputSession(lowLatency:true);TextIs(File.ReadAllText(setup.First));
            var editor=Editors().Where(n=>!n.GetProperty("offscreen").GetBoolean()).OrderByDescending(n=>n.GetProperty("width").GetDouble()*n.GetProperty("height").GetDouble()).First();
            var origin=layer.ClientOrigin(root);int x=(int)editor.GetProperty("x").GetDouble()-origin.X+200,y=(int)editor.GetProperty("y").GetDouble()-origin.Y+8;
            var hit=layer.HitTestPointer(root,x,y);layer.SendQueueMouseSequence(hit.Window,[new(MouseMessage.Move,hit.X,hit.Y),new(MouseMessage.LeftDown,hit.X,hit.Y,1),new(MouseMessage.LeftUp,hit.X,hit.Y)],primeCapture:true);
            layer.SendQueueStateChord(root,[0x11,0x41],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);layer.PostKeyboardInput(root,"//",followPrivateFocus:true);TextIs("//");Thread.Sleep(250);
            editor=Editors().First(n=>Normalize(n.GetProperty("text").GetString()??"")=="//");origin=layer.ClientOrigin(root);regionX=(int)editor.GetProperty("x").GetDouble()-origin.X+80;regionY=(int)editor.GetProperty("y").GetDouble()-origin.Y+3;
            var initial=layer.CaptureResearch(root,"window-dc");File.WriteAllBytes(Path.Combine(output,"00-dc-before-any-print.png"),initial.Png);using var baselineImage=Decode(initial);var baseline=Region(baselineImage);
            const string marker="WMWMWMWMWMWMWMWMWMWM";var clock=Stopwatch.StartNew();layer.PostKeyboardInput(root,marker,followPrivateFocus:true);
            bool fresh=false;int changed=0,attempts=0;double? freshMilliseconds=null;CaptureFrame? last=null;
            while(clock.ElapsedMilliseconds<1500)
            {
                last=layer.CaptureResearch(root,"window-dc");using var image=Decode(last);changed=Changed(baseline,image);attempts++;
                if(changed>=30){fresh=true;freshMilliseconds=clock.Elapsed.TotalMilliseconds;break;}Thread.Sleep(8);
            }
            if(last!=null)File.WriteAllBytes(Path.Combine(output,"01-dc-after-real-typing-before-print.png"),last.Png);
            TextIs("//"+marker);
            var printed=layer.CaptureResearch(root,"pw1");File.WriteAllBytes(Path.Combine(output,"02-first-client-print.png"),printed.Png);using var printedImage=Decode(printed);int printChanged=Changed(baseline,printedImage);
            var afterPrint=layer.CaptureResearch(root,"window-dc");File.WriteAllBytes(Path.Combine(output,"03-dc-after-first-print.png"),afterPrint.Png);using var afterImage=Decode(afterPrint);int dcAfterPrintChanged=Changed(baseline,afterImage);
            var expected=Region(printedImage);int? beforePrintMismatch=null;
            if(last!=null){using var beforeImage=Decode(last);beforePrintMismatch=Changed(expected,beforeImage);}
            int afterPrintMismatch=Changed(expected,afterImage);
            findings=new{freshBeforeAnyPrint=fresh,verifiedIndependentDcGlyphs=fresh&&beforePrintMismatch<=3,freshMilliseconds,changedPixelsBeforePrint=changed,attempts,printChangedPixels=printChanged,dcAfterPrintChangedPixels=dcAfterPrintChanged,beforePrintMismatchPixels=beforePrintMismatch,afterPrintMismatchPixels=afterPrintMismatch,firstDcMilliseconds=initial.CaptureMilliseconds,lastDcMilliseconds=last?.CaptureMilliseconds,firstPrintNativeMilliseconds=printed.NativeRenderingMilliseconds,regionX,regionY,note="PNG transport and decoding are included in freshness time. A changed region alone is insufficient: the DC pixels are also compared with the actual subsequent printed source glyphs, excluding the final caret. Actual IDE text is checked separately. No PrintWindow call precedes the first DC trial."};
            // Save through the ordinary application after capture research.
            layer.SendQueueStateChord(root,[0x11,0x53],posted:true,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:true,followPrivateFocus:true);Wait(()=>File.ReadAllText(setup.First)=="//"+marker,"Research text did not save exactly");
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(failure);}
        finally{if(!layer.Closed){owned=layer.GetProcessIds();try{layer.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}layer.Dispose();}
        var physical=monitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool completed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"dc-capture.json"),JsonSerializer.Serialize(new{completed,failure,findings,cleanup,owned,physical,note="A successful experiment run can establish a negative compatibility result. This is not a released capture backend."},new JsonSerializerOptions{WriteIndented=true}));return completed?0:1;
    }
}

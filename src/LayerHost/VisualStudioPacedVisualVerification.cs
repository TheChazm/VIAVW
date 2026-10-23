using System.Diagnostics;
using System.Text.Json;
namespace LayerHost;

// Ordinary editor glyphs act as observable content. No application adapter,
// source-process instrumentation or physical keyboard/pointer is used.
internal static class VisualStudioPacedVisualVerification
{
    sealed record Glyph(int[] Mask,byte[] Before,int Required);
    sealed record Submission(int Character,long StartQpc,long BeforeInputSequence);
    internal static int Run(string output,string setupFile,string viewerExecutable,bool fast)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        string binRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../")))+Path.DirectorySeparatorChar;
        viewerExecutable=Path.GetFullPath(viewerExecutable);
        if(!viewerExecutable.StartsWith(binRoot,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(viewerExecutable)!="LayerHost.exe"||!File.Exists(viewerExecutable))throw new ArgumentException("Use a preserved VIAVW viewer");
        using var manager=new LayerManager();using var qa=manager.CreateLayer();using var physicalMonitor=new ResearchDesktopMonitor();using var pacing=new ResearchWaitableTimer();
        long viewer=0,canvas=0;int[] owned=[];string? failure=null;var masks=new List<Glyph>();var submitted=new List<Submission>();var observations=new List<object>();
        var fresh=new Dictionary<int,(long Frame,long ObservedQpc)>();const int count=24,width=288,height=14;
        JsonElement Preview(){using var doc=JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-preview.json")));return doc.RootElement.Clone();}
        void Wait(Func<bool> test,string reason,int timeout=30000){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<timeout){if(test())return;pacing.Wait(2);}throw new TimeoutException(reason);}
        void Button(string name)=>qa.SendMouseInput(qa.GetWindows().Single(w=>w.Text==name).Handle,10,10);
        JsonElement[] Nodes(){using var doc=JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json")));return doc.RootElement.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).ToArray();}
        string Normalize(string s)=>System.Text.RegularExpressions.Regex.Replace(s,@"(?m)^\d+ ","");
        void Inspect(string expected)
        {
            long old=Preview().GetProperty("observationVersion").GetInt64();Button("Inspect details");
            Wait(()=>{long current=Preview().GetProperty("observationVersion").GetInt64();if(current<=old)return false;if(Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")==expected))return true;old=current;Button("Inspect details");return false;},"Actual editor text not observed",45000);
        }
        void Chord(params int[] keys){long before=Preview().GetProperty("inputSequence").GetInt64();qa.SendQueueStateChord(canvas,keys);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>before,"Chord not forwarded");}
        void Text(string text){long before=Preview().GetProperty("inputSequence").GetInt64();qa.PostKeyboardInput(canvas,text);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>=before+text.Length,"Text not forwarded");}
        byte[] Pixels(JsonElement p)=>Convert.FromBase64String(p.GetProperty("pixelProbe").GetProperty("pixels").GetString()!);
        int[] Calibrate(byte[] before,byte[] after)
        {
            // The single-character helper intentionally limits width to 128.
            // This bounded 4032-pixel strip covers all 24 successive positions.
            var changed=new bool[width*height];var columns=new int[width];
            for(int i=0;i<changed.Length;i++)if(Math.Abs(before[i*4]-after[i*4])+Math.Abs(before[i*4+1]-after[i*4+1])+Math.Abs(before[i*4+2]-after[i*4+2])>75){changed[i]=true;columns[i%width]++;}
            int[] mask=Enumerable.Range(0,changed.Length).Where(i=>changed[i]&&columns[i%width]<height-1).ToArray();
            if(mask.Length<10)throw new InvalidOperationException("Insufficient paced glyph pixels after excluding caret columns");return mask;
        }
        int Changed(Glyph glyph,byte[] pixels){int changed=0;foreach(int index in glyph.Mask){int i=index*4;if(Math.Abs(glyph.Before[i]-pixels[i])+Math.Abs(glyph.Before[i+1]-pixels[i+1])+Math.Abs(glyph.Before[i+2]-pixels[i+2])>75)changed++;}return changed;}
        void FreshCalibrationFrame()
        {
            Thread.Sleep(100);long before=Preview().GetProperty("previewVersion").GetInt64();Button("Refresh");Wait(()=>Preview().GetProperty("previewVersion").GetInt64()>before,"Calibration refresh missing");
        }
        try
        {
            qa.LaunchApplication(viewerExecutable,[fast?"--run-vs-fast-timing-viewer":"--run-vs-damage-timing-viewer",setupFile,output],output);
            Wait(()=>File.Exists(Path.Combine(output,"viewer-preview.json")),"Viewer startup",90000);
            var windows=qa.GetWindows();viewer=windows.Single(w=>w.Text=="VIAVW — private application layer").Handle;canvas=windows.Single(w=>w.Parent==viewer&&w.Width>900&&w.Height>200).Handle;
            qa.EnablePrivateInputSession();Button("Human control");Wait(()=>Preview().GetProperty("owner").GetString()=="Human","Human control");
            Wait(()=>Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs",StringComparison.Ordinal),"Document startup",60000);Inspect(File.ReadAllText(setup.First));
            var editor=Nodes().First(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")==File.ReadAllText(setup.First));
            var p=Preview();var origin=p.GetProperty("sourceClientOrigin");var presentation=p.GetProperty("presentation");
            qa.SendMouseInput(canvas,(int)Math.Ceiling((editor.GetProperty("x").GetDouble()+80-origin.GetProperty("X").GetInt32())*presentation.GetProperty("DrawWidth").GetInt32()/presentation.GetProperty("SourceWidth").GetInt32()),(int)Math.Ceiling((editor.GetProperty("y").GetDouble()+8-origin.GetProperty("Y").GetInt32())*presentation.GetProperty("DrawHeight").GetInt32()/presentation.GetProperty("SourceHeight").GetInt32()));
            Chord(0x11,0x41);Text("//");Inspect("//");
            editor=Nodes().First(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")=="//");p=Preview();origin=p.GetProperty("sourceClientOrigin");
            int x=(int)editor.GetProperty("x").GetDouble()-origin.GetProperty("X").GetInt32()+2,y=(int)editor.GetProperty("y").GetDouble()-origin.GetProperty("Y").GetInt32()+1;
            File.WriteAllText(Path.Combine(output,"pixel-probe.json"),JsonSerializer.Serialize(new{Root=p.GetProperty("root").GetInt64(),X=x,Y=y,Width=width,Height=height,ExportPixels=true}));
            File.WriteAllText(Path.Combine(output,"pixel-probe-reset.request"),"Paced glyph calibration");
            Wait(()=>{var v=Preview();return v.TryGetProperty("pixelProbe",out var probe)&&probe.ValueKind==JsonValueKind.Object&&probe.GetProperty("ready").GetBoolean()&&probe.GetProperty("pixels").ValueKind==JsonValueKind.String;},"Exported pixel baseline");
            FreshCalibrationFrame();byte[] previous=Pixels(Preview());
            for(int i=0;i<count;i++)
            {
                Text("W");Inspect("//"+new string('W',i+1));FreshCalibrationFrame();byte[] after=Pixels(Preview());
                int[] mask=Calibrate(previous,after);masks.Add(new(mask,previous,(int)Math.Ceiling(mask.Length*.75)));previous=after;
            }
            File.WriteAllText(Path.Combine(output,"glyph-calibration.json"),JsonSerializer.Serialize(new{x,y,width,height,masks,note="Per-position W masks exclude caret columns. All 24 prefix texts were checked outside timed submission."}));
            Chord(0x11,0x41);Text("//");Inspect("//");FreshCalibrationFrame();
            Wait(()=>{byte[] pixels=Pixels(Preview());return masks.All(g=>Changed(g,pixels)<=3);},"Every calibrated glyph must disappear before timing");
            long initialSequence=Preview().GetProperty("inputSequence").GetInt64(),lastVersion=-1;
            void Sample()
            {
                var current=Preview();long version=current.GetProperty("previewVersion").GetInt64();if(version==lastVersion)return;lastVersion=version;
                byte[] pixels=Pixels(current);long sequence=current.GetProperty("inputSequence").GetInt64(),observed=Stopwatch.GetTimestamp();var newGlyphs=new List<int>();
                foreach(var item in submitted)
                    if(!fresh.ContainsKey(item.Character)&&sequence>=initialSequence+item.Character+1&&Changed(masks[item.Character],pixels)>=masks[item.Character].Required){fresh[item.Character]=(version,observed);newGlyphs.Add(item.Character);}
                observations.Add(new{version,sequence,observedQpc=observed,newGlyphs});
            }
            var cadence=Stopwatch.StartNew();
            for(int i=0;i<count;i++)
            {
                while(cadence.Elapsed.TotalMilliseconds<i*50){Sample();pacing.Wait(1);}
                long started=Stopwatch.GetTimestamp();submitted.Add(new(i,started,initialSequence+i));qa.PostKeyboardInput(canvas,"W");Sample();
            }
            Wait(()=>{Sample();return fresh.Count==count;},"Every submitted character must appear in actual captured source pixels",3000);
            Inspect("//"+new string('W',count));Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)=="//"+new string('W',count),"Paced visual test exact save");
            File.WriteAllBytes(Path.Combine(output,"paced-final.png"),qa.CaptureVisualOutput(viewer).Png);
            File.WriteAllText(Path.Combine(output,"viewer-timing-flush.request"),"End paced character proof");Wait(()=>File.Exists(Path.Combine(output,"viewer-timing.json")),"Paced trace flush");
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);}
        finally{if(!qa.Closed){owned=qa.GetProcessIds();try{qa.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}qa.Dispose();}
        var rows=new List<object>();
        if(failure==null)
        {
            using var trace=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"viewer-timing.json")));var entries=trace.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            double Ms(long a,long b)=>(a-b)*1000.0/Stopwatch.Frequency;
            foreach(var item in submitted)
            {
                var seen=fresh[item.Character];var bitmap=entries.FirstOrDefault(e=>e.GetProperty("Stage").GetString()=="bitmap-ready"&&e.GetProperty("Frame").GetInt64()==seen.Frame);
                var paint=entries.Where(e=>e.GetProperty("Stage").GetString()=="paint-end"&&e.GetProperty("Frame").GetInt64()>=seen.Frame&&e.GetProperty("Qpc").GetInt64()>=item.StartQpc).OrderBy(e=>e.GetProperty("Qpc").GetInt64()).FirstOrDefault();
                if(bitmap.ValueKind!=JsonValueKind.Object||paint.ValueKind!=JsonValueKind.Object){failure="A timed observed glyph lacks its bitmap or normal paint trace";break;}
                rows.Add(new{item.Character,item.StartQpc,observedFrame=seen.Frame,bitmapMilliseconds=Ms(bitmap.GetProperty("Qpc").GetInt64(),item.StartQpc),paintMilliseconds=Ms(paint.GetProperty("Qpc").GetInt64(),item.StartQpc),reportObservedMilliseconds=Ms(seen.ObservedQpc,item.StartQpc)});
            }
        }
        var physical=physicalMonitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool passed=failure==null&&rows.Count==count&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"paced-visual.json"),JsonSerializer.Serialize(new{passed,failure,fast,viewerExecutable,viewerAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(viewerExecutable,".dll")))),rows,observations,cleanup,owned,physical,note="24 characters submitted at 50 ms cadence without per-character acknowledgement waits. Each new glyph is proved against a calibrated position mask. Trace times use the first report-observed fresh frame, an upper bound that may miss earlier coalesced frames. Normal paint-handler return is not physical scanout. Pixel export/trace add diagnostic overhead to both settings."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

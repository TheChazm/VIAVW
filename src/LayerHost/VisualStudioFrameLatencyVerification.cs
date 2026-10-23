using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class VisualStudioFrameLatencyVerification
{
    internal static int Run(string output,string setupFile,bool copied,bool repaint=false,bool activatePrivate=false,bool windowDc=false,string? viewerExecutable=null,bool pacedTyping=false,bool traceStages=false,bool captureOriginReport=false,bool asyncStatusReports=false,bool preciseMouseWaits=false,bool singleCharacter=false,bool incrementalPaint=false,bool deadlinePreview=false,int deadlineFirstMilliseconds=16,int deadlineVariant=0,bool fastPreview=false,int followupMilliseconds=16,bool immediatePaint=false,bool preparePointer=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        viewerExecutable=Path.GetFullPath(viewerExecutable??Path.Combine(AppContext.BaseDirectory,"LayerHost.exe"));
        string binaryRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../")))+Path.DirectorySeparatorChar;
        if(!viewerExecutable.StartsWith(binaryRoot,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(viewerExecutable)!="LayerHost.exe"||!File.Exists(viewerExecutable))throw new ArgumentException("Use a preserved VIAVW viewer in this project's bin directory.");
        using var manager=new LayerManager();using var qa=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();
        string? failure=null;int[] owned=[];long viewer=0,canvas=0;var measurements=new List<object>();var shortcutMeasurements=new List<object>();
        JsonElement Preview()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-preview.json"))).RootElement.Clone();
        void Wait(Func<bool> test,string message,int timeout=20000){var t=Stopwatch.StartNew();do{if(test())return;Thread.Sleep(2);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException(message);}
        void Button(string name)=>qa.SendMouseInput(qa.GetWindows().Single(w=>w.Text==name).Handle,10,10);
        void Chord(params int[] keys){long n=Preview().GetProperty("inputSequence").GetInt64();var clock=Stopwatch.StartNew();qa.SendQueueStateChord(canvas,keys);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>n,"Shortcut not forwarded");shortcutMeasurements.Add(new{keys,reportMilliseconds=clock.Elapsed.TotalMilliseconds,forwarding=Preview().GetProperty("lastForwardedChord")});}
        void Text(string text){long n=Preview().GetProperty("inputSequence").GetInt64();qa.PostKeyboardInput(canvas,text);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>=n+text.Length,"Text not forwarded");}
        void Key(int key){long n=Preview().GetProperty("inputSequence").GetInt64();qa.PostKey(canvas,key);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>n,"Key not forwarded");}
        JsonElement[] Nodes()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json"))).RootElement.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).ToArray();
        string Normalize(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ","");
        void Inspect(string name,string expected)
        {
            long observation=Preview().GetProperty("observationVersion").GetInt64();Button("Inspect details");
            Wait(()=>{long now=Preview().GetProperty("observationVersion").GetInt64();if(now<=observation)return false;
                if(Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")==expected))return true;
                // Request another observation only after the previous explicit
                // inspection completed, rather than on each automatic frame.
                observation=now;Button("Inspect details");return false;},"Actual editor text was not observed: "+name,30000);
            File.WriteAllText(Path.Combine(output,name+"-accessible.json"),SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json")));
            byte[] screenshot=qa.CaptureVisualOutput(viewer).Png;
            File.WriteAllBytes(Path.Combine(output,name+".png"),screenshot);
            if(windowDc)VisualStudioFrameChecks.RequireNoSentinel(screenshot);
        }
        object? busy=null,pacedActivity=null;
        try
        {
            string[] viewerArguments=[incrementalPaint?"--run-vs-damage-timing-viewer":preciseMouseWaits?"--run-vs-precise-timing-viewer":asyncStatusReports?(traceStages?"--run-vs-async-report-timing-viewer":"--run-vs-async-report-pixel-viewer"):captureOriginReport?"--run-vs-origin-timing-viewer":traceStages?"--run-vs-timing-viewer":windowDc?"--run-vs-dc-pixel-viewer":activatePrivate?"--run-vs-active-pixel-viewer":repaint?"--run-vs-repaint-pixel-viewer":copied?"--run-vs-copy-pixel-viewer":"--run-vs-pixel-viewer",setupFile,output];
            if(deadlinePreview)viewerArguments=["--run-vs-deadline-timing-viewer",setupFile,output,deadlineFirstMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            if(deadlinePreview&&deadlineVariant!=0)viewerArguments=["--run-vs-deadline-variant-viewer",setupFile,output,deadlineFirstMilliseconds.ToString(),deadlineVariant.ToString()];
            if(fastPreview)viewerArguments=followupMilliseconds==16?["--run-vs-fast-timing-viewer",setupFile,output]:["--run-vs-cadence-timing-viewer",setupFile,output,followupMilliseconds.ToString()];
            if(preparePointer)viewerArguments=["--run-vs-prepared-timing-viewer",setupFile,output];
            if(immediatePaint)viewerArguments=["--run-vs-paint-timing-viewer",setupFile,output,followupMilliseconds.ToString()];
            qa.LaunchApplication(viewerExecutable,viewerArguments,output);
            Wait(()=>File.Exists(Path.Combine(output,"viewer-preview.json")),"Viewer did not start",90000);
            var windows=qa.GetWindows();viewer=windows.Single(w=>w.Text=="VIAVW — private application layer").Handle;canvas=windows.Single(w=>w.Parent==viewer&&w.Width>900&&w.Height>200).Handle;
            qa.EnablePrivateInputSession();Button("Human control");Wait(()=>Preview().GetProperty("owner").GetString()=="Human","Human control missing");
            Wait(()=>Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs",StringComparison.Ordinal),"Initial document missing",60000);
            Inspect("00-loaded-document",File.ReadAllText(setup.First));
            var code=Nodes().First(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")==File.ReadAllText(setup.First));
            var placement=Preview();var pointOrigin=placement.GetProperty("sourceClientOrigin");var scale=placement.GetProperty("presentation");
            qa.SendMouseInput(canvas,(int)Math.Ceiling((code.GetProperty("x").GetDouble()+80-pointOrigin.GetProperty("X").GetInt32())*scale.GetProperty("DrawWidth").GetInt32()/scale.GetProperty("SourceWidth").GetInt32()),(int)Math.Ceiling((code.GetProperty("y").GetDouble()+8-pointOrigin.GetProperty("Y").GetInt32())*scale.GetProperty("DrawHeight").GetInt32()/scale.GetProperty("SourceHeight").GetInt32()));
            Inspect("00-clicked-document",File.ReadAllText(setup.First));
            qa.ResearchSeedPrivateModifiers(canvas,[0x10,0xA0]);
            Chord(0x11,0x41);Text("//");Key(0x23);Inspect("01-two-character-comment","//");Thread.Sleep(1000);
            var editor=Nodes().First(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")=="//");var initial=Preview();var origin=initial.GetProperty("sourceClientOrigin");
            int probeX=(int)editor.GetProperty("x").GetDouble()-origin.GetProperty("X").GetInt32()+(singleCharacter?2:80),probeY=(int)editor.GetProperty("y").GetDouble()-origin.GetProperty("Y").GetInt32()+(singleCharacter?1:3),probeWidth=singleCharacter?64:20;
            long probeRoot=initial.GetProperty("root").GetInt64();int[]? glyphMask=null;int requiredChangedPixels=30;
            void ConfigureProbe(bool export,int[]? mask)=>File.WriteAllText(Path.Combine(output,"pixel-probe.json"),JsonSerializer.Serialize(new{Root=probeRoot,X=probeX,Y=probeY,Width=probeWidth,Height=14,ExportPixels=export,Mask=mask}));
            ConfigureProbe(singleCharacter,null);
            Wait(()=>Preview().GetProperty("pixelProbe").GetProperty("ready").GetBoolean(),"Pixel comparison baseline was not captured");
            if(singleCharacter)
            {
                byte[] beforePixels=Convert.FromBase64String(Preview().GetProperty("pixelProbe").GetProperty("pixels").GetString()!);
                Text("W");Wait(()=>Preview().GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32()>=30,"Calibration glyph did not appear");Inspect("calibration-W","//W");
                byte[] afterPixels=Convert.FromBase64String(Preview().GetProperty("pixelProbe").GetProperty("pixels").GetString()!);
                glyphMask=SingleGlyphMask.Build(beforePixels,afterPixels,probeWidth,14);
                Chord(0x11,0x41);Text("//");Key(0x23);Wait(()=>Preview().GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32()<=3,"Calibration pixels did not reset");
                ConfigureProbe(false,glyphMask);File.WriteAllText(Path.Combine(output,"pixel-probe-reset.request"),"New calibrated mask");
                Wait(()=>{var probe=Preview().GetProperty("pixelProbe");return probe.GetProperty("ready").GetBoolean()&&probe.GetProperty("configuration").GetProperty("Mask").ValueKind==JsonValueKind.Array;},"Calibrated mask was not loaded");
                requiredChangedPixels=Math.Max(10,(glyphMask.Length*3+3)/4);
                File.WriteAllText(Path.Combine(output,"single-character-calibration.json"),JsonSerializer.Serialize(new{probeX,probeY,probeWidth,height=14,mask=glyphMask,requiredChangedPixels,note="Changed glyph pixels excluding columns with near-full-height caret changes. Baseline and W verified in actual IDE before timed trials."}));
            }
            double Cpu(){double value=0;foreach(int id in qa.GetProcessIds())try{using var p=Process.GetProcessById(id);value+=p.TotalProcessorTime.TotalMilliseconds;}catch(ArgumentException){}catch(InvalidOperationException){}return value;}
            double beforeCpu=Cpu();var activity=Stopwatch.StartNew();
            string marker=singleCharacter?"W":pacedTyping?string.Concat(Enumerable.Repeat("WM19_",40)):"WMWMWMWMWMWMWMWMWMWM";
            if(pacedTyping)
            {
                long before=Preview().GetProperty("inputSequence").GetInt64(),version=Preview().GetProperty("previewVersion").GetInt64();
                var frames=new List<object>();var cadence=Stopwatch.StartNew();
                void Sample()
                {
                    var p=Preview();long current=p.GetProperty("previewVersion").GetInt64();if(current==version)return;version=current;
                    frames.Add(new{elapsedMilliseconds=cadence.Elapsed.TotalMilliseconds,version,captureMilliseconds=p.GetProperty("CaptureMilliseconds").GetDouble(),refreshMilliseconds=p.GetProperty("refreshMilliseconds").GetDouble(),changedPixels=p.GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32()});
                }
                for(int i=0;i<marker.Length;i++)
                {
                    double due=i*50.0;
                    while(cadence.Elapsed.TotalMilliseconds<due){Sample();Thread.Sleep(Math.Clamp((int)(due-cadence.Elapsed.TotalMilliseconds),1,5));}
                    qa.PostKeyboardInput(canvas,marker[i].ToString());Sample();
                }
                double submitted=cadence.Elapsed.TotalMilliseconds;
                Wait(()=>{Sample();return Preview().GetProperty("inputSequence").GetInt64()>=before+marker.Length;},"Paced text did not finish forwarding");
                double wall=cadence.Elapsed.TotalMilliseconds,cpu=Cpu()-beforeCpu;
                pacedActivity=new{characters=marker.Length,requestedIntervalMilliseconds=50,submissionMilliseconds=submitted,forwardedMilliseconds=wall,sampledTreeCpuMilliseconds=cpu,oneCorePercent=cpu/wall*100,frames,note="200 separate character submissions over about ten seconds. CPU covers the owned process tree during typing, excludes the later explicit UIA/PNG checks. Frame count is sampled publications, not per-character visual latency."};
                Inspect("paced-final","//"+marker);
            }
            else
            for(int trial=0;trial<7;trial++)
            {
                if(trial!=0){Chord(0x11,0x41);Text("//");Key(0x23);Wait(()=>Preview().GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32()<=3,"Baseline comment pixels did not return");}
                long before=Preview().GetProperty("inputSequence").GetInt64(),startQpc=Stopwatch.GetTimestamp();var latency=Stopwatch.StartNew();qa.PostKeyboardInput(canvas,marker);
                Wait(()=>{var p=Preview();return p.GetProperty("inputSequence").GetInt64()>=before+marker.Length&&p.GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32()>=requiredChangedPixels;},"Typed glyphs were not visible in the captured pixel region");
                var p=Preview();measurements.Add(new{trial,startQpc,beforeInputSequence=before,endInputSequence=before+marker.Length,requiredChangedPixels,milliseconds=latency.Elapsed.TotalMilliseconds,sourceForwardedMilliseconds=(p.GetProperty("pixelProbe").GetProperty("lastInputQpc").GetInt64()-startQpc)*1000.0/Stopwatch.Frequency,changedPixels=p.GetProperty("pixelProbe").GetProperty("changedPixels").GetInt32(),p});
                Inspect("typed-"+trial,"//"+marker);
            }
            busy=new{wallMilliseconds=activity.Elapsed.TotalMilliseconds,sampledTreeCpuMilliseconds=Cpu()-beforeCpu,note="Includes explicit state verification and PNG evidence between trials; not a sustained typing CPU benchmark."};
            Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)=="//"+marker,"Final actual file did not match the observed typed text");
            if(traceStages){File.WriteAllText(Path.Combine(output,"viewer-timing-flush.request"),"Bounded trace requested after measurement");Wait(()=>File.Exists(Path.Combine(output,"viewer-timing.json")),"Viewer timing trace did not flush");}
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);}
        finally{if(!qa.Closed){owned=qa.GetProcessIds();try{qa.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}qa.Dispose();}
        var physical=monitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"frame-latency.json"),JsonSerializer.Serialize(new{passed,failure,followupMilliseconds,immediatePaint,copied,repaint,activatePrivate,windowDc,pacedTyping,traceStages,captureOriginReport,asyncStatusReports,preciseMouseWaits,singleCharacter,measurements,shortcutMeasurements,busy,pacedActivity,cleanup,owned,physical,viewerExecutable,viewerAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(viewerExecutable,".dll")))),assemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"LayerHost.dll")))),note=singleCharacter?"Seven individual W submissions. Calibrated glyph mask excludes caret columns; exact actual text and final saved file verified. Stage trace distinguishes bitmap, normal paint-handler completion and reports; not physical scanout.":pacedTyping?"Paced 200-character typing workload. Exact actual IDE text and saved file checked after measurement; sampled frames do not establish per-character visual latency.":"Stops when the captured source bitmap contains new glyph pixels in a region beyond the initial comment and before the final caret. Exact actual IDE text and final file are checked separately. This measures captured visual content, not physical scanout. Seven small bursts, not a universal latency guarantee."},new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
}

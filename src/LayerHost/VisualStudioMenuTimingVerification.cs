using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Focused installed-IDE workflow: repeat the same real popup command rather than
// infer menu latency from one sample or from report acknowledgements alone.
internal static class VisualStudioMenuTimingVerification
{
    internal static int Run(string output,string setupFile,string viewerExecutable,bool optimized)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;VisualStudioResearch.ValidateSetup(setup,setupFile);
        viewerExecutable=Path.GetFullPath(viewerExecutable);string binaries=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../")));
        if(!viewerExecutable.StartsWith(binaries+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(viewerExecutable)!="LayerHost.exe")throw new ArgumentException("Use a preserved project viewer");
        using var manager=new LayerManager();using var qa=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();int[] owned=[];long viewer=0,canvas=0;string? failure=null;var measurements=new List<object>();
        JsonElement Preview()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-preview.json"))).RootElement.Clone();
        JsonElement[] Nodes()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json"))).RootElement.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).ToArray();
        void Wait(Func<bool> test,string message,int timeout=30000){var t=Stopwatch.StartNew();do{if(test())return;Thread.Sleep(10);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException(message);}
        void Button(string name)=>qa.SendMouseInput(qa.GetWindows().Single(w=>w.Text==name).Handle,10,10);
        void Inspect(){long v=Preview().GetProperty("observationVersion").GetInt64();Button("Inspect details");Wait(()=>Preview().GetProperty("observationVersion").GetInt64()>v,"Explicit menu inspection did not finish");}
        void Click(JsonElement node)
        {
            var p=Preview();var origin=p.GetProperty("sourceClientOrigin");var scale=p.GetProperty("presentation");
            int x=(int)Math.Ceiling((node.GetProperty("x").GetDouble()+node.GetProperty("width").GetDouble()/2-origin.GetProperty("X").GetInt32())*scale.GetProperty("DrawWidth").GetInt32()/scale.GetProperty("SourceWidth").GetInt32());
            int y=(int)Math.Ceiling((node.GetProperty("y").GetDouble()+node.GetProperty("height").GetDouble()/2-origin.GetProperty("Y").GetInt32())*scale.GetProperty("DrawHeight").GetInt32()/scale.GetProperty("SourceHeight").GetInt32());
            long before=p.GetProperty("inputSequence").GetInt64();qa.SendMouseInput(canvas,x,y);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>before,"Mouse command not forwarded");
        }
        JsonElement Menu(string name)=>Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&(n.GetProperty("name").GetString()??"").StartsWith(name,StringComparison.Ordinal)&&!n.GetProperty("offscreen").GetBoolean());
        try
        {
            qa.LaunchApplication(viewerExecutable,[optimized?"--run-vs-precise-viewer":"--run-vs-dc-viewer",setupFile,output],output);
            Wait(()=>File.Exists(Path.Combine(output,"viewer-preview.json")),"Viewer did not start",90000);
            viewer=qa.GetWindows().Single(w=>w.Text=="VIAVW — private application layer").Handle;canvas=qa.GetWindows().Single(w=>w.Parent==viewer&&w.Width>900&&w.Height>200).Handle;
            qa.EnablePrivateInputSession();Button("Human control");Wait(()=>Preview().GetProperty("owner").GetString()=="Human","Human ownership not applied");
            Wait(()=>Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs",StringComparison.Ordinal),"Initial actual document did not open",60000);Inspect();
            for(int trial=0;trial<5;trial++)
            {
                Inspect();Click(Menu("Tools"));Wait(()=>Preview().GetProperty("popupSurfaces").GetArrayLength()>0,"Tools popup did not open");var tools=Preview().GetProperty("lastPointerMetrics");Inspect();
                var clock=Stopwatch.StartNew();Click(Menu("Options"));Wait(()=>Preview().GetProperty("activeText").GetString()=="Options","Actual Options dialog did not open");double observed=clock.Elapsed.TotalMilliseconds;var metrics=Preview().GetProperty("lastPointerMetrics");
                if(!metrics.GetProperty("overPopup").GetBoolean())throw new InvalidOperationException("Measured command was not a popup input path");
                Inspect();var cancel=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.Button"&&n.GetProperty("name").GetString()=="Cancel");
                if(trial==0)File.WriteAllBytes(Path.Combine(output,"options-dialog.png"),qa.CaptureVisualOutput(viewer).Png);
                Click(cancel);Wait(()=>Preview().GetProperty("root").GetInt64()==Preview().GetProperty("mainRoot").GetInt64(),"Cancel did not restore the same IDE");
                measurements.Add(new{trial,tools,options=metrics,optionsDialogObservedMilliseconds=observed});Console.WriteLine($"PASS real Options popup/cancel trial {trial}");
            }
            Inspect();string Normalize(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ","");
            if(!Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&Normalize(n.GetProperty("text").GetString()??"")==File.ReadAllText(setup.First)))throw new InvalidOperationException("Actual source document changed or disappeared");
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);}
        finally{if(!qa.Closed){owned=qa.GetProcessIds();try{qa.CloseGracefully(TimeSpan.FromSeconds(6));}catch{}}qa.Dispose();}
        var physical=monitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"menu-timing.json"),JsonSerializer.Serialize(new{passed,failure,optimized,measurements,cleanup,owned,physical,viewerExecutable,viewerAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(viewerExecutable,".dll")))),note="Five real Tools/Options/Cancel workflows in one IDE session. Pointer metrics cover wrapper mouse-command handling, not physical scanout. Dialog-observation time includes report/capture delay. Explicit inspections occur outside measured popup selection."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Test input enters the real viewer canvas. Observations are passive; the
// viewer records standard accessibility assistance for eligible modal controls.
// Cases record actual state/files/processes and can fail independently.
internal static class VisualStudioEndToEndVerification
{
    internal static int Run(string output, string setupFile, bool extended = false, bool debuggerOnly = false, bool projectOnly = false,bool menusOnly=false,string? viewerExecutable=null,bool designerOnly=false,bool windowDcPreview=false,bool asyncStatusReports=false,bool preciseMouseWaits=false,bool incrementalPaint=false,bool postedFunctionKeys=false,bool fastPreview=false,bool preparePointer=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        bool focused=projectOnly||menusOnly||designerOnly;
        viewerExecutable=Path.GetFullPath(viewerExecutable??Path.Combine(AppContext.BaseDirectory,"LayerHost.exe"));
        string projectRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        if(!viewerExecutable.StartsWith(projectRoot,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(viewerExecutable)!="LayerHost.exe"||!File.Exists(viewerExecutable))throw new ArgumentException("Use a preserved VIAVW viewer inside this project.");
        var setup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        VisualStudioResearch.ValidateSetup(setup,setupFile);
        if(!File.Exists(Path.Combine(Path.GetDirectoryName(setupFile)!,"software-profile-ready.json")))throw new ArgumentException("Configure a fresh runnable profile first.");
        using var manager=new LayerManager(); using var qa=manager.CreateLayer(); using var monitor=new ResearchDesktopMonitor();
        var personal=VisualStudioResearch.PersonalSettings(allowUnavailable:true); var results=new List<object>(); var menus=new List<object>(); var timings=new List<object>();
        var menuTree=new List<object>();
        var menuBarGeometry=new HashSet<(string?,double,double,double,double)>();
        (string?,double,double,double,double) MenuGeometry(JsonElement n)=>(n.GetProperty("name").GetString(),n.GetProperty("x").GetDouble(),n.GetProperty("y").GetDouble(),n.GetProperty("width").GetDouble(),n.GetProperty("height").GetDouble());
        int[] owned=[]; long viewer=0,canvas=0; int sourcePid=0; string? fatal=null; int failures=0; var clock=Stopwatch.StartNew();
        string directory=Path.GetDirectoryName(setup.First)!, program=Path.Combine(directory,"Program.cs"), exe=Path.Combine(directory,"bin/Debug/ViavwDisposable.exe");
        string originalFirst=File.ReadAllText(setup.First);
        JsonElement Preview()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-preview.json"))).RootElement.Clone();
        JsonElement[] Nodes()=>JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json"))).RootElement.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).ToArray();
        JsonElement[] PopupNodes()
        {
            var top=Preview().GetProperty("popupSurfaces")[0];
            double x=top.GetProperty("X").GetInt32(),y=top.GetProperty("Y").GetInt32(),right=x+top.GetProperty("Width").GetInt32(),bottom=y+top.GetProperty("Height").GetInt32();
            // Some popup HWND providers expose only their first few rows. The
            // owning IDE's logical tree contains the remaining peers. Scope the
            // merged observations to the actual topmost popup's screen bounds.
            return Nodes().Where(n=>n.GetProperty("x").ValueKind==JsonValueKind.Number&&n.GetProperty("width").ValueKind==JsonValueKind.Number&&n.GetProperty("y").ValueKind==JsonValueKind.Number&&n.GetProperty("height").ValueKind==JsonValueKind.Number)
                .Where(n=>!menuBarGeometry.Contains(MenuGeometry(n)))
                .Where(n=>{double cx=n.GetProperty("x").GetDouble()+n.GetProperty("width").GetDouble()/2,cy=n.GetProperty("y").GetDouble()+n.GetProperty("height").GetDouble()/2;return cx>=x&&cx<right&&cy>=y&&cy<bottom;}).ToArray();
        }
        void Wait(Func<bool> test,string reason,int timeout=20000){var t=Stopwatch.StartNew();do{if(test())return;Thread.Sleep(25);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException(reason);}
        void Require(bool test,string reason){if(!test)throw new InvalidOperationException(reason);}
        void Button(string name)=>qa.SendMouseInput(qa.GetWindows().Single(w=>w.Text==name).Handle,10,10);
        void Ack(long before,string name)=>Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>before,"Viewer did not forward "+name);
        void Key(int key){long n=Preview().GetProperty("inputSequence").GetInt64();qa.PostKey(canvas,key);Ack(n,"key");}
        void Chord(params int[] keys){long n=Preview().GetProperty("inputSequence").GetInt64();qa.SendQueueStateChord(canvas,keys);Ack(n,"shortcut");}
        void Type(string text){long n=Preview().GetProperty("inputSequence").GetInt64();qa.PostKeyboardInput(canvas,text);Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>=n+text.Length,"Typing did not finish forwarding");}
        void Inspect(string name)
        {
            long before=Preview().GetProperty("previewVersion").GetInt64();Button("Inspect details");
            Wait(()=>{var p=Preview();long v=p.GetProperty("previewVersion").GetInt64();if(v<=before)return false;if(p.GetProperty("observationVersion").GetInt64()==v)return true;before=v;Button("Inspect details");return false;},"Observation did not finish: "+name,30000);
            File.WriteAllText(Path.Combine(output,name+"-accessible.json"),SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json")));
            File.WriteAllText(Path.Combine(output,name+"-preview.json"),Preview().GetRawText());
            if(windowDcPreview)Require(Preview().GetProperty("Backend").GetString()!.StartsWith("Window DC",StringComparison.Ordinal),"DC workflow used a different root capture backend");
            byte[] screenshot=qa.CaptureVisualOutput(viewer).Png;
            File.WriteAllBytes(Path.Combine(output,name+".png"),screenshot);
            if(windowDcPreview)VisualStudioFrameChecks.RequireNoSentinel(screenshot);
        }
        void ObserveUntil(string name,Func<bool> condition,int timeout=20000)
        {var t=Stopwatch.StartNew();do{Inspect(name);if(condition())return;Thread.Sleep(100);}while(t.ElapsedMilliseconds<timeout);throw new TimeoutException("Application state not reached: "+name);}
        void Click(JsonElement node,int offsetX=-1,int offsetY=-1)
        {
            if(offsetX<0&&node.GetProperty("type").GetString() is "ControlType.CheckBox" or "ControlType.RadioButton")offsetX=8;
            var p=Preview();var o=p.GetProperty("sourceClientOrigin");var s=p.GetProperty("presentation");
            double x=node.GetProperty("x").GetDouble()+(offsetX<0?node.GetProperty("width").GetDouble()/2:offsetX)-o.GetProperty("X").GetInt32();
            double y=node.GetProperty("y").GetDouble()+(offsetY<0?node.GetProperty("height").GetDouble()/2:offsetY)-o.GetProperty("Y").GetInt32();
            long n=p.GetProperty("inputSequence").GetInt64();qa.SendMouseInput(canvas,(int)Math.Ceiling(x*s.GetProperty("DrawWidth").GetInt32()/s.GetProperty("SourceWidth").GetInt32()),(int)Math.Ceiling(y*s.GetProperty("DrawHeight").GetInt32()/s.GetProperty("SourceHeight").GetInt32()));Ack(n,"pointer");
        }
        JsonElement Find(string type,string name)=>Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType."+type&&n.GetProperty("name").GetString()==name&&!n.GetProperty("offscreen").GetBoolean());
        JsonElement EditorNode()=>Nodes().Where(n=>n.GetProperty("id").GetString()=="WpfTextView"&&!n.GetProperty("offscreen").GetBoolean()).OrderByDescending(n=>n.GetProperty("width").GetDouble()*n.GetProperty("height").GetDouble()).First();
        string Editor()=>EditorNode().GetProperty("text").GetString()!;
        bool Matches(string expected){string desired=expected.Replace("\r\n","\n");return Nodes().Where(n=>n.GetProperty("id").GetString()=="WpfTextView"&&!n.GetProperty("offscreen").GetBoolean()).Any(editor=>{string actual=editor.GetProperty("text").GetString()?.Replace("\r\n","\n")??"";return actual==desired||System.Text.RegularExpressions.Regex.Replace(actual,@"(?m)^\d+ ","")==desired;});}
        void EditorIs(string expected,string name)=>ObserveUntil(name,()=>Matches(expected));
        void Open(string path,string name)
        {
            Chord(0x11,0x4F);ObserveUntil(name+"-picker",()=>Preview().GetProperty("activeText").GetString()=="Open File");
            var field=Nodes().First(n=>n.GetProperty("id").GetString()=="1148"&&n.GetProperty("type").GetString()=="ControlType.Edit");
            Click(field);Chord(0x11,0x41);Type(path);Key(0x0D);
            ObserveUntil(name+"-opened",()=>Preview().GetProperty("activeText").GetString()!.Contains(Path.GetFileName(path),StringComparison.Ordinal)&&Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView")&&Matches(File.ReadAllText(path)));
        }
        void ShowIde(){for(int i=0;i<8&&Preview().GetProperty("root").GetInt64()!=Preview().GetProperty("mainRoot").GetInt64();i++){Button("Next window");Thread.Sleep(100);}Require(Preview().GetProperty("root").GetInt64()==Preview().GetProperty("mainRoot").GetInt64(),"Could not return to IDE");}
        void Menu(string name,string evidence){Inspect(evidence+"-before");Click(Find("MenuItem",name));ObserveUntil(evidence,()=>Preview().GetProperty("popupSurfaces").GetArrayLength()>0);}
        void Command(string menu,string command,string evidence){Menu(menu,evidence+"-menu");Click(Nodes().Last(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&n.GetProperty("name").GetString()==command&&!n.GetProperty("offscreen").GetBoolean()));}
        void Case(string name,Action action)
        {
            var t=Stopwatch.StartNew();string? error=null;string? recovery=null;
            try{action();Console.WriteLine("PASS "+name);}catch(Exception e){error=e.ToString();failures++;Console.WriteLine("FAIL "+name+": "+e.Message);try{Inspect("failure-"+results.Count);Key(0x1B);Thread.Sleep(300);Key(0x1B);ShowIde();}catch{}}
            if(error!=null)try
            {
                if(qa.GetProcessIds().Any(id=>{try{using var p=Process.GetProcessById(id);return p.ProcessName=="ViavwDisposable";}catch{return false;}}))
                {ShowIde();Chord(0x10,0x74);Wait(()=>!qa.GetProcessIds().Any(id=>{try{using var p=Process.GetProcessById(id);return p.ProcessName=="ViavwDisposable";}catch{return false;}}),"Debugger did not stop during recovery",8000);recovery="Owned debuggee stopped through viewer Shift+F5";}
            }catch(Exception e){recovery=e.Message;}
            results.Add(new{name,passed=error==null,error,recovery,milliseconds=t.Elapsed.TotalMilliseconds});
            File.WriteAllText(Path.Combine(output,"progress.json"),JsonSerializer.Serialize(new{results,menus,timings},new JsonSerializerOptions{WriteIndented=true}));
        }
        object Resources()
        {
            var values=new List<object>();foreach(int id in qa.GetProcessIds())try{using var p=Process.GetProcessById(id);values.Add(new{pid=id,name=p.ProcessName,cpuMilliseconds=p.TotalProcessorTime.TotalMilliseconds,workingSet=p.WorkingSet64,privateBytes=p.PrivateMemorySize64,handles=p.HandleCount});}catch(ArgumentException){}catch(InvalidOperationException){}
            return new{wallMilliseconds=clock.Elapsed.TotalMilliseconds,processes=values};
        }
        object? beforeResources=null,afterResources=null;
        try
        {
            string[] launchArguments=[incrementalPaint?"--run-vs-damage-viewer":preciseMouseWaits?"--run-vs-precise-viewer":asyncStatusReports?"--run-vs-async-report-viewer":windowDcPreview?"--run-vs-dc-viewer":"--run-vs-private-viewer",setupFile,output];
            if(postedFunctionKeys)launchArguments=["--run-vs-function-viewer",setupFile,output];
            if(fastPreview)launchArguments=[preparePointer?"--run-vs-prepared-viewer":"--run-vs-fast-viewer",setupFile,output];
            qa.LaunchApplication(viewerExecutable,launchArguments,output);
            Wait(()=>File.Exists(Path.Combine(output,"viewer-preview.json")),"Viewer did not start",90000);
            var windows=qa.GetWindows();viewer=windows.Single(w=>w.Text=="VIAVW — private application layer").Handle;canvas=windows.Single(w=>w.Parent==viewer&&w.Width>900&&w.Height>200).Handle;
            qa.EnablePrivateInputSession();Button("Human control");Wait(()=>Preview().GetProperty("owner").GetString()=="Human","Human control missing");
            ObserveUntil("01-startup",()=>Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs",StringComparison.Ordinal)&&Matches(originalFirst),60000);
            sourcePid=Preview().GetProperty("applicationPid").GetInt32();beforeResources=Resources();
            Case("Startup opens actual installed IDE and exact disposable source",()=>Require(Matches(originalFirst),"Startup editor differs from disk"));
            if(!debuggerOnly&&!focused)
            {
            Case("Editor click Enter rapid typing Backspace Delete undo redo save",()=>
            {
                var editor=EditorNode();Click(editor,180,8);Chord(0x11,0x41);Type("// line one");Key(0x0D);Type("// line twoX");Key(0x08);
                EditorIs("// line one\n// line two","02-enter-backspace");Key(0x24);Key(0x2E);EditorIs("// line one\n/ line two","03-delete");
                Chord(0x11,0x5A);EditorIs("// line one\n// line two","04-undo");Chord(0x11,0x59);EditorIs("// line one\n/ line two","05-redo");
                Chord(0x11,0x41);string expected="namespace ViavwDisposable { public static class Greeting { public const string Text = \"Original greeting\"; } }";Type(expected);EditorIs(expected,"06-restored");Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)==expected,"Save mismatch");
            });
            Case("Switch documents with Ctrl+Tab and open another file",()=>{Open(setup.Second,"07-second");Chord(0x11,0x09);ObserveUntil("08-switch-back",()=>Matches(File.ReadAllText(setup.First)));Open(program,"09-program");});
            Case("Find selects actual matching source text",()=>{Open(program,"find-source");Chord(0x11,0x46);Type("startupValue");Key(0x1B);ObserveUntil("10-find",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selection").EnumerateArray().Any(s=>s.GetString()=="startupValue")));});
            Case("Build creates executable with both compiled constants",()=>
            {Chord(0x11,0x10,0x42);Wait(()=>File.Exists(exe),"IDE did not produce runnable executable",60000);Require(VisualStudioResearch.ReadCompiledConstants(exe).GetValueOrDefault("Text")=="Original greeting","Built constant mismatch");ObserveUntil("11-built",()=>Nodes().Any(n=>(n.GetProperty("text").GetString()??"").Contains("1 succeeded, 0 failed",StringComparison.Ordinal)),45000);});
            Case("Ctrl+F5 runs actual program and viewer controls child window",()=>
            {
                Chord(0x11,0x74);Wait(()=>qa.GetProcessIds().Any(id=>{try{using var p=Process.GetProcessById(id);return p.ProcessName=="ViavwDisposable";}catch{return false;}}),"Ctrl+F5 did not launch executable",45000);
                Wait(()=>Preview().GetProperty("availableSurfaces").EnumerateArray().Any(w=>w.GetProperty("Text").GetString()=="VIAVW disposable running application"),"Running program absent from layer window selector");
                Button("Next window");ObserveUntil("12-running-application",()=>Preview().GetProperty("activeText").GetString()=="VIAVW disposable running application");
                Require(Nodes().Any(n=>n.GetProperty("name").GetString()=="Original greeting | Draft | answer=42"),"Actual running program content differs");
                Click(Find("Button","Count: 0"));ObserveUntil("13-running-click",()=>Nodes().Any(n=>n.GetProperty("name").GetString()=="Count: 1"));
                var edit=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.Edit");Click(edit);Chord(0x11,0x41);Type("Typed into running program");
                ObserveUntil("14-running-text",()=>Nodes().Any(n=>n.GetProperty("value").GetString()=="Typed into running program"));
                Chord(0x12,0x73);Wait(()=>!qa.GetProcessIds().Any(id=>{try{using var p=Process.GetProcessById(id);return p.ProcessName=="ViavwDisposable";}catch{return false;}}),"Running program did not close");ShowIde();
            });
            }
            if(!focused)Case("F9 breakpoint F5 break menu step F10 continue Shift+F5 stop",()=>
            {
                ShowIde();Open(program,"15-debug-source");Chord(0x11,0x47);Type("12");Key(0x0D);Key(0x78);Key(0x74);
                ObserveUntil("16-debug-break",()=>Preview().GetProperty("activeText").GetString()!.Contains("(Debugging)",StringComparison.OrdinalIgnoreCase)&&Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selectionStartLines").EnumerateArray().Any(l=>l.GetInt32()==12)),45000);
                Menu("Debug","17-debug-menu");Require(Nodes().Any(n=>(n.GetProperty("name").GetString()??"").StartsWith("Continue",StringComparison.Ordinal)),"Debugger did not reach break state");
                Click(Find("MenuItem","Step Over"));ObserveUntil("18a-debug-menu-step",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selectionStartLines").EnumerateArray().Any(l=>l.GetInt32()==13)));
                Key(0x79);ObserveUntil("18-debug-step",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selectionStartLines").EnumerateArray().Any(l=>l.GetInt32()==14)));Key(0x74);
                Wait(()=>Preview().GetProperty("availableSurfaces").EnumerateArray().Any(w=>w.GetProperty("Text").GetString()=="VIAVW disposable running application"),"Continue did not reach running application",30000);
                Chord(0x10,0x74);Wait(()=>!qa.GetProcessIds().Any(id=>{try{using var p=Process.GetProcessById(id);return p.ProcessName=="ViavwDisposable";}catch{return false;}}),"Stop debugging did not end owned debuggee");Inspect("19-debug-stopped");
            });
            if(!debuggerOnly)
            {
            // Enumerate real top-level menus and every visible item in their first
            // popup. Opening a menu is coverage of presentation/input, not proof
            // that every discovered command works.
            ShowIde();Key(0x1B);Inspect("20-before-menu-inventory");
            foreach(var n in Nodes().Where(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&n.GetProperty("depth").GetInt32()<=4&&!n.GetProperty("offscreen").GetBoolean()))menuBarGeometry.Add(MenuGeometry(n));
            var topMenus=Nodes().Where(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&n.GetProperty("depth").GetInt32()<=4&&!n.GetProperty("offscreen").GetBoolean()).Select(n=>n.GetProperty("name").GetString()!).Where(n=>n!="System").Distinct().ToArray();
            if(!focused)
            {
            foreach(string name in topMenus)Case("Menu opens and dismisses: "+name,()=>
            {Key(0x1B);Menu(name,"menu-"+menus.Count);menus.Add(new{name,items=Nodes().Where(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem").Select(n=>new{name=n.GetProperty("name").GetString(),offscreen=n.GetProperty("offscreen").GetBoolean(),depth=n.GetProperty("depth").GetInt32()}).ToArray()});Key(0x1B);});
            Case("Tools Options opens by pointer and Cancel preserves IDE",()=>{Menu("Tools","21-tools");Click(Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&(n.GetProperty("name").GetString()??"").StartsWith("Options",StringComparison.Ordinal)));ObserveUntil("22-options",()=>Preview().GetProperty("activeText").GetString()=="Options");Click(Find("Button","Cancel"));Wait(()=>Preview().GetProperty("root").GetInt64()==Preview().GetProperty("mainRoot").GetInt64(),"Cancel Options did not return to IDE");});
            Case("Go To Line and keyboard selection operate on actual code",()=>
            {
                Open(program,"25-navigation-source");Chord(0x11,0x47);Type("12");Key(0x0D);
                ObserveUntil("26-go-to-line",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selectionStartLines").EnumerateArray().Any(l=>l.GetInt32()==12)));
                Key(0x24);Chord(0x10,0x23);ObserveUntil("27-line-selection",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selection").EnumerateArray().Any(s=>(s.GetString()??"").Contains("startupValue += 2",StringComparison.Ordinal))));Key(0x27);
            });
            Case("Build menu Clean Rebuild and Configuration Manager",()=>
            {
                Command("Build","Clean Solution","28-clean");Wait(()=>!File.Exists(exe),"Clean did not remove owned executable",45000);Inspect("29-cleaned");
                Command("Build","Rebuild Solution","30-rebuild");Wait(()=>File.Exists(exe),"Rebuild did not recreate executable",45000);ObserveUntil("31-rebuilt",()=>Nodes().Any(n=>(n.GetProperty("text").GetString()??"").Contains("1 succeeded, 0 failed",StringComparison.Ordinal)));
                Command("Build","Configuration Manager...","32-configuration");ObserveUntil("33-configuration-manager",()=>Preview().GetProperty("activeText").GetString()=="Configuration Manager");
                var close=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.Button"&&n.GetProperty("name").GetString()=="Close");Click(close);Wait(()=>Preview().GetProperty("root").GetInt64()==Preview().GetProperty("mainRoot").GetInt64(),"Configuration Manager did not close");
            });
            Case("Tool windows open by mouse and editor focus recovers",()=>
            {
                foreach(string window in new[]{"Solution Explorer","Error List","Output","Properties Window"})
                {Command("View",window,"34-view-"+window.Replace(" ","-"));Inspect("35-window-"+window.Replace(" ","-"));Require(Nodes().Any(n=>n.GetProperty("name").GetString()==window||window=="Properties Window"&&n.GetProperty("name").GetString()=="Properties"),"Requested tool window was not observed: "+window);}
                Open(setup.Second,"36-focus-restored");var editor=EditorNode();Click(editor,150,8);Require(Matches(File.ReadAllText(setup.Second)),"Tool navigation changed document");
            });
            Case("Compilation errors are visible and recover after source correction",()=>
            {
                Open(setup.First,"37-error-source");var editor=EditorNode();Click(editor,120,8);Chord(0x11,0x41);
                const string invalid="namespace ViavwDisposable { public static class Greeting { public const string Text = VIAVW_MISSING_VALUE; } }";Type(invalid);EditorIs(invalid,"38-invalid-editor");Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)==invalid,"Invalid disposable source did not save");Chord(0x11,0x10,0x42);
                Command("View","Output","39-show-build-errors");
                ObserveUntil("39-build-errors",()=>Nodes().Any(n=>(n.GetProperty("text").GetString()??"").Contains("0 succeeded, 1 failed",StringComparison.Ordinal)&&(n.GetProperty("text").GetString()??"").Contains("CS0103",StringComparison.Ordinal)),45000);
                Open(setup.First,"40-repair-source");editor=EditorNode();Click(editor,120,8);Chord(0x11,0x41);const string repaired="namespace ViavwDisposable { public static class Greeting { public const string Text = \"Original greeting\"; } }";Type(repaired);EditorIs(repaired,"41-repaired-editor");Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)==repaired,"Repaired source did not save");Chord(0x11,0x10,0x42);
                ObserveUntil("42-build-recovered",()=>Nodes().Any(n=>(n.GetProperty("text").GetString()??"").Contains("1 succeeded, 0 failed",StringComparison.Ordinal)),45000);
            });
            Case("Human automation handoff rejects text and preserves process",()=>{Open(setup.Second,"23-handoff-source");string expected=Editor();Button("Return to automation");Wait(()=>qa.GetWindows().Any(w=>w.Text.StartsWith("Automation | PID ",StringComparison.Ordinal)||w.Text.Contains("Automation owns input",StringComparison.Ordinal)),"Automation owner missing");qa.PostKeyboardInput(canvas,"REJECTED_HUMAN");Thread.Sleep(300);Button("Human control");Wait(()=>qa.GetWindows().Any(w=>w.Text.StartsWith("Human | PID ",StringComparison.Ordinal)||w.Text.Contains("Human owns input",StringComparison.Ordinal)),"Human owner missing");EditorIs(expected,"24-handoff-preserved");Require(Preview().GetProperty("applicationPid").GetInt32()==sourcePid,"Handoff restarted IDE");});
            }
            if(extended&&(!focused||designerOnly))Case("Add Windows Form opens designer and switches to code",()=>
            {
                Command("Project","Add Form (Windows Forms)...","designer-add-form");
                ObserveUntil("designer-add-dialog",()=>Preview().GetProperty("root").GetInt64()!=Preview().GetProperty("mainRoot").GetInt64()&&Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.Edit"&&(n.GetProperty("value").GetString()??"").EndsWith(".cs",StringComparison.OrdinalIgnoreCase)),45000);
                var filename=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.Edit"&&(n.GetProperty("value").GetString()??"").EndsWith(".cs",StringComparison.OrdinalIgnoreCase));Click(filename);Chord(0x11,0x41);Type("ResearchForm.cs");Inspect("designer-new-name");
                Require(Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.Edit"&&n.GetProperty("value").GetString()=="ResearchForm.cs"),"Add item filename was not verified");Click(Find("Button","Add"));
                Wait(()=>File.Exists(Path.Combine(directory,"ResearchForm.cs"))&&File.Exists(Path.Combine(directory,"ResearchForm.Designer.cs")),"IDE did not create the form and designer files",45000);
                ObserveUntil("designer-created",()=>Preview().GetProperty("activeText").GetString()!.Contains("ResearchForm.cs",StringComparison.Ordinal)&&Preview().GetProperty("activeText").GetString()!.Contains("Design",StringComparison.OrdinalIgnoreCase),60000);
                bool immediateTransitionProbe=postedFunctionKeys||fastPreview;
                string? generatedCode=immediateTransitionProbe?File.ReadAllText(Path.Combine(directory,"ResearchForm.cs")):null;
                if(immediateTransitionProbe){qa.PostKey(canvas,0x76);qa.PostKeyboardInput(canvas,"//POSTED_FN_PROBE");}
                else Key(0x76);
                ObserveUntil("designer-view-code",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&(n.GetProperty("text").GetString()??"").Contains("partial class ResearchForm",StringComparison.Ordinal)&&(!immediateTransitionProbe||(n.GetProperty("text").GetString()??"").Contains("//POSTED_FN_PROBE",StringComparison.Ordinal))));
                if(immediateTransitionProbe)
                {
                    int undo=0;while(Editor().ReplaceLineEndings("\n")!=generatedCode!.ReplaceLineEndings("\n"))
                    {Require(undo++<20,"Undo did not restore generated form after immediate F7 typing test");Chord(0x11,0x5A);Inspect("designer-undo-probe-"+undo);}
                    Chord(0x11,0x53);Wait(()=>File.ReadAllText(Path.Combine(directory,"ResearchForm.cs"))==generatedCode,"Generated form did not restore after immediate F7 typing test");
                }
                Chord(0x10,0x76);ObserveUntil("designer-return",()=>Preview().GetProperty("activeText").GetString()!.Contains("Design",StringComparison.OrdinalIgnoreCase));Chord(0x11,0x10,0x53);
            });
            if(extended&&!projectOnly&&!designerOnly)Case("Discover accessible nested menus without executing leaf commands",()=>
            {
                Open(program,"nested-code-context");
                var pending=new Queue<string[]>(topMenus.Select(n=>new[]{n}));var seen=new HashSet<string>();int errors=0;
                while(pending.Count>0&&menuTree.Count<120)
                {
                    string[] path=pending.Dequeue();string identity=string.Join(" > ",path);if(!seen.Add(identity))continue;
                    try
                    {
                        Key(0x1B);Key(0x1B);Key(0x1B);Menu(path[0],"nested-current");
                        for(int i=1;i<path.Length;i++)
                        {
                            var submenu=PopupNodes().First(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&n.GetProperty("name").GetString()==path[i]&&!n.GetProperty("offscreen").GetBoolean()&&n.GetProperty("enabled").GetBoolean()&&n.GetProperty("expandCollapseState").GetString() is "Collapsed" or "Expanded");
                            long parentPopup=Preview().GetProperty("popupSurfaces")[0].GetProperty("Handle").GetInt64();
                            Click(submenu);
                            ObserveUntil("nested-current",()=>{var popups=Preview().GetProperty("popupSurfaces");return popups.GetArrayLength()>0&&popups[0].GetProperty("Handle").GetInt64()!=parentPopup;},10000);
                        }
                        var items=PopupNodes().Where(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&!n.GetProperty("offscreen").GetBoolean()).DistinctBy(n=>(n.GetProperty("name").GetString(),n.GetProperty("x").GetDouble(),n.GetProperty("y").GetDouble())).ToArray();
                        menuTree.Add(new{path,passed=true,items=items.Select(n=>new{name=n.GetProperty("name").GetString(),enabled=n.GetProperty("enabled").GetBoolean(),expand=n.GetProperty("expandCollapseState").GetString()}).ToArray()});
                        if(path.Length<3)foreach(var child in items.Where(n=>n.GetProperty("enabled").GetBoolean()&&n.GetProperty("expandCollapseState").GetString()=="Collapsed"))
                        {string label=child.GetProperty("name").GetString()!;if(!path.Contains(label))pending.Enqueue([..path,label]);}
                    }
                    catch(Exception e){errors++;menuTree.Add(new{path,passed=false,error=e.Message});try{Inspect("nested-failure-"+menuTree.Count);}catch{}}
                    File.WriteAllText(Path.Combine(output,"menu-tree.json"),JsonSerializer.Serialize(new{menuTree,remaining=pending.Count,depthLimit=3,menuLimit=120,leafCommandsExecuted=false},new JsonSerializerOptions{WriteIndented=true}));
                }
                Key(0x1B);Key(0x1B);Key(0x1B);Key(0x1B);
                ObserveUntil("nested-dismissed",()=>Preview().GetProperty("popupSurfaces").GetArrayLength()==0&&!Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.MenuItem"&&n.GetProperty("focused").GetBoolean()));
                Require(errors==0,"Some nested menus were not observable; see menu-tree.json");
            });
            if(extended&&!menusOnly&&!designerOnly)Case("New Project creates a disposable executable from an installed template",()=>
            {
                File.WriteAllText(Path.Combine(output,"Directory.Build.props"),"<Project><PropertyGroup><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings></PropertyGroup></Project>");
                ShowIde();Chord(0x11,0x10,0x53);Chord(0x11,0x10,0x4E);
                ObserveUntil("43-new-project",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="PART_SearchBox"),60000);
                var search=Nodes().First(n=>n.GetProperty("id").GetString()=="PART_SearchBox");Click(search);Type("Console App (.NET Framework)");
                ObserveUntil("44-template-search",()=>Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.ListItem"&&n.GetProperty("name").GetString()=="Console App (.NET Framework)"),60000);
                var suggestion=Nodes().FirstOrDefault(n=>n.GetProperty("id").GetString()=="SearchControlMRUItem"&&n.GetProperty("name").GetString()=="Console App (.NET Framework)"&&!n.GetProperty("offscreen").GetBoolean());
                if(suggestion.ValueKind!=JsonValueKind.Undefined)Click(suggestion);else Key(0x1B);
                ObserveUntil("44a-actual-template-cards",()=>Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.ListItem"&&n.GetProperty("name").GetString()=="Console App (.NET Framework)"&&n.GetProperty("id").GetString()!="SearchControlMRUItem"&&n.GetProperty("height").GetDouble()>30),60000);
                var template=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.ListItem"&&n.GetProperty("name").GetString()=="Console App (.NET Framework)"&&n.GetProperty("id").GetString()!="SearchControlMRUItem"&&n.GetProperty("height").GetDouble()>30&&!n.GetProperty("offscreen").GetBoolean());Click(template);ObserveUntil("45-template-selected",()=>Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.Button"&&n.GetProperty("name").GetString()=="Next"&&n.GetProperty("enabled").GetBoolean()));Click(Find("Button","Next"));
                ObserveUntil("46-new-project-configuration",()=>Nodes().Any(n=>n.GetProperty("id").GetString()=="projectNameText"));
                void Field(string id,string text)
                {
                    var field=Nodes().First(n=>n.GetProperty("type").GetString()=="ControlType.Edit"&&n.GetProperty("id").GetString()==id);
                    Require(field.GetProperty("enabled").GetBoolean(),"Cannot type into disabled project field: "+id);
                    Click(field);Chord(0x11,0x41);Type(text);
                    ObserveUntil("project-field-"+id,()=>Nodes().Any(n=>n.GetProperty("id").GetString()==id&&n.GetProperty("value").GetString()==text));
                }
                Field("projectNameText","CreatedFromIde");Field("PART_EditableTextBox",output);
                if(Nodes().First(n=>n.GetProperty("id").GetString()=="solutionNameText").GetProperty("enabled").GetBoolean())Field("solutionNameText","CreatedFromIde");
                Inspect("47-project-destination");
                Require(Nodes().Any(n=>n.GetProperty("id").GetString()=="PART_EditableTextBox"&&n.GetProperty("value").GetString()==output),"Project destination was not the disposable output directory");
                var same=Nodes().FirstOrDefault(n=>n.GetProperty("id").GetString()=="slnProjInSameDirChk");if(same.ValueKind!=JsonValueKind.Undefined&&same.GetProperty("toggleState").GetString()=="Off"){Click(same);Inspect("48-project-same-directory");}
                foreach(var git in Nodes().Where(n=>n.GetProperty("type").GetString()=="ControlType.CheckBox"&&(n.GetProperty("name").GetString()??"").Contains("Git",StringComparison.OrdinalIgnoreCase)))
                    if(git.GetProperty("toggleState").GetString()=="On"){Click(git);Inspect("48a-no-git-initialization");}
                Require(!Nodes().Any(n=>n.GetProperty("type").GetString()=="ControlType.CheckBox"&&(n.GetProperty("name").GetString()??"").Contains("Git",StringComparison.OrdinalIgnoreCase)&&n.GetProperty("toggleState").GetString()!="Off"),"Git creation must remain disabled");
                var framework=Nodes().First(n=>n.GetProperty("id").GetString()=="fxVersionCmb");Click(framework,(int)framework.GetProperty("width").GetDouble()-8);
                ObserveUntil("48b-framework-options",()=>Nodes().Any(n=>n.GetProperty("name").GetString()==".NET Framework 4.8"&&n.GetProperty("type").GetString()=="ControlType.ListItem"&&!n.GetProperty("offscreen").GetBoolean()));
                var framework48=Nodes().First(n=>n.GetProperty("name").GetString()==".NET Framework 4.8"&&n.GetProperty("type").GetString()=="ControlType.ListItem");Click(framework48);Inspect("48c-framework-selected");
                Click(Find("Button","Create"));
                ObserveUntil("49-created-files",()=>Directory.EnumerateFiles(output,"CreatedFromIde.*proj",SearchOption.AllDirectories).Any(),90000);
                ObserveUntil("49-created-project",()=>Preview().GetProperty("root").GetInt64()==Preview().GetProperty("mainRoot").GetInt64()&&Preview().GetProperty("activeText").GetString()!.Contains("CreatedFromIde",StringComparison.Ordinal),90000);
                Chord(0x11,0x10,0x42);Wait(()=>Directory.EnumerateFiles(output,"CreatedFromIde.exe",SearchOption.AllDirectories).Any(),"Created project did not build an executable",60000);Inspect("50-created-project-built");
            });
            }
            afterResources=Resources();
        }
        catch(Exception e){fatal=e.ToString();Console.Error.WriteLine(e);}
        finally{if(!qa.Closed){owned=qa.GetProcessIds();try{qa.CloseGracefully(TimeSpan.FromSeconds(8));}catch{}}qa.Dispose();}
        var physical=monitor.Complete(owned);var after=VisualStudioResearch.PersonalSettings(allowUnavailable:true);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));
        bool readableUnchanged=personal.Where(kv=>!kv.Value.StartsWith("unavailable:")).All(kv=>after.GetValueOrDefault(kv.Key)==kv.Value);
        bool passed=fatal==null&&failures==0&&cleanup&&readableUnchanged&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"end-to-end.json"),JsonSerializer.Serialize(new{passed,fatal,failures,extended,windowDcPreview,asyncStatusReports,preciseMouseWaits,viewerExecutable,results,menus,menuTree,timings,beforeResources,afterResources,cleanup,owned,sourcePid,physical,readablePersonalSettingsUnchanged=readableUnchanged,personalBefore=personal,personalAfter=after,unavailableSettings=personal.Where(kv=>kv.Value.StartsWith("unavailable:")).Select(kv=>kv.Key),setupFile,assemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"LayerHost.dll")))),physicalHumanAcceptance="pending",coverage="Selected workflows and bounded menu inventory; not exhaustive command compatibility. Git, account, extension installation, clipboard mutation and external deployment are excluded by project boundaries."},new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
}

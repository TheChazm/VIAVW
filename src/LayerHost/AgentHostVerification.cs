using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class AgentHostVerification
{
    internal sealed class Client:IDisposable
    {
        internal readonly Process Process;
        internal readonly HashSet<int> Owned=[];
        internal readonly List<object> Calls=[];
        internal readonly HashSet<string> Sessions=[];
        int sequence;readonly Task<string> stderr;
        readonly string inputValidationMode;
        internal Client(string output,string? validationMode=null)
        {
            inputValidationMode=validationMode??"full-window-scan";
            Process=System.Diagnostics.Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"LayerHost.exe")){ArgumentList={"--agent-stdio",output},UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true})!;
            Owned.Add(Process.Id);stderr=Process.StandardError.ReadToEndAsync();
        }
        internal JsonElement Raw(string line)
        {
            var watch=Stopwatch.StartNew();Process.StandardInput.WriteLine(line);Process.StandardInput.Flush();
            string reply=Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult()??throw new IOException("Agent host disconnected: "+(stderr.IsCompleted?stderr.Result:""));
            using var json=JsonDocument.Parse(reply);var value=json.RootElement.Clone();
            Calls.Add(new{milliseconds=watch.Elapsed.TotalMilliseconds,response=value});
            void Collect(JsonElement element)
            {
                if(element.ValueKind==JsonValueKind.Object)foreach(var property in element.EnumerateObject())
                {if(property.Name=="ownedProcessIds")foreach(var id in property.Value.EnumerateArray())Owned.Add(id.GetInt32());else Collect(property.Value);}
                else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Collect(item);
            }
            Collect(value);
            if(value.GetProperty("ok").GetBoolean()&&value.GetProperty("data").ValueKind==JsonValueKind.Object&&value.GetProperty("data").TryGetProperty("observation",out _)&&value.GetProperty("data").TryGetProperty("id",out var sessionId))Sessions.Add(sessionId.GetString()!);
            return value;
        }
        internal string Request(string op,string? session=null,object? fields=null,string? id=null)
        {
            var values=new Dictionary<string,object?>{{"protocol",1},{"id",id??"request-"+(++sequence)},{"op",op}};
            if(session!=null)values["session"]=session;
            if(op=="input"&&inputValidationMode!="full-window-scan")values["validationMode"]=inputValidationMode;
            if(fields!=null)foreach(var p in JsonSerializer.SerializeToElement(fields).EnumerateObject())values[p.Name]=p.Value.Clone();
            return JsonSerializer.Serialize(values);
        }
        internal JsonElement Call(string op,string? session=null,object? fields=null)
        {
            var reply=Raw(Request(op,session,fields));if(!reply.GetProperty("ok").GetBoolean()){var error=reply.GetProperty("error");throw new AgentException(error.GetProperty("code").GetString()!,error.GetProperty("message").GetString()!,error.GetProperty("outcome").GetString()!);}return reply.GetProperty("data");
        }
        internal void End()
        {
            if(Process.HasExited)return;Process.StandardInput.Close();if(!Process.WaitForExit(20000))throw new TimeoutException("Agent host EOF cleanup did not finish");
            if(Process.ExitCode!=0)throw new InvalidOperationException("Agent host exited "+Process.ExitCode+": "+stderr.GetAwaiter().GetResult());
        }
        public void Dispose(){try{End();}finally{if(!Process.HasExited){Process.Kill(true);Process.WaitForExit(10000);}Process.Dispose();}}
    }
    internal static int Run(string output,string? validationMode=null)
    {
        GpuBridgeVerification.PrepareOutput(output);using var physicalMonitor=new ResearchDesktopMonitor();
        var checks=new Dictionary<string,bool>();var owned=new HashSet<int>();var pictures=new List<string>();string? failure=null;var clients=new List<Client>();
        string personal=InstalledNotepadResearch.PersonalSettingsDigest();
        void Check(string name,bool result){checks[name]=result;Console.WriteLine((result?"PASS ":"FAIL ")+name);if(!result)throw new InvalidOperationException(name);}
        void Wait(Func<bool> test,string reason,int milliseconds=15000){var time=Stopwatch.StartNew();while(time.ElapsedMilliseconds<milliseconds){try{if(test())return;}catch(IOException error)when((error.HResult&0xffff) is 32 or 33){/* App save has not released its file yet; repeat only the read. */}Thread.Sleep(50);}throw new TimeoutException(reason);}
        JsonElement State(Client c,string id)=>c.Call("status",id).GetProperty("session");
        long Revision(Client c,string id)=>State(c,id).GetProperty("revision").GetInt64();
        JsonElement Observe(Client c,string id,bool capture=false,bool accessible=false)
        {
            JsonElement response=default;
            for(int attempt=0;attempt<5;attempt++)
            {
                response=c.Raw(c.Request("observe",id,new{capture,accessibility=accessible}));
                if(response.GetProperty("ok").GetBoolean())break;
                if(response.GetProperty("error").GetProperty("code").GetString() is not ("observation_changed" or "no_surface" or "capture_unavailable"))throw new InvalidOperationException(response.GetRawText());
                Thread.Sleep(30);
            }
            if(!response.GetProperty("ok").GetBoolean()){var error=response.GetProperty("error");throw new AgentException(error.GetProperty("code").GetString()!,error.GetProperty("message").GetString()!);}
            var r=response.GetProperty("data");
            if(capture)pictures.Add(r.GetProperty("result").GetProperty("image").GetProperty("path").GetString()!);
            return r;
        }
        long Root(Client c,string id)=>Observe(c,id).GetProperty("session").GetProperty("root").GetInt64();
        JsonElement[] Nodes(JsonElement observation)
        {using var doc=JsonDocument.Parse(File.ReadAllText(observation.GetProperty("result").GetProperty("accessibilityPath").GetString()!));return doc.RootElement.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).ToArray();}
        void Input(Client c,string id,long window,string kind,object? fields=null)
        {
            var values=new Dictionary<string,object?>{{"expectedRevision",Revision(c,id)},{"window",window},{"kind",kind}};
            if(fields!=null)foreach(var p in JsonSerializer.SerializeToElement(fields).EnumerateObject())values[p.Name]=p.Value.Clone();
            c.Call("input",id,values);
        }
        void Chord(Client c,string id,long target,params int[] keys)=>Input(c,id,target,"chord",new{keys});
        void Text(Client c,string id,long target,string text)=>Input(c,id,target,"text",new{text});
        bool Title(Client c,string id,string text){try{var r=Observe(c,id);long root=r.GetProperty("session").GetProperty("root").GetInt64();return r.GetProperty("result").GetProperty("windows").EnumerateArray().Any(w=>w.GetProperty("Handle").GetInt64()==root&&w.GetProperty("Text").GetString()!.Contains(text,StringComparison.OrdinalIgnoreCase));}catch(AgentException error)when(error.Code is "observation_changed" or "no_surface"){return false;}}
        try
        {
            var budget=new AgentArtifacts(Path.Combine(output,"budget-probe"),8,1);budget.WriteAsync("probe","bin",new byte[8]).GetAwaiter().GetResult();bool rejected=false;
            try{budget.WriteAsync("extra","bin",new byte[1]).GetAwaiter().GetResult();}catch(AgentException e)when(e.Code=="artifact_budget"){rejected=true;}
            Check("observation quota rejects additional data without a large dataset",rejected);
            var client=new Client(Path.Combine(output,"host"),validationMode);clients.Add(client);
            Check("external client negotiates versioned protocol",client.Call("hello").GetProperty("protocol").GetInt32()==1);
            Check("malformed request is rejected and stream remains usable",!client.Raw("{broken").GetProperty("ok").GetBoolean()&&client.Call("hello").GetProperty("protocol").GetInt32()==1);
            Check("oversized request is bounded and stream recovers",client.Raw(new string('x',70000)).GetProperty("error").GetProperty("code").GetString()=="request_too_large"&&client.Call("hello").GetProperty("protocol").GetInt32()==1);
            var first=InstalledNotepadResearch.Prepare(Path.Combine(output,"first"),true);var second=InstalledNotepadResearch.Prepare(Path.Combine(output,"second"),true);
            string Create(InstalledNotepadResearch.Setup setup)=>client.Call("create",fields:new{profile="private-dc",executable=setup.Executable,arguments=setup.Arguments}).GetProperty("id").GetString()!;
            string one=Create(first),two=Create(second);
            var start=Observe(client,one,true,true);var secondStart=Observe(client,two);
            Check("two installed Notepad++ sessions own distinct processes and desktops",start.GetProperty("session").GetProperty("applicationPid").GetInt32()!=secondStart.GetProperty("session").GetProperty("applicationPid").GetInt32()&&start.GetProperty("session").GetProperty("layerId").GetString()!=secondStart.GetProperty("session").GetProperty("layerId").GetString());
            Check("background startup keeps viewers hidden",!start.GetProperty("session").GetProperty("viewerVisible").GetBoolean()&&!secondStart.GetProperty("session").GetProperty("viewerVisible").GetBoolean());
            long editor=start.GetProperty("result").GetProperty("windows").EnumerateArray().First(w=>w.GetProperty("ClassName").GetString()=="Scintilla"&&w.GetProperty("Visible").GetBoolean()).GetProperty("Handle").GetInt64();
            Input(client,one,editor,"click",new{x=100,y=10});Chord(client,one,editor,0x11,0x41);
            string line=client.Request("input",one,new{expectedRevision=Revision(client,one),window=editor,kind="text",text="Agent edited the first document"},"exactly-once-text");
            var initial=client.Raw(line);var repeat=client.Raw(line);Check("retry returns original response without replaying input",initial.GetRawText()==repeat.GetRawText());
            Chord(client,one,editor,0x11,0x53);Wait(()=>File.ReadAllText(first.Document)=="Agent edited the first document","First exact save");
            Check("installed editor selection edit and save produce exact disk content",true);
            var conflict=client.Raw(line.Replace("first document","wrong duplicate"));Check("conflicting request ID is rejected",conflict.GetProperty("error").GetProperty("code").GetString()=="id_conflict");
            for(int i=0;i<35;i++)State(client,one);
            var expired=client.Raw(line).GetProperty("error");Check("expired response never re-executes input or claims an original outcome",expired.GetProperty("code").GetString()=="result_expired"&&expired.GetProperty("outcome").GetString()=="unknown");
            Chord(client,one,editor,0x11,0x53);Check("duplicate handling preserves actual saved text",File.ReadAllText(first.Document)=="Agent edited the first document");
            string extra=Path.Combine(output,"first","second-file.txt");File.WriteAllText(extra,"Second file opened by ordinary dialog");
            Chord(client,one,editor,0x11,0x4F);Wait(()=>Title(client,one,"Open"),"Open dialog");long picker=Root(client,one);
            Chord(client,one,picker,0x11,0x41);Text(client,one,picker,extra);Input(client,one,picker,"key",new{key=0x0D});Wait(()=>Title(client,one,"second-file.txt"),"Second document opened");
            long main=Root(client,one);Chord(client,one,main,0x11,0x41);Text(client,one,main,"Edited second file");Chord(client,one,main,0x11,0x53);Wait(()=>File.ReadAllText(extra)=="Edited second file","Second exact save");
            Check("ordinary Open dialog editing and saving another file work through protocol",true);
            Chord(client,one,main,0x11,0x09);Wait(()=>Title(client,one,"disposable.txt"),"Switch documents");Check("Ctrl+Tab switches actual documents",true);
            main=Root(client,one);Chord(client,one,main,0x11,0x12,0x53);Wait(()=>Title(client,one,"Save As"),"Save As dialog");picker=Root(client,one);
            string saved=Path.Combine(output,"first","saved-as.txt");Chord(client,one,picker,0x11,0x41);Text(client,one,picker,saved);Input(client,one,picker,"key",new{key=0x0D});Wait(()=>File.Exists(saved)&&File.ReadAllText(saved)=="Agent edited the first document","Save As exact file");
            Check("ordinary Save As creates exact disposable document",true);Observe(client,one,true,true);
            long before=Revision(client,one);int pid=State(client,one).GetProperty("applicationPid").GetInt32();
            client.Call("attention",one,new{reason="Please inspect the disposable document"});client.Call("ownership",one,new{expectedRevision=before,owner="Human"});
            long human=Revision(client,one);main=Root(client,one);
            var denied=client.Raw(client.Request("input",one,new{expectedRevision=human,window=main,kind="text",text="BLOCKED"}));
            Check("automation is rejected during human ownership",denied.GetProperty("error").GetProperty("code").GetString()=="human_owns_input");
            client.Call("ownership",one,new{expectedRevision=human,owner="Automation"});
            denied=client.Raw(client.Request("input",one,new{expectedRevision=before,window=main,kind="text",text="STALE"}));
            Check("handoff round trip rejects stale automation and preserves same app",denied.GetProperty("error").GetProperty("code").GetString()=="stale_revision"&&State(client,one).GetProperty("applicationPid").GetInt32()==pid);
            Check("return clears attention request",State(client,one).GetProperty("attention").ValueKind==JsonValueKind.Null);
            denied=client.Raw(client.Request("input",two,new{expectedRevision=Revision(client,two),window=main,kind="text",text="CROSS_SESSION"}));
            Check("window from another session is rejected",denied.GetProperty("error").GetProperty("code").GetString()=="invalid_target");
            var idle=State(client,one);Thread.Sleep(2200);Check("hidden session performs no continuous captures",State(client,one).GetProperty("previewVersion").GetInt64()==idle.GetProperty("previewVersion").GetInt64());
            var vsFixture=Path.Combine(output,"vs-fixture");string seed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/vs-m30-runnable/launch.json"));VisualStudioFreshProfile.PrepareUsingProfile(vsFixture,seed);
            string vs=client.Call("create",fields:new{profile="visual-studio",setupFile=Path.Combine(vsFixture,"launch.json")}).GetProperty("id").GetString()!;
            Wait(()=>Title(client,vs,"Greeting.cs"),"Installed VS source startup",60000);var vsSetup=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(Path.Combine(vsFixture,"launch.json")))!;
            JsonElement readyEditor=default;JsonElement editorNode=default;
            Wait(()=>{readyEditor=Observe(client,vs,accessible:true);editorNode=Nodes(readyEditor).FirstOrDefault(n=>n.GetProperty("id").GetString()=="WpfTextView"&&(n.GetProperty("text").GetString()??"").Contains("public static class Greeting",StringComparison.Ordinal)&&!n.GetProperty("offscreen").GetBoolean());return editorNode.ValueKind==JsonValueKind.Object;},"Actual source editor readiness",30000);
            long vsMain=readyEditor.GetProperty("session").GetProperty("root").GetInt64();var vsOrigin=readyEditor.GetProperty("result").GetProperty("sourceClientOrigin");
            Input(client,vs,vsMain,"click",new{x=(int)editorNode.GetProperty("x").GetDouble()+80-vsOrigin.GetProperty("X").GetInt32(),y=(int)editorNode.GetProperty("y").GetDouble()+8-vsOrigin.GetProperty("Y").GetInt32()});
            Chord(client,vs,vsMain,0x11,0x41);
            Wait(()=>Nodes(Observe(client,vs,accessible:true)).Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&n.GetProperty("selectionCoversDocument").EnumerateArray().Any(v=>v.GetBoolean())),"Actual source selection before typing");
            Check("VS editor readiness click and real selection verified before editing",true);
            Text(client,vs,vsMain,"// Agent API editing proof");Input(client,vs,vsMain,"key",new{key=0x0D});Text(client,vs,vsMain,"// second linex");Input(client,vs,vsMain,"key",new{key=0x08});
            Wait(()=>Nodes(Observe(client,vs,accessible:true)).Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&System.Text.RegularExpressions.Regex.Replace(n.GetProperty("text").GetString()??"",@"(?m)^\d+ ","").Replace("\r\n","\n")=="// Agent API editing proof\n// second line"),"Actual VS edited text before Save");
            Check("VS typed content is observed before requesting save",true);Chord(client,vs,vsMain,0x11,0x53);
            Wait(()=>File.ReadAllText(vsSetup.First).Replace("\r\n","\n")=="// Agent API editing proof\n// second line","VS exact save");
            Check("installed Visual Studio API Enter text Backspace and save work",true);Observe(client,vs,true,true);
            Chord(client,vs,vsMain,0x11,0x4F);Wait(()=>Title(client,vs,"Open File"),"VS Open File dialog");picker=Root(client,vs);Chord(client,vs,picker,0x11,0x41);Text(client,vs,picker,vsSetup.Second);Input(client,vs,picker,"key",new{key=0x0D});Wait(()=>Title(client,vs,"Details.cs"),"VS second source opened");
            Check("installed Visual Studio ordinary file dialog and navigation work",true);
            var vsClose=client.Call("close",vs,new{expectedRevision=Revision(client,vs)});
            bool vsExitedAfterReply=false;
            if(!vsClose.GetProperty("result").GetProperty("closed").GetBoolean()&&vsClose.GetProperty("result").GetProperty("reason").GetString()=="application_or_prompt_running")
            {
                // A close response is a bounded snapshot, not proof that the IDE
                // failed to exit. Observe only; do not resend WM_CLOSE or discard
                // a possible save prompt while it finishes normal shutdown.
                var exitWait=Stopwatch.StartNew();
                while(exitWait.ElapsedMilliseconds<3000)
                {
                    var closeState=State(client,vs);
                    if(closeState.GetProperty("state").GetString() is "closed" or "application_root_exited"){vsExitedAfterReply=true;break;}
                    Thread.Sleep(50);
                }
            }
            Check("saved Visual Studio graceful close exits IDE or reports remaining owned processes",vsClose.GetProperty("result").GetProperty("closed").GetBoolean()||vsClose.GetProperty("result").GetProperty("reason").GetString()=="owned_processes_remaining"||vsExitedAfterReply);
            if(!vsClose.GetProperty("result").GetProperty("closed").GetBoolean())client.Call("close",vs,new{expectedRevision=Revision(client,vs),force=true});
            Check("explicit Visual Studio helper cleanup closes only owned session",State(client,vs).GetProperty("state").GetString()=="closed");
            var promptSetup=InstalledNotepadResearch.Prepare(Path.Combine(output,"prompt-docs"),true);string prompt=Create(promptSetup);long promptRoot=Root(client,prompt);
            Chord(client,prompt,promptRoot,0x11,0x41);Text(client,prompt,promptRoot,"Unsaved content must not disappear silently");
            Wait(()=>Title(client,prompt,"*"),"Unsaved document flag");
            var pending=client.Call("close",prompt,new{expectedRevision=Revision(client,prompt)});
            Check("graceful close preserves save prompt and transfers ownership",!pending.GetProperty("result").GetProperty("closed").GetBoolean()&&pending.GetProperty("result").GetProperty("reason").GetString()=="application_or_prompt_running"&&State(client,prompt).GetProperty("owner").GetString()=="Human"&&File.ReadAllText(promptSetup.Document)=="Original disposable document");
            var promptObservation=Observe(client,prompt,true,true);long promptWindow=promptObservation.GetProperty("session").GetProperty("root").GetInt64();
            Check("preserved prompt is observable as an owned modal",promptObservation.GetProperty("result").GetProperty("windows").EnumerateArray().Any(w=>w.GetProperty("Handle").GetInt64()==promptWindow&&w.GetProperty("ClassName").GetString()=="#32770"));
            client.Call("close",prompt,new{expectedRevision=Revision(client,prompt),force=true});Check("unsaved disposable session closes only on explicit force",State(client,prompt).GetProperty("state").GetString()=="closed");
            var twoState=State(client,two);int doomed=twoState.GetProperty("applicationPid").GetInt32();
            using(var target=Process.GetProcessById(doomed))
            {Check("fault injection targets only exact owned installed Notepad++ process",twoState.GetProperty("ownedProcessIds").EnumerateArray().Any(v=>v.GetInt32()==doomed)&&string.Equals(target.MainModule!.FileName,InstalledNotepadResearch.Executable,StringComparison.OrdinalIgnoreCase));target.Kill();target.WaitForExit(5000);}
            Wait(()=>State(client,two).GetProperty("state").GetString()=="application_root_exited","Report source exit");client.Call("close",two,new{expectedRevision=Revision(client,two),force=true});
            Chord(client,one,Root(client,one),0x11,0x53);Check("one source exit leaves peer session usable",File.ReadAllText(saved)=="Agent edited the first document");
            client.End();foreach(int id in client.Owned)owned.Add(id);Wait(()=>owned.All(id=>!CompatibilityVerification.Alive(id)),"EOF cleanup");Check("EOF releases owned application and worker trees",true);
            var crash=new Client(Path.Combine(output,"crash-host"),validationMode);clients.Add(crash);var crashSetup=InstalledNotepadResearch.Prepare(Path.Combine(output,"crash-docs"),true);
            crash.Call("create",fields:new{executable=crashSetup.Executable,arguments=crashSetup.Arguments});crash.Process.Kill();crash.Process.WaitForExit(5000);foreach(int id in crash.Owned)owned.Add(id);
            Wait(()=>owned.All(id=>!CompatibilityVerification.Alive(id)),"Host crash job cleanup");Check("abrupt host exit releases its owned application job",true);
            Check("personal Notepad++ settings unchanged",personal==InstalledNotepadResearch.PersonalSettingsDigest());
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);foreach(var client in clients)foreach(string session in client.Sessions)try{Observe(client,session,true,true);}catch{/* Retain the original failure; no input is replayed for diagnostics. */}}
        finally{foreach(var client in clients){foreach(int id in client.Owned)owned.Add(id);try{client.Dispose();}catch(Exception error){failure??=error.ToString();}}}
        var physical=physicalMonitor.Complete(owned.ToArray());bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"agent-verification.json"),JsonSerializer.Serialize(new{passed,failure,checks,pictures,cleanup,owned,physical,clients=clients.Select(c=>c.Calls).ToArray(),humanAcceptance="pending; ownership transitions tested programmatically, no physical human actions fabricated"},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

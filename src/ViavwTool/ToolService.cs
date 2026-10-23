using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ViavwTool;

internal sealed partial class ToolService
{
    internal const string Version="0.1.2";
    internal readonly string Root,Store,Host;
    readonly byte[] secret;
    internal ToolService(string root)
    {
        Root=Path.GetFullPath(root);Store=Path.Combine(Root,"artifacts","tool-layers");
        Host=Path.Combine(Root,"src","LayerHost","bin","Runtime","net10.0-windows","LayerHost.exe");
        var manifest=Files.Read(Path.Combine(Root,"package.json"));
        if(Hash(Path.ChangeExtension(Host,"dll"))!=manifest["hostAssemblySha256"]!.GetValue<string>())throw new ToolFailure("integrity_failed","Packaged layer host does not match package.json.");
        Directory.CreateDirectory(Store);
        string key=Path.Combine(Store,"observation-key.bin");
        try{using var stream=new FileStream(key,FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(RandomNumberGenerator.GetBytes(32));}
        catch(IOException)when(File.Exists(key)){}
        // A concurrently starting client may still be publishing the key.
        for(int attempt=0;;attempt++)
            try{secret=File.ReadAllBytes(key);if(secret.Length!=32)throw new IOException("Incomplete observation key.");break;}
            catch(IOException)when(attempt<50){Thread.Sleep(10);}
    }
    internal static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    [GeneratedRegex("^[a-f0-9]{32}$")] private static partial Regex LayerId();
    string DirectoryFor(string id)
    {
        if(!LayerId().IsMatch(id))throw new ToolFailure("invalid_layer","Use a layer ID returned by create or list.");
        return Path.Combine(Store,id);
    }
    JsonObject Record(string id)=>Files.Read(Path.Combine(DirectoryFor(id),"tool-record.json"));
    string Descriptor(string id)=>Path.Combine(DirectoryFor(id),"owner","layer-owner.json");
    static string Text(JsonObject arguments,string key)=>arguments[key]?.GetValue<string>()??throw new ToolFailure("invalid_arguments","Missing string: "+key);
    internal static bool Flag(JsonObject arguments,string key,bool fallback=false)=>arguments[key]?.GetValue<bool>()??fallback;
    string Sign(JsonObject proof)
    {
        byte[] data=Encoding.UTF8.GetBytes(proof.ToJsonString());
        return Convert.ToBase64String(data)+"."+Convert.ToHexString(HMACSHA256.HashData(secret,data));
    }
    JsonObject Proof(string token,string id,string epoch)
    {
        try
        {
            if(token.Length>4096)throw new FormatException();
            var parts=token.Split('.');if(parts.Length!=2)throw new FormatException();
            byte[] data=Convert.FromBase64String(parts[0]);
            if(!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(secret,data),Convert.FromHexString(parts[1])))throw new FormatException();
            var proof=JsonNode.Parse(data)!.AsObject();
            if(Text(proof,"layer")!=id||Text(proof,"epoch")!=epoch)throw new FormatException();
            return proof;
        }
        catch(Exception error)when(error is FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {throw new ToolFailure("stale_observation","Obtain a fresh observation from this layer and owner. Input was not sent.");}
    }
    async Task<OwnerConnection> Connect(string id)
    {
        var descriptor=Files.Read(Descriptor(id));
        if(!Files.Alive(descriptor))throw new ToolFailure("owner_disconnected","Application may be retained. Use recover, then observe and explicitly reconcile.");
        var client=new OwnerConnection(descriptor);
        try{await client.Connect();return client;}catch{await client.DisposeAsync();throw;}
    }
    static async Task<FileStream> Lock(string directory)
    {
        Directory.CreateDirectory(directory);var deadline=Stopwatch.StartNew();
        while(true)
        {
            try{return new FileStream(Path.Combine(directory,".control.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException)when(deadline.ElapsedMilliseconds<3500){await Task.Delay(10);}
        }
    }
    async Task Start(string id,string? keeper=null)
    {
        DetachedProcess.RequireRoom(keeper!=null);
        string output=Path.Combine(DirectoryFor(id),"owner");
        using var process=DetachedProcess.Start(Host,Root,keeper==null?["--layer-service",output]:["--layer-adopt-service",output,keeper]);
        Files.Write(Path.Combine(DirectoryFor(id),"last-owner-launch.json"),new(){["pid"]=process.Id,["createdFileTime"]=process.StartTime.ToFileTimeUtc(),["adoption"]=keeper!=null,["createdUtc"]=DateTime.UtcNow});
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<15000)
        {
            if(process.HasExited)throw new ToolFailure("owner_start_failed","Layer owner exited before readiness. Inspect layer "+id+"; application state was retained where possible.","unknown");
            try{if(Files.Read(Descriptor(id))["pid"]!.GetValue<int>()==process.Id)return;}catch(IOException){}catch(System.Text.Json.JsonException){}
            await Task.Delay(10);
        }
        throw new ToolFailure("owner_start_unknown","Owner readiness unconfirmed. Inspect layer "+id+" before creating another.","unknown");
    }
    static async Task<JsonObject> DeliverInput(OwnerConnection connection,string session,JsonObject fields)
    {
        // Posting an entire WM_CHAR batch around Enter/Tab can reorder a
        // framework's translated character after the remainder of the batch
        // (observed in Word). Use the established private queue key pacing at
        // control boundaries; ordinary character runs retain the fast path.
        var segments=new List<JsonObject>();string kind=Text(fields,"kind");
        void Characters(string value){if(value.Length>0)segments.Add(new(){["kind"]="text",["text"]=value});}
        void Control(int key)=>segments.Add(new(){["kind"]="chord",["keys"]=new JsonArray(key)});
        if(kind=="text")
        {
            string value=Text(fields,"text");int start=0;
            for(int index=0;index<value.Length;index++)
                if(value[index] is '\r' or '\n' or '\t')
                {
                    Characters(value[start..index]);Control(value[index]=='\t'?9:13);
                    if(value[index]=='\r'&&index+1<value.Length&&value[index+1]=='\n')index++;
                    start=index+1;
                }
            Characters(value[start..]);
        }
        else if(kind=="key"&&fields["key"]!.GetValue<int>() is 8 or 9 or 13)Control(fields["key"]!.GetValue<int>());
        else segments.Add(fields.DeepClone().AsObject());
        if(segments.Count==0)throw new ToolFailure("invalid_arguments","Text must not be empty.");
        int completed=0;JsonObject? response=null;
        try
        {
            foreach(var segment in segments)
            {
                foreach(string key in new[]{"window","expectedRevision","expectedInputSequence"})segment[key]=fields[key]!.DeepClone();
                response=await connection.Call("input",session,segment);completed++;
                // Advance only from an acknowledged result. Never silently
                // refresh ownership or replay an uncertain segment.
                fields["expectedInputSequence"]=response["session"]!["inputSequence"]!.DeepClone();
            }
        }
        catch(ToolFailure error)when(completed>0&&error.Outcome=="not_started")
        {throw new ToolFailure("partial_input",error.Message+" Earlier segments were forwarded; inspect and reconcile before continuing.","unknown");}
        response!["result"]!["segmentsForwarded"]=completed;return response;
    }
    internal async Task<JsonObject> Execute(string name,JsonObject arguments)
    {
        if(name=="viavw_info")return new(){["name"]="VIAVW",["version"]=Version,["interface"]="standalone MCP stdio and command-line",["viewer"]="optional; shown on demand; close hides and preserves app",["lifetime"]="AI disconnect preserves applications",["store"]=Store,["scope"]="Windows x64; tested applications only; no universal GPU or device emulation claim",["handoff"]="Human ownership blocks automation. Human returns control using viewer. Observe again before continuing.",["uncertainInputPolicy"]="Input intent is persisted before delivery. Lost/unknown input pauses the tool until fresh observation and explicit reconciliation.",["defaultCapture"]="auto: print-window for tested Word/Excel executables; window-dc otherwise; explicit choice is respected"};
        if(name=="viavw_list")
        {
            var layers=new JsonArray();
            foreach(string directory in Directory.EnumerateDirectories(Store))
            {
                string id=Path.GetFileName(directory);if(!LayerId().IsMatch(id))continue;
                try
                {
                    var record=Record(id);var item=record.DeepClone().AsObject();
                    item["ownerAlive"]=File.Exists(Descriptor(id))&&Files.Alive(Files.Read(Descriptor(id)));
                    layers.Add(item);
                }
                catch(Exception error)when(error is IOException or System.Text.Json.JsonException){layers.Add(new JsonObject{["layer"]=id,["state"]="management_attention",["message"]=error.Message});}
            }
            return new(){["layers"]=layers};
        }
        if(name=="viavw_create")
        {
            string executable=Text(arguments,"executable");
            if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ToolFailure("invalid_executable","Use an existing absolute Windows executable path.");
            string capture=arguments["captureMode"]?.GetValue<string>()??"auto";
            if(capture=="auto")capture=Path.GetFileName(executable).ToUpperInvariant() is "WINWORD.EXE" or "EXCEL.EXE"?"print-window":"window-dc";
            string id=Guid.NewGuid().ToString("N"),directory=DirectoryFor(id);Directory.CreateDirectory(directory);
            using var gate=await Lock(directory);
            var record=new JsonObject{["layer"]=id,["executable"]=executable,["state"]="starting",["captureMode"]=capture,["createdUtc"]=DateTime.UtcNow};
            Files.Write(Path.Combine(directory,"tool-record.json"),record);
            try
            {
                await Start(id);
                await using var client=await Connect(id);
                JsonObject result;
                try{result=await client.Call("create",fields:new(){["executable"]=executable,["arguments"]=arguments["arguments"]?.DeepClone()??new JsonArray(),["captureMode"]=capture});}
                catch(ToolFailure error)when(error.Outcome=="not_started"){try{await client.Call("shutdown");}catch(ToolFailure){}throw;}
                record["session"]=result["id"]!.DeepClone();record["state"]="created";
                record["keeper"]=result["observation"]!["session"]!["keeperDescriptor"]!.DeepClone();
                Files.Write(Path.Combine(directory,"tool-record.json"),record);
                return new(){["layer"]=id,["data"]=result,["captureMode"]=capture,["note"]="Application is independent of this tool connection. Observe before input. Viewer is hidden until requested."};
            }
            catch(Exception error){throw new ToolFailure("create_needs_inspection",error.Message+" Inspect retained layer "+id+" before another launch. Owner output is under "+directory+". Creation is not replayed.","unknown");}
        }
        string layer=Text(arguments,"layer");string folder=DirectoryFor(layer);
        if(!Directory.Exists(folder))throw new ToolFailure("unknown_layer","Unknown layer ID.");
        using var control=await Lock(folder);
        var saved=Record(layer);string? session=saved["session"]?.GetValue<string>();
        if(name=="viavw_recover")
        {
            string? keeper=saved["keeper"]?.GetValue<string>();
            if(keeper==null)
            {
                // A tool can die after app launch but before its catalog write.
                // Recover only one unambiguous existing startup record; never
                // launch another application to compensate for a lost reply.
                string ownerFolder=Path.Combine(folder,"owner");
                var seeds=Directory.Exists(ownerFolder)?Directory.EnumerateDirectories(ownerFolder,"session-*").Where(d=>File.Exists(Path.Combine(d,"keeper-current.json"))&&File.Exists(Path.Combine(d,"create.json"))).ToArray():[];
                if(seeds.Length==1)
                {
                    keeper=Path.Combine(seeds[0],"keeper-current.json");session=Files.Read(Path.Combine(seeds[0],"create.json"))["sessionId"]!.GetValue<string>();
                    saved["keeper"]=keeper;saved["session"]=session;Files.Write(Path.Combine(folder,"tool-record.json"),saved);
                }
            }
            bool alive=Files.Alive(Files.Read(Descriptor(layer)));
            if(alive&&Flag(arguments,"replaceOwner"))
            {
                await using(var old=await Connect(layer)){var replaced=await old.Call("replace-owner",session);keeper=Text(replaced,"keeperDescriptor");}
                var deadline=Stopwatch.StartNew();while(Files.Alive(Files.Read(Descriptor(layer)))){if(deadline.ElapsedMilliseconds>8000)throw new ToolFailure("owner_exit_unknown","Existing owner has not exited; no competing owner started.","unknown");await Task.Delay(10);}alive=false;
            }
            if(!alive)
            {
                if(keeper==null)throw new ToolFailure("keeper_unknown","No recorded keeper. Inspect layer files; no duplicate app was launched.","unknown");
                await Start(layer,keeper);
            }
            await using var recovery=await Connect(layer);
            session??=await FindSession(recovery);
            saved["session"]=session;
            var state=(await recovery.Call("status",session))["session"]!.AsObject();
            saved["keeper"]=state["keeperDescriptor"]?.DeepClone();Files.Write(Path.Combine(folder,"tool-record.json"),saved);
            if(!Flag(state,"keeperReady")){await recovery.Call("repair-keeper",session,new(){["expectedRevision"]=state["revision"]!.DeepClone()});state=(await recovery.Call("status",session))["session"]!.AsObject();}
            if(Flag(state,"workerFaulted"))await recovery.Call("recover",session,new(){["expectedRevision"]=state["revision"]!.DeepClone()});
            return new(){["layer"]=layer,["data"]=await recovery.Call("status",session),["note"]="No input replayed. Observe and verify application context, then explicitly reconcile if paused. Human ownership is preserved."};
        }
        await using var connection=await Connect(layer);
        if(session==null)
        {
            if(name=="viavw_close"&&(await connection.Call("list"))["items"]!.AsArray().Count==0)
            {
                await connection.Call("shutdown");saved["state"]="closed";Files.Write(Path.Combine(folder,"tool-record.json"),saved);
                return new(){["layer"]=layer,["data"]=new JsonObject{["result"]=new JsonObject{["closed"]=true,["note"]="Empty application owner stopped; no application relaunched or discarded."}}};
            }
            session=await FindSession(connection);saved["session"]=session;
            var recoveredState=(await connection.Call("status",session))["session"]!;
            saved["keeper"]=recoveredState["keeperDescriptor"]?.DeepClone();Files.Write(Path.Combine(folder,"tool-record.json"),saved);
        }
        JsonObject response;
        switch(name)
        {
            case "viavw_status":response=await connection.Call("status",session);break;
            case "viavw_observe":
                response=await connection.Call("observe",session,new(){["capture"]=Flag(arguments,"capture",true),["accessibility"]=Flag(arguments,"accessibility")});
                var state=response["session"]!;var result=response["result"]!;
                response["observation"]=Sign(new(){["layer"]=layer,["epoch"]=connection.Epoch,["revision"]=state["revision"]!.DeepClone(),["sequence"]=state["inputSequence"]!.DeepClone(),["root"]=state["root"]!.DeepClone(),["inputFence"]=saved["inputFence"]?.DeepClone()??JsonValue.Create(""),["recoveryToken"]=result["recoveryToken"]?.DeepClone()});
                if(result["accessibilityPath"] is JsonValue accessPath)
                {
                    string file=Artifact(layer,accessPath.GetValue<string>());
                    if(new FileInfo(file).Length<=262144)response["accessibility"]=JsonNode.Parse(File.ReadAllText(file));
                    else response["accessibilityNote"]="Accessibility exceeds inline size; use asynchronous bounded inspection.";
                }
                break;
            case "viavw_input":
            case "viavw_surface":
            case "viavw_reconcile":
                var proof=Proof(Text(arguments,"observation"),layer,connection.Epoch);
                if((proof["inputFence"]?.GetValue<string>()??"")!=(saved["inputFence"]?.GetValue<string>()??""))throw new ToolFailure("stale_observation","Input intent changed after this observation. Observe the actual app before another decision.");
                var fields=new JsonObject{["expectedRevision"]=proof["revision"]!.DeepClone()};
                if(name=="viavw_input")
                {
                    if(Flag(saved,"uncertainInput"))throw new ToolFailure("input_uncertain","An earlier input outcome is unknown. Observe and explicitly reconcile; no input was sent.");
                    fields["expectedInputSequence"]=proof["sequence"]!.DeepClone();fields["window"]=arguments["window"]?.DeepClone()??proof["root"]!.DeepClone();
                    foreach(string key in new[]{"kind","text","key","keys","x","y","deliveryMode"})if(arguments[key]!=null)fields[key]=arguments[key]!.DeepClone();
                    // Persist intent before dispatch. Even a tool-process crash
                    // between delivery and its reply cannot silently permit a
                    // second mutation after the next AI reconnects.
                    saved["uncertainInput"]=true;saved["inputFence"]=Guid.NewGuid().ToString("N");
                    Files.Write(Path.Combine(folder,"tool-record.json"),saved);
                    try
                    {
                        response=await DeliverInput(connection,session,fields);
                        saved["uncertainInput"]=false;Files.Write(Path.Combine(folder,"tool-record.json"),saved);
                    }
                    catch(ToolFailure error)when(error.Outcome=="not_started")
                    {saved["uncertainInput"]=false;Files.Write(Path.Combine(folder,"tool-record.json"),saved);throw;}
                }
                else if(name=="viavw_surface")
                {
                    fields["window"]=arguments["window"]!.DeepClone();response=await connection.Call("surface",session,fields);
                }
                else
                {
                    if(!Flag(arguments,"confirmContext")||string.IsNullOrWhiteSpace(Text(arguments,"reason")))throw new ToolFailure("context_required","Verify actual application context, then provide confirmContext and a reason. No input was replayed.");
                    var current=(await connection.Call("status",session))["session"]!.AsObject();
                    if(current["owner"]?.GetValue<string>()!="Automation")throw new ToolFailure("human_owns_input","Reconciliation cannot reclaim Human ownership.");
                    if(!JsonNode.DeepEquals(current["revision"],proof["revision"])||!JsonNode.DeepEquals(current["inputSequence"],proof["sequence"])||!JsonNode.DeepEquals(current["root"],proof["root"]))throw new ToolFailure("stale_observation","Application context changed. Observe again before reconciling.");
                    if(Flag(current,"workerFaulted")||!Flag(current,"keeperReady"))throw new ToolFailure("layer_suspended","Repair the worker/keeper before reconciliation.");
                    if(Flag(current,"recoveryPending"))
                    {
                        fields["observationToken"]=proof["recoveryToken"]?.DeepClone();fields["continue"]=true;
                        response=await connection.Call("reconcile",session,fields);
                    }
                    else
                    {
                        if(!Flag(saved,"uncertainInput"))throw new ToolFailure("invalid_state","There is no uncertain input or recovered layer to reconcile.");
                        response=new(){["session"]=current.DeepClone(),["result"]=new JsonObject{["resumed"]=true,["note"]="Caller accepted the observed outcome. No input replayed."}};
                    }
                    saved["uncertainInput"]=false;saved["lastReconciliationReason"]=Text(arguments,"reason");Files.Write(Path.Combine(folder,"tool-record.json"),saved);
                }
                break;
            case "viavw_viewer":response=await connection.Call("viewer",session,new(){["visible"]=Flag(arguments,"visible",true)});break;
            case "viavw_handoff":
                var handoffState=(await connection.Call("status",session))["session"]!;
                await connection.Call("attention",session,new(){["reason"]=Text(arguments,"reason")});
                await connection.Call("ownership",session,new(){["expectedRevision"]=handoffState["revision"]!.DeepClone(),["owner"]="Human"});
                response=await connection.Call("viewer",session,new(){["visible"]=true});break;
            case "viavw_close":
                var closeState=(await connection.Call("status",session))["session"]!;
                response=await connection.Call("close",session,new(){["expectedRevision"]=closeState["revision"]!.DeepClone(),["force"]=Flag(arguments,"force")});
                if(Flag(response["result"]!.AsObject(),"closed")){await connection.Call("shutdown");saved["state"]="closed";Files.Write(Path.Combine(folder,"tool-record.json"),saved);}break;
            case "viavw_inspect":
                if(arguments["ticket"]!=null)response=await connection.Call("inspect-poll",session,new(){["ticket"]=arguments["ticket"]!.DeepClone()});
                else response=await connection.Call("inspect-begin",session,new(){["automationId"]=arguments["automationId"]?.DeepClone()});break;
            default:throw new ToolFailure("unknown_tool","Unknown tool: "+name);
        }
        if(response["session"] is JsonObject reported)
        {
            reported["inputUncertain"]=Flag(saved,"uncertainInput");
            if(Flag(saved,"uncertainInput")&&reported["state"]?.GetValue<string>()=="ready")reported["state"]="paused_input_uncertain";
        }
        return new(){["layer"]=layer,["data"]=response};
    }
    static async Task<string> FindSession(OwnerConnection client)
    {
        var list=(await client.Call("list"))["items"]!.AsArray();
        if(list.Count!=1)throw new ToolFailure("session_unknown","Owner does not have exactly one recorded application. Inspect the retained layer; no duplicate app started.","unknown");
        return list[0]!["id"]!.GetValue<string>();
    }
    internal string Artifact(string id,string path)
    {
        string full=Path.GetFullPath(path),prefix=Path.Combine(DirectoryFor(id),"owner","observations")+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(full)+Path.DirectorySeparatorChar!=prefix||new FileInfo(full).Length>16*1024*1024)throw new ToolFailure("invalid_artifact","Only this layer's bounded observations can be returned.");
        return full;
    }
}

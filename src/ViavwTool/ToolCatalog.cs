using System.Text.Json.Nodes;

namespace ViavwTool;

internal static class ToolCatalog
{
    static JsonObject String(string description,int maximum=4096)=>new(){["type"]="string",["description"]=description,["maxLength"]=maximum};
    static JsonObject Boolean(bool value)=>new(){["type"]="boolean",["default"]=value};
    static JsonObject Integer(string description,long minimum=0,long maximum=long.MaxValue)=>new(){["type"]="integer",["description"]=description,["minimum"]=minimum,["maximum"]=maximum};
    static JsonObject Definition(string name,string description,JsonObject fields,string[] required,bool read=false,bool destructive=false)
        =>new(){["name"]=name,["description"]=description,["inputSchema"]=new JsonObject{["type"]="object",["properties"]=fields,["required"]=new JsonArray(required.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray()),["additionalProperties"]=false},["annotations"]=new JsonObject{["readOnlyHint"]=read,["destructiveHint"]=destructive,["idempotentHint"]=read,["openWorldHint"]=true}};
    static JsonObject Layer()=>new(){["layer"]=String("Opaque layer ID returned by create/list.",32)};
    internal static JsonArray All()
    {
        var tools=new JsonArray();
        tools.Add(Definition("viavw_info","Describe the standalone Windows application tool and its boundaries.",new(),[],true));
        tools.Add(Definition("viavw_list","List retained layers across tool/AI disconnects. Closed records remain visible; ownerAlive alone is not app readiness.",new(),[],true));
        tools.Add(Definition("viavw_create","Launch an installed Windows application on a private desktop. Choose safe independent-instance arguments. Does not show a viewer or take desktop focus. Application keeps running after this tool exits.",new(){["executable"]=String("Absolute installed .exe path."),["arguments"]=new JsonObject{["type"]="array",["items"]=String("Launch argument."),["maxItems"]=64},["captureMode"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("auto","window-dc","print-window"),["default"]="auto",["description"]="Auto selects print-window for tested Word/Excel executables and window-dc otherwise. Explicit settings are respected."}},["executable"]));
        tools.Add(Definition("viavw_status","Read current owner, app PID, readiness, recovery, input sequence and viewer state.",Layer(),["layer"],true));
        var observe=Layer();observe["capture"]=Boolean(true);observe["accessibility"]=Boolean(false);
        tools.Add(Definition("viavw_observe","Observe actual owned windows; optionally return a PNG image and accessibility data. Returns a signed observation token required for input. Capture is on demand; successful input is not proof of application completion.",observe,["layer"],true));
        var input=Layer();input["observation"]=String("Token from latest observe. Stale owner/revision/input sequence is rejected.");input["window"]=Integer("Owned target HWND. Defaults to observed root; child coordinates differ.",1);
        input["kind"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("text","key","chord","click")};input["text"]=String("Text; CR/LF, Tab use ordinary keys. No literal-paste guarantee.");input["key"]=Integer("Windows virtual-key number: Enter 13, Backspace 8.",1,255);input["keys"]=new JsonObject{["type"]="array",["items"]=Integer("Virtual key.",1,255),["minItems"]=1,["maxItems"]=8};input["x"]=Integer("Target client pixel X.",0,32767);input["y"]=Integer("Target client pixel Y.",0,32767);
        input["deliveryMode"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("synchronous","posted"),["description"]="Click only: synchronous by default; posted is needed for some native modal mouse handlers such as 7-Zip's list view. Neither proves action completion."};
        tools.Add(Definition("viavw_input","Send one serialized independent text/key/chord/left-click action. Requires Automation ownership and fresh observation. Never replay an unknown outcome. Observe actual results afterward. Physical input is untouched.",input,["layer","observation","kind"],destructive:true));
        var viewer=Layer();viewer["visible"]=Boolean(true);
        tools.Add(Definition("viavw_viewer","Show/hide the optional viewer without changing input ownership. Closing its window hides it and preserves the application.",viewer,["layer"]));
        var handoff=Layer();handoff["reason"]=String("Why the person needs to inspect or edit this app.",1024);
        tools.Add(Definition("viavw_handoff","Give Human control and open the optional viewer. Automation input is rejected until the person chooses Return to automation in the viewer. AI cannot reclaim Human ownership through this interface.",handoff,["layer","reason"]));
        var close=Layer();close["force"]=Boolean(false);
        tools.Add(Definition("viavw_close","Request graceful application close. Unsaved save prompts keep app alive and open Human viewer. force=true explicitly discards the entire owned layer and unsaved work.",close,["layer"],destructive:true));
        var recover=Layer();recover["replaceOwner"]=Boolean(false);
        tools.Add(Definition("viavw_recover","Repair failed workers/keeper or adopt a dead owner while preserving the app. replaceOwner=true deliberately replaces a live controller. Pauses uncertain workflows for fresh observation and explicit reconciliation; never replays input or reclaims Human control.",recover,["layer"]));
        var reconcile=Layer();reconcile["observation"]=String("Fresh post-recovery observation token.");reconcile["confirmContext"]=Boolean(false);reconcile["reason"]=String("Describe the actual application/document context you verified.",1024);
        tools.Add(Definition("viavw_reconcile","Explicitly resume a layer paused after unknown input or controller recovery, after checking its actual observed context. Requires confirmContext=true and a reason; cannot take Human control. This confirms a decision, not automatic proof of document identity.",reconcile,["layer","observation","confirmContext","reason"]));
        var surface=Layer();surface["observation"]=String("Fresh observation token.");surface["window"]=Integer("Owned visible top-level HWND from observed windows.",1);
        tools.Add(Definition("viavw_surface","Select an owned top-level dialog/window as the presentation surface. Requires Automation ownership. Observe again before input.",surface,["layer","observation","window"]));
        var inspect=Layer();inspect["ticket"]=Integer("Poll an earlier inspection ticket.",1);inspect["automationId"]=String("Optional control automation ID for a new passive inspection.",256);
        tools.Add(Definition("viavw_inspect","Begin bounded asynchronous passive accessibility inspection, or poll a ticket. Stale results do not authorize input; never treats inspection completion as application completion.",inspect,["layer"],true));
        return tools;
    }
    internal static void Validate(string name,JsonObject arguments)
    {
        var definition=All().FirstOrDefault(n=>n!["name"]!.GetValue<string>()==name)??throw new ToolFailure("unknown_tool","Unknown tool: "+name);
        ValidateNode(arguments,definition["inputSchema"]!.AsObject(),"arguments");
        if(name=="viavw_input")
        {
            string kind=arguments["kind"]!.GetValue<string>();
            string[] required=kind switch{"text"=>["text"],"key"=>["key"],"chord"=>["keys"],"click"=>["x","y"],_=>[]};
            foreach(string field in required)if(arguments[field]==null)throw new ToolFailure("invalid_arguments",kind+" requires "+field);
        }
    }
    static void ValidateNode(JsonNode? value,JsonObject schema,string path)
    {
        if(value==null)throw new ToolFailure("invalid_arguments",path+" cannot be null.");
        string type=schema["type"]!.GetValue<string>();bool valid=type switch
        {
            "object"=>value is JsonObject,"array"=>value is JsonArray,
            "string"=>value is JsonValue v&&v.TryGetValue<string>(out _),
            "boolean"=>value is JsonValue b&&b.TryGetValue<bool>(out _),
            "integer"=>value is JsonValue i&&i.TryGetValue<long>(out _),_=>false
        };
        if(!valid)throw new ToolFailure("invalid_arguments",path+" must be "+type);
        if(schema["enum"] is JsonArray choices&&!choices.Any(c=>JsonNode.DeepEquals(c,value)))throw new ToolFailure("invalid_arguments",path+" has an unsupported value.");
        if(value is JsonObject obj)
        {
            foreach(var required in schema["required"]!.AsArray())if(!obj.ContainsKey(required!.GetValue<string>()))throw new ToolFailure("invalid_arguments",path+" requires "+required);
            var properties=schema["properties"]!.AsObject();
            foreach(var field in obj){if(properties[field.Key] is not JsonObject fieldSchema)throw new ToolFailure("invalid_arguments","Unknown argument: "+field.Key);ValidateNode(field.Value,fieldSchema,path+"."+field.Key);}
        }
        else if(value is JsonArray array)
        {
            if(array.Count<(schema["minItems"]?.GetValue<int>()??0)||array.Count>(schema["maxItems"]?.GetValue<int>()??int.MaxValue))throw new ToolFailure("invalid_arguments",path+" has an invalid number of items.");
            foreach(var item in array)ValidateNode(item,schema["items"]!.AsObject(),path+"[]");
        }
        else if(type=="string"&&value.GetValue<string>().Length>(schema["maxLength"]?.GetValue<int>()??int.MaxValue))throw new ToolFailure("invalid_arguments",path+" is too long.");
        else if(type=="integer"&&(value.GetValue<long>()<(schema["minimum"]?.GetValue<long>()??long.MinValue)||value.GetValue<long>()>(schema["maximum"]?.GetValue<long>()??long.MaxValue)))throw new ToolFailure("invalid_arguments",path+" is out of range.");
    }
}

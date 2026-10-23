using System.Diagnostics;
using System.Text.Json;
namespace LayerHost;
internal sealed partial class DemoForm
{
    // Explicit inspection is allowed to wait asynchronously for the requested
    // sample. Never switch targets while still collecting its pending ticket.
    // Ordinary input and automatic pixel refresh do not call this method.
    async Task<JsonElement> ReadDetailedInspectionAsync(long window,string? automationId=null,bool menus=false)
    {
        var clock=Stopwatch.StartNew();long? ticket=null;JsonElement? last=null;
        while(clock.ElapsedMilliseconds<2500)
        {
            if(ticket==null)
            {
                try{ticket=await Task.Run(()=>menus?layer!.BeginAccessibleMenuInspection(window):layer!.BeginAccessibleInspection(window,automationId,allVisibleMatches:automationId=="WpfTextView"));}
                catch(InspectionBusyException){await Task.Delay(20);continue;}
            }
            var poll=await Task.Run(()=>layer!.PollAccessibleInspection(ticket.Value));last=poll;
            if(poll.GetProperty("status").GetString() is not ("pending" or "partial"))
            {
                var result=poll.GetProperty("result");
                var fields=result.ValueKind==JsonValueKind.Object?JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(result.GetRawText())!:new Dictionary<string,JsonElement>{["nodes"]=JsonSerializer.SerializeToElement(Array.Empty<object>()),["truncated"]=JsonSerializer.SerializeToElement(true)};
                fields["inspection"]=poll;return JsonSerializer.SerializeToElement(fields);
            }
            await Task.Delay(20);
        }
        return JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),truncated=true,inspectionStatus="pending",inspection=last,window,note="Explicit inspection collection budget elapsed; no result or input acknowledgement is invented."});
    }
}

using System.Text.Json;
namespace LayerHost;
internal sealed partial class DemoForm
{
    async Task MaybePrepareInitialPointerAsync(CaptureFrame frame,WindowInfo source)
    {
        if(initialPointerTitleFragment==null||initialPointerAutomationId==null||initialPointerPreparationAttempted||layer==null||!nativePopupPointer)return;
        if(!layer.CanPrepareInitialPointer){initialPointerPreparationAttempted=true;initialPointerPreparation=new{skipped=true,reason="Input was already attempted"};return;}
        if(root!=nativeMainRoot||root!=nativeSurfaceRoot||!source.Enabled||!source.Text.Contains(initialPointerTitleFragment,StringComparison.Ordinal))return;
        if(initialPointerTicket is {} ticket)
        {
            var status=layer.PollAccessibleInspection(ticket);string? state=status.GetProperty("status").GetString();
            if(state is "pending" or "partial")return;
            initialPointerTicket=null;initialPointerRetryDue=Environment.TickCount64+250;
            if(state=="complete"&&initialPointerReadRoot==root&&status.GetProperty("result").TryGetProperty("nodes",out var nodes))
            {
                foreach(var node in nodes.EnumerateArray())
                {
                    if(node.GetProperty("pid").GetInt32()!=source.ProcessId||node.GetProperty("id").GetString()!=initialPointerAutomationId||node.GetProperty("type").GetString()!="ControlType.Edit"||!node.GetProperty("enabled").GetBoolean()||node.GetProperty("offscreen").GetBoolean()||node.GetProperty("text").ValueKind!=JsonValueKind.String)continue;
                    if(new[]{"x","y","width","height"}.Any(key=>node.GetProperty(key).ValueKind!=JsonValueKind.Number))continue;
                    double x=node.GetProperty("x").GetDouble(),y=node.GetProperty("y").GetDouble(),width=node.GetProperty("width").GetDouble(),height=node.GetProperty("height").GetDouble();
                    if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)continue;
                    var origin=await Task.Run(()=>layer.ClientOrigin(root));
                    double localX=x+width/2-origin.X,localY=y+height/2-origin.Y;
                    if(localX<0||localY<0||localX>=frame.Width||localY>=frame.Height)continue;
                    initialPointerPreparationAttempted=true;
                    try{var preparation=await Task.Run(()=>layer.PrepareInitialPointer(root,new Point((int)localX,(int)localY)));initialPointerPreparation=new{readinessMilliseconds=status.GetProperty("milliseconds").GetDouble(),preparation};}
                    catch(InvalidOperationException error){initialPointerPreparation=new{skipped=true,error=error.Message};}
                    return;
                }
            }
            if(initialPointerReadAttempts>=3){initialPointerPreparationAttempted=true;initialPointerPreparation=new{completed=false,reason="Editor readiness was not established; normal pointer lookup retained"};return;}
        }
        if(Environment.TickCount64<initialPointerRetryDue)return;
        try{initialPointerTicket=layer.BeginAccessibleInspection(root,initialPointerAutomationId);initialPointerReadRoot=root;initialPointerReadAttempts++;}
        catch(InspectionBusyException){initialPointerRetryDue=Environment.TickCount64+150;}
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Diagnostic-only, bounded in-memory observations. QPC timestamps measure local
// software stages, not monitor scanout or application-internal render completion.
internal sealed class ViewerTimingTrace(string directory)
{
    internal record Entry(string Stage,long Qpc,long Frame,long InputSequence,int? ChangedPixels,double? DurationMilliseconds);
    readonly Queue<Entry> entries=new();
    readonly object gate=new();
    int dropped;bool flushed;
    internal void Record(string stage,long frame,long inputSequence,int? changedPixels=null,double? durationMilliseconds=null)
    {
        lock(gate){if(entries.Count==8192){entries.Dequeue();dropped++;}
        entries.Enqueue(new(stage,Stopwatch.GetTimestamp(),frame,inputSequence,changedPixels,durationMilliseconds));}
    }
    internal async Task FlushIfRequestedAsync()
    {
        if(flushed||!File.Exists(Path.Combine(directory,"viewer-timing-flush.request")))return;
        flushed=true;
        Entry[] snapshot;lock(gate)snapshot=entries.ToArray();
        await SnapshotFiles.ReplaceTextAsync(Path.Combine(directory,"viewer-timing.json"),JsonSerializer.Serialize(new{frequency=Stopwatch.Frequency,dropped,entries=snapshot,note="Input forwarding is not target processing acknowledgement. Bitmap freshness bounds source rendering by sampled capture times. Paint end is a WinForms paint-handler return, not physical scanout."}));
    }
}

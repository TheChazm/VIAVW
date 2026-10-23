using System.Collections.Concurrent;

namespace LayerHost;

internal record PreviewPoll(bool Ready,long Ticket,SharedPreview? Frame=null);

// One optional rendering thread and one reusable map. Native input remains on
// the RPC thread. Only one capture can be outstanding; no frame queue grows.
internal sealed class PrivatePreviewThread : IDisposable
{
    readonly BlockingCollection<Action> calls=new(1);
    Thread? thread;
    Task<SharedPreview>? pending;
    long ticket;
    EventWaitHandle? ready;
    string channel="";
    readonly SharedPreviewWriter output=new();
    internal long Begin(string requestedChannel,Func<SharedPreviewWriter,SharedPreview> capture)
    {
        if(pending!=null)throw new InvalidOperationException("Consume the outstanding preview before requesting another.");
        SharedPreviewWriter.ValidateChannel(requestedChannel);
        if(channel.Length!=0&&channel!=requestedChannel)throw new InvalidOperationException("A preview worker retains one authenticated channel.");
        if(ready==null){ready=EventWaitHandle.OpenExisting(requestedChannel+"_ready");channel=requestedChannel;}
        if(thread==null)
        {
            thread=new Thread(()=>
            {
                try{foreach(var call in calls.GetConsumingEnumerable())call();}
                finally{output.Dispose();ready?.Dispose();}
            }){IsBackground=true,Name="VIAVW private preview"};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();
        }
        var result=new TaskCompletionSource<SharedPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending=result.Task;ticket=checked(ticket+1);
        calls.Add(()=>
        {
            try{WindowCaptureResearch.PrivateOnly();result.SetResult(capture(output));}
            catch(Exception error){result.SetException(error);}
            finally{ready!.Set();}
        });
        return ticket;
    }
    internal PreviewPoll Poll(long requested)
    {
        if(pending==null||requested!=ticket)throw new InvalidOperationException("Preview ticket is no longer current.");
        if(!pending.IsCompleted)return new(false,ticket);
        var completed=pending;pending=null;
        return new(true,ticket,completed.GetAwaiter().GetResult());
    }
    public void Dispose()
    {
        calls.CompleteAdding();
        // The host's capture deadline closes the owned job on a stall. Avoid an
        // unbounded shutdown wait and let the rendering thread release its map.
        if(thread==null){output.Dispose();ready?.Dispose();calls.Dispose();}
        else if(thread.Join(1000))calls.Dispose();
    }
}

namespace LayerHost;

// One in-flight snapshot and one pending snapshot. Automatic reports coalesce;
// a required inspection replaces the pending automatic report and cannot be
// superseded until its write completes. This queue never contains app input.
internal sealed class LatestSnapshotPublisher(Func<long,string,Task> write) : IDisposable
{
    sealed record Work(long Version,string Text,TaskCompletionSource? Completion);
    readonly object gate=new();
    Work? pending,active;
    bool running,disposed,requiredActive;
    Exception? error;
    internal bool Queue(long version,string text)
    {
        Validate(text);
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(requiredActive)return false;
            pending=new(version,text,null);Start();return true;
        }
    }
    internal Task WriteRequiredAsync(long version,string text)
    {
        Validate(text);
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(requiredActive)throw new InvalidOperationException("One explicit inspection may publish at a time.");
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            requiredActive=true;pending=new(version,text,completion);Start();return completion.Task;
        }
    }
    static void Validate(string text){if(text.Length>524288)throw new ArgumentException("Bound status snapshots to 512 Ki characters.");}
    void Start(){if(running)return;running=true;_ = Task.Run(PumpAsync);}
    async Task PumpAsync()
    {
        while(true)
        {
            Work work;
            lock(gate)
            {
                if(disposed||pending==null){running=false;return;}
                work=pending;pending=null;active=work;
            }
            Exception? failure=null;
            try{await write(work.Version,work.Text).ConfigureAwait(false);}catch(Exception e){failure=e;}
            lock(gate)
            {
                active=null;
                if(failure!=null)error=failure;
                if(work.Completion!=null)
                {
                    requiredActive=false;
                    if(disposed)work.Completion.TrySetCanceled();
                    else if(failure!=null)work.Completion.TrySetException(failure);
                    else work.Completion.TrySetResult();
                }
            }
        }
    }
    internal Exception? TakeError(){lock(gate){var result=error;error=null;return result;}}
    public void Dispose()
    {
        lock(gate)
        {
            disposed=true;pending?.Completion?.TrySetCanceled();active?.Completion?.TrySetCanceled();pending=null;
        }
    }
}

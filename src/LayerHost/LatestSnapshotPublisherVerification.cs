using System.Text.Json;

namespace LayerHost;

internal static class LatestSnapshotPublisherVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var checks=new Dictionary<string,bool>();string? failure=null;
        void Check(string name,bool condition){checks[name]=condition;if(!condition)throw new InvalidOperationException(name);}
        async Task Verify()
        {
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var last=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var versions=new List<long>();int active=0,maximum=0;
            using(var publisher=new LatestSnapshotPublisher(async(version,text)=>
            {
                int count=Interlocked.Increment(ref active);maximum=Math.Max(maximum,count);
                try{versions.Add(version);if(version==1){started.SetResult();await release.Task;}if(version==10000)last.SetResult();}
                finally{Interlocked.Decrement(ref active);}
            }))
            {
                publisher.Queue(1,"first");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for(int i=2;i<=10000;i++)publisher.Queue(i,"latest");
                Check("slow writer does not block 9999 automatic submissions",!last.Task.IsCompleted);
                release.SetResult();await last.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await publisher.WriteRequiredAsync(10001,"barrier").WaitAsync(TimeSpan.FromSeconds(5));
                Check("automatic backlog coalesces to newest snapshot",versions.SequenceEqual([1L,10000L,10001L]));
                Check("only one write executes at once",maximum==1);
            }
            started=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);versions=[];
            using(var publisher=new LatestSnapshotPublisher(async(version,text)=>{versions.Add(version);if(version==1){started.SetResult();await release.Task;}}))
            {
                publisher.Queue(1,"blocked");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));publisher.Queue(2,"obsolete automatic");
                Task required=publisher.WriteRequiredAsync(3,"explicit");
                Check("explicit report waits behind the active write",!required.IsCompleted);
                Check("automatic report cannot supersede pending inspection",!publisher.Queue(4,"must not replace explicit"));
                bool duplicate=false;try{_ = publisher.WriteRequiredAsync(5,"duplicate");}catch(InvalidOperationException){duplicate=true;}Check("overlapping explicit inspections rejected",duplicate);
                release.SetResult();await required.WaitAsync(TimeSpan.FromSeconds(5));Check("explicit report replaces stale pending automatic report",versions.SequenceEqual([1L,3L]));
            }
            int attempts=0;
            using(var publisher=new LatestSnapshotPublisher((version,text)=>++attempts==1?Task.FromException(new IOException("Disposable injected write failure")):Task.CompletedTask))
            {
                bool failed=false;try{await publisher.WriteRequiredAsync(1,"failure");}catch(IOException){failed=true;}
                Check("required write failure is observable",failed&&publisher.TakeError() is IOException);
                await publisher.WriteRequiredAsync(2,"recovery");Check("later publication recovers without replaying failed snapshot",attempts==2&&publisher.TakeError()==null);
                bool oversized=false;try{publisher.Queue(3,new string('x',524289));}catch(ArgumentException){oversized=true;}Check("snapshot storage size bounded",oversized);
            }
            started=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);last=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var closing=new LatestSnapshotPublisher(async(version,text)=>{started.SetResult();await release.Task;last.SetResult();});
            Task waiting=closing.WriteRequiredAsync(1,"closing");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));closing.Dispose();
            Check("dispose cancels inspection waiter without waiting for disk",waiting.IsCanceled);
            bool rejected=false;try{closing.Queue(2,"after close");}catch(ObjectDisposedException){rejected=true;}Check("no new snapshots accepted after dispose",rejected);
            release.SetResult();await last.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        try{Verify().GetAwaiter().GetResult();}catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);}
        File.WriteAllText(Path.Combine(output,"latest-publisher.json"),JsonSerializer.Serialize(new{passed=failure==null,checks,failure,note="Deliberately stalled and failing disposable writers; no application input is queued, retried or replayed by this component."},new JsonSerializerOptions{WriteIndented=true}));
        return failure==null?0:1;
    }
}

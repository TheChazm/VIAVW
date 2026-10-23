using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace LayerHost;
internal static class PreviewDeadlineVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);var checks=new Dictionary<string,bool>();var observations=new List<object>();string? failure=null;
        void Check(string name,bool passed){checks[name]=passed;if(!passed)throw new InvalidOperationException(name);}
        static long After(int milliseconds)=>Stopwatch.GetTimestamp()+(long)(milliseconds*Stopwatch.Frequency/1000.0);
        try
        {
            var pending=new ConcurrentQueue<Action>();using var work=new AutoResetEvent(false);int callbacks=0;long posted=0;
            using(var dispatcher=new PreviewDeadlineDispatcher(action=>{posted=Stopwatch.GetTimestamp();pending.Enqueue(action);work.Set();},()=>callbacks++))
            {
                Check("idle dispatcher posts nothing",!work.WaitOne(100)&&pending.IsEmpty);
                long begin=Stopwatch.GetTimestamp();dispatcher.Schedule(After(12));Check("one-shot deadline completes",work.WaitOne(2000));
                observations.Add(new{kind="12ms-deadline",actualMilliseconds=(posted-begin)*1000.0/Stopwatch.Frequency});
                Check("deadline not delivered prematurely",(posted-begin)*1000.0/Stopwatch.Frequency>=11.5);
                Check("one UI callback queued",pending.Count==1);while(pending.TryDequeue(out var callback))callback();Check("callback delivered once",callbacks==1);
                begin=Stopwatch.GetTimestamp();dispatcher.Schedule(After(120));dispatcher.Schedule(After(20));dispatcher.Schedule(After(90));Check("earliest coalesced deadline completes",work.WaitOne(2000));
                double elapsed=(posted-begin)*1000.0/Stopwatch.Frequency;observations.Add(new{kind="earliest-coalesced",actualMilliseconds=elapsed});Check("later requests do not postpone earliest",elapsed>=19.5&&elapsed<115);
                while(pending.TryDequeue(out var callback))callback();Check("coalesced callback delivered once",callbacks==2);Check("no leftover timer callback",!work.WaitOne(140));
                dispatcher.Schedule(After(1));Check("blocked UI has a pending callback",work.WaitOne(2000));
                for(int n=0;n<10000;n++)dispatcher.Schedule(After(1+n%4));Thread.Sleep(30);
                Check("blocked UI callback queue bounded at one",pending.Count==1);while(pending.TryDequeue(out var callback))callback();Check("bounded callback executes once",callbacks==3);work.WaitOne(1);
                Check("dispatcher native operations succeeded",dispatcher.Error==null);
                dispatcher.Schedule(After(60000));var stop=Stopwatch.StartNew();dispatcher.Dispose();Check("cancel pending minute-long wait promptly",stop.ElapsedMilliseconds<1000);
                dispatcher.Schedule(After(0));Check("disposed dispatcher accepts no new work",!work.WaitOne(40));
            }
            callbacks=0;using(var dispatcher=new PreviewDeadlineDispatcher(action=>{pending.Enqueue(action);work.Set();},()=>callbacks++))
            {dispatcher.Schedule(After(1));Check("dispose-race callback posted",work.WaitOne(2000));dispatcher.Dispose();while(pending.TryDequeue(out var callback))callback();Check("queued callback cannot run after disposal",callbacks==0);}
            for(int n=0;n<8;n++){using var warm=new PreviewDeadlineDispatcher(_=>{},()=>{});}
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var process=Process.GetCurrentProcess();process.Refresh();int before=process.HandleCount;
            for(int n=0;n<64;n++){using var repeat=new PreviewDeadlineDispatcher(_=>{},()=>{});repeat.Schedule(After(60000));}
            process.Refresh();int beforeCollection=process.HandleCount;
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();process.Refresh();int after=process.HandleCount;
            observations.Add(new{kind="64-create-arm-cancel-dispose",handlesBefore=before,handlesBeforeCollection=beforeCollection,handlesAfterCollection=after,note="Diagnostic collection only: separate CLR Thread object lifetime from owned event/timer disposal. Production never forces collection."});Check("repeated cancellation releases handles after managed thread reclamation",after<=before+4);
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(error);}
        bool passed=failure==null&&checks.Values.All(x=>x);File.WriteAllText(Path.Combine(output,"deadline-verification.json"),JsonSerializer.Serialize(new{passed,failure,checks,observations,note="Disposable scheduler primitives; callback queue, earliest deadline, no idle posts, cancellation and disposal races. Application compatibility is tested separately."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace LayerHost;

// One cancellable, one-shot deadline. No polling while idle, periodic timer,
// spin waiting, priority change or process/system timer-resolution request.
internal sealed class PreviewDeadlineDispatcher : IDisposable
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint CreateWaitableTimerExW(nint attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint callback,nint argument,bool resume);
    [DllImport("kernel32.dll")]static extern bool CancelWaitableTimer(nint timer);
    sealed class TimerHandle : WaitHandle
    {
        internal TimerHandle(){nint handle=CreateWaitableTimerExW(0,null,2,0x100002);Native.Check(handle!=0,"Create preview deadline timer");SafeWaitHandle=new SafeWaitHandle(handle,true);}
    }
    readonly object gate=new();readonly AutoResetEvent wake=new(false);readonly TimerHandle timer=new();readonly Thread thread;
    readonly Action<Action> post;readonly Action callback;
    long due=long.MaxValue;bool stopped;int queued;string? error;
    internal string? Error=>Volatile.Read(ref error);
    internal PreviewDeadlineDispatcher(Action<Action> post,Action callback)
    {this.post=post;this.callback=callback;thread=new Thread(Run){IsBackground=true,Name="VIAVW preview deadline"};thread.Start();}
    internal void Schedule(long timestamp)
    {lock(gate){if(stopped)return;if(timestamp>=due)return;due=timestamp;wake.Set();}}
    void Run()
    {
        try
        {
            WaitHandle[] handles=[wake,timer];
            while(true)
            {
                long target;lock(gate){if(stopped)return;target=due;}
                if(target==long.MaxValue){wake.WaitOne();continue;}
                long remaining=target-Stopwatch.GetTimestamp();
                if(remaining>0)
                {
                    long relative=-Math.Max(1,(long)Math.Ceiling(remaining*10000000.0/Stopwatch.Frequency));
                    Native.Check(SetWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle(),ref relative,0,0,0,false),"Arm preview deadline");
                    WaitHandle.WaitAny(handles);continue;
                }
                lock(gate){if(due!=target)continue;due=long.MaxValue;}
                if(Interlocked.CompareExchange(ref queued,1,0)!=0)continue;
                try{post(()=>{Interlocked.Exchange(ref queued,0);lock(gate){if(stopped)return;}callback();});}
                catch(Exception exception)when(exception is InvalidOperationException or ObjectDisposedException){Interlocked.Exchange(ref queued,0);lock(gate){if(stopped)return;}throw;}
            }
        }
        catch(Exception exception){Volatile.Write(ref error,exception.ToString());}
    }
    public void Dispose()
    {
        lock(gate){if(stopped)return;stopped=true;wake.Set();}
        if(!thread.Join(2000))throw new TimeoutException("Preview deadline thread did not stop");
        CancelWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle());timer.Dispose();wake.Dispose();
    }
}

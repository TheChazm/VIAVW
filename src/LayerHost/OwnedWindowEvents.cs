using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LayerHost;

// Read-only OUTOFCONTEXT accessibility notifications, filtered by an exact
// validated application PID on its private desktop. No injected DLL or input.
internal sealed class OwnedWindowEvents : IDisposable
{
    delegate void WinEvent(nint hook,uint kind,nint window,int objectId,int childId,uint sourceThread,uint eventTime);
    [DllImport("user32.dll",SetLastError=true)]static extern nint SetWinEventHook(uint first,uint last,nint module,WinEvent callback,uint pid,uint thread,uint flags);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll",SetLastError=true)]static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32.dll",SetLastError=true)]static extern int GetMessageW(out Native.MSG message,nint window,uint first,uint last);
    [DllImport("user32.dll")]static extern bool TranslateMessage(ref Native.MSG message);
    [DllImport("user32.dll")]static extern nint DispatchMessageW(ref Native.MSG message);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostThreadMessageW(uint thread,uint message,nuint wparam,nint lparam);
    readonly object gate=new();
    readonly List<(long Qpc,uint Kind,long Window,int Object,int Child,uint EventTime)> events=[];
    readonly ManualResetEventSlim ready=new();
    readonly Thread thread;
    readonly uint pid;
    nint processIdentity;
    volatile bool stopping;
    volatile uint threadId;
    bool registered,removed;
    int dropped;
    string? error;
    internal OwnedWindowEvents(nint window)
    {
        WindowCaptureResearch.PrivateOnly();Native.GetWindowThreadProcessId(window,out pid);
        if(pid==0||pid==Environment.ProcessId)throw new ArgumentException("Select an owned application process.");
        processIdentity=Native.OpenProcess(0x1000|0x100000,false,pid);
        Native.Check(processIdentity!=0,"Hold owned event process identity");
        nint desktop=Native.GetThreadDesktop(Native.GetCurrentThreadId());
        try
        {
            uint ownerThread=Native.GetWindowThreadProcessId(window,out uint currentPid);
            Native.Check(currentPid==pid&&Native.ObjectName(Native.GetThreadDesktop(ownerThread))==Native.ObjectName(desktop),"Owned event process/desktop identity changed");
            thread=new Thread(()=>Run(desktop)){IsBackground=true,Name="VIAVW passive owned window events"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        }
        catch{Native.CloseHandle(processIdentity);processIdentity=0;throw;}
        if(!ready.Wait(200)){Stop();throw new TimeoutException("Private event registration did not become ready within 200 ms.");}
        if(error!=null){Stop();throw new InvalidOperationException(error);}
    }
    void Run(nint desktop)
    {
        nint hook=0;GCHandle keepAlive=default;
        WinEvent callback=(_,kind,window,objectId,childId,_,eventTime)=>
        {
            try
            {
                if(stopping||window==0)return;
                Native.GetWindowThreadProcessId(window,out uint sourcePid);if(sourcePid!=pid)return;
                long now=Stopwatch.GetTimestamp();
                lock(gate){if(events.Count==1024){events.RemoveAt(0);dropped++;}events.Add((now,kind,(long)window,objectId,childId,eventTime));}
            }
            catch(Exception failure){lock(gate)error=failure.Message;}
        };
        try
        {
            Native.Check(SetThreadDesktop(desktop),"Bind passive window observer to private desktop");WindowCaptureResearch.PrivateOnly();
            lock(gate)threadId=Native.GetCurrentThreadId();Native.PeekMessageW(out _,0,0,0,0);
            if(stopping){ready.Set();return;}
            keepAlive=GCHandle.Alloc(callback);
            hook=SetWinEventHook(0x8000,0x8015,0,callback,pid,0,2);Native.Check(hook!=0,"Subscribe to exact owned process events");
            registered=true;ready.Set();
            while(!stopping)
            {
                int result=GetMessageW(out var message,0,0,0);
                if(result==0)break;Native.Check(result>0,"Pump passive owned events");TranslateMessage(ref message);DispatchMessageW(ref message);
            }
        }
        catch(Exception failure){lock(gate)error=failure.ToString();ready.Set();}
        finally
        {
            lock(gate)threadId=0;
            if(hook!=0){removed=UnhookWinEvent(hook);if(!removed)lock(gate)error="UnhookWinEvent failed: "+Marshal.GetLastWin32Error();}
            if(keepAlive.IsAllocated)keepAlive.Free();
            if(processIdentity!=0){Native.CloseHandle(processIdentity);processIdentity=0;}
        }
    }
    internal object Snapshot()
    {
        lock(gate)return new{registered,removed,error,dropped,pid,lastQpc=events.Count==0?0:events[^1].Qpc,events=events.Select(e=>e.Qpc).ToArray(),records=events.Select(e=>new{qpc=e.Qpc,eventId=e.Kind,window=e.Window,objectId=e.Object,childId=e.Child,eventTime=e.EventTime}).ToArray(),note="Exact-process passive WinEvent receipts on a private desktop. They are hints, not input-processing or rendering acknowledgements. No global subscription or injected callback."};
    }
    void Stop(){lock(gate){stopping=true;uint id=threadId;if(id!=0)PostThreadMessageW(id,0x12,0,0);}}
    public void Dispose(){Stop();if(!thread.Join(250))throw new TimeoutException("Passive owned event thread did not stop within 250 ms.");ready.Dispose();}
}

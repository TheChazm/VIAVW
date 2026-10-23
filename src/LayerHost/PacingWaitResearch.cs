using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal sealed class ResearchWaitableTimer : IDisposable
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint CreateWaitableTimerExW(nint attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint callback,nint argument,bool resume);
    [DllImport("kernel32.dll")]static extern bool CancelWaitableTimer(nint timer);
    nint timer;
    internal ResearchWaitableTimer(){timer=CreateWaitableTimerExW(0,null,2,0x100002);Native.Check(timer!=0,"Create disposable high-resolution timer");}
    internal void Wait(int milliseconds)
    {
        ObjectDisposedException.ThrowIf(timer==0,this);if(milliseconds is <1 or >100)throw new ArgumentException("Bound research waits to 1–100 milliseconds");
        long due=-milliseconds*10000L;Native.Check(SetWaitableTimer(timer,ref due,0,0,0,false),"Set disposable one-shot timer");
        if(Native.WaitForSingleObject(timer,(uint)(milliseconds+1000))!=0)throw new TimeoutException("Disposable pacing timer did not signal");
    }
    public void Dispose(){if(timer!=0){CancelWaitableTimer(timer);Native.CloseHandle(timer);timer=0;}}
}
internal static class PacingWaitResearch
{
    internal static object Measure()
    {
        WindowCaptureResearch.PrivateOnly();var samples=new List<object>();using var process=Process.GetCurrentProcess();double before=process.TotalProcessorTime.TotalMilliseconds;var clock=Stopwatch.StartNew();
        using(var timer=new ResearchWaitableTimer())
        for(int round=0;round<24;round++)foreach(int delay in new[]{1,5,8,16})foreach(bool precise in round%2==0?new[]{false,true}:new[]{true,false})
        {
            long started=Stopwatch.GetTimestamp();if(precise)timer.Wait(delay);else Thread.Sleep(delay);
            samples.Add(new{round,requestedMilliseconds=delay,method=precise?"one-shot high-resolution timer":"Thread.Sleep",actualMilliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency});
        }
        return new{completed=true,samples,workerCpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds-before,wallMilliseconds=clock.Elapsed.TotalMilliseconds,note="Alternating disposable waits on the private RPC worker. No timeBeginPeriod, system timer-resolution change, spin waiting, physical input, or persistent timer. This is a wait primitive benchmark, not application compatibility evidence."};
    }
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var layer=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();string? failure=null;JsonElement? result=null;int[] owned=[];
        try{result=layer.ResearchMeasurePacingWaits();}catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);}finally{owned=layer.GetProcessIds();layer.Dispose();}
        var physical=monitor.Complete(owned);bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"pacing-waits.json"),JsonSerializer.Serialize(new{passed,failure,result,cleanup,owned,physical},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

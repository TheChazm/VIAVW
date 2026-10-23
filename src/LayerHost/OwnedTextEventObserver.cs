using System.Diagnostics;
using System.Windows.Automation;

namespace LayerHost;

// Research only. A single owned editor subscription, registered and removed on
// one private-desktop MTA thread. Callbacks only timestamp; they do not read or
// mutate the editor and do not call physical/global input APIs.
internal sealed class OwnedTextEventObserver : IDisposable
{
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]static extern bool SetThreadDesktop(nint desktop);
    readonly object gate=new();
    readonly List<long> events=[];
    readonly ManualResetEventSlim ready=new(),stop=new();
    readonly Thread thread;
    string? error,removeError;bool registered,removed;int dropped;long lastQpc;
    internal OwnedTextEventObserver(nint root,int[] allowed)
    {
        WindowCaptureResearch.PrivateOnly();nint desktop=Native.GetThreadDesktop(Native.GetCurrentThreadId());
        thread=new Thread(()=>Observe(root,allowed,desktop)){IsBackground=true,Name="VIAVW owned editor events"};
        thread.SetApartmentState(ApartmentState.MTA);thread.Start();
        if(!ready.Wait(TimeSpan.FromSeconds(5))){stop.Set();throw new TimeoutException("Owned editor event registration exceeded five seconds");}
        if(error!=null){stop.Set();throw new InvalidOperationException(error);}
    }
    void Observe(nint root,int[] allowed,nint desktop)
    {
        AutomationElement? editor=null;AutomationEventHandler handler=(_,_)=>
        {
            lock(gate){lastQpc=Stopwatch.GetTimestamp();if(events.Count==1024){events.RemoveAt(0);dropped++;}events.Add(lastQpc);}
        };
        try
        {
            Native.Check(SetThreadDesktop(desktop),"Select owned event desktop");WindowCaptureResearch.PrivateOnly();
            editor=AutomationElement.FromHandle(root).FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"WpfTextView"),new PropertyCondition(AutomationElement.IsOffscreenProperty,false)));
            if(editor==null||!allowed.Contains(editor.Current.ProcessId)||!editor.TryGetCurrentPattern(TextPattern.Pattern,out _))throw new InvalidOperationException("No owned visible editor TextPattern for event observation");
            Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent,editor,TreeScope.Element,handler);registered=true;ready.Set();
            stop.Wait();
        }
        catch(Exception e){error=e.ToString();ready.Set();}
        finally
        {
            if(registered&&editor!=null)try{Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent,editor,handler);removed=true;}catch(Exception e){removeError=e.ToString();}
        }
    }
    internal object Snapshot(){lock(gate)return new{registered,removed,error,removeError,dropped,lastQpc,events=events.ToArray(),note="Receipt times of read-only UIA TextChanged callbacks, not source input-processing or render-completion timestamps"};}
    public void Dispose(){stop.Set();thread.Join(2000);}
}

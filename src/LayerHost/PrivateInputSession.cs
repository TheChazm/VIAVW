namespace LayerHost;

// One worker-thread lease, confined to validated non-input desktop HWNDs. Keeping
// the queues connected avoids repeated attach/detach state resets. This is not
// global input, Raw Input, or GetAsyncKeyState emulation.
internal sealed class PrivateInputSession : IDisposable
{
    uint target;
    ResearchWaitableTimer? pacingTimer;
    internal bool PreciseMouseWaits {get;private set;}
    nint threadHandle;
    int attachments,switches,seeds;
    internal bool Enabled {get;private set;}
    internal bool LowLatency {get;private set;}
    internal int StepMilliseconds=>LowLatency?5:30;
    internal int ChordHoldMilliseconds=>LowLatency?8:50;
    internal void Enable(bool lowLatency=false,bool preciseMouseWaits=false){WindowCaptureResearch.PrivateOnly();if(preciseMouseWaits&&!lowLatency)throw new ArgumentException("Precise pacing requires the bounded fast private input session.");if(preciseMouseWaits)pacingTimer??=new();else{pacingTimer?.Dispose();pacingTimer=null;}Enabled=true;LowLatency=lowLatency;PreciseMouseWaits=preciseMouseWaits;}
    // Default keyboard chords retain verified Thread.Sleep pacing. The optional
    // per-command research policy uses precise steps with an explicit longer hold.
    internal void DelayMouse(int milliseconds){if(PreciseMouseWaits&&milliseconds<30)pacingTimer!.Wait(milliseconds);else Thread.Sleep(milliseconds);}
    internal void DelayChord(int milliseconds,bool precise){if(precise){pacingTimer??=new();pacingTimer.Wait(milliseconds);}else Thread.Sleep(milliseconds);}
    internal static byte[] Idle(byte[] value){var result=(byte[])value.Clone();for(int i=0;i<result.Length;i++)result[i]&=0x7f;return result;}
    internal void Ensure(nint window)
    {
        WindowCaptureResearch.PrivateOnly();uint next=Native.GetWindowThreadProcessId(window,out _);
        if(target!=next||threadHandle==0||Native.WaitForSingleObject(threadHandle,0)==0)
        {
            DisposeConnection();Native.PeekMessageW(out _,0,0,0,0);
            threadHandle=Native.OpenThread(0x100000,false,next);Native.Check(threadHandle!=0,"Hold private input thread identity");
            try{Native.Check(Native.AttachThreadInput(Native.GetCurrentThreadId(),next,true),"Connect persistent private input queue");target=next;attachments++;}
            catch{Native.CloseHandle(threadHandle);threadHandle=0;throw;}
        }
        var state=new byte[256];Native.Check(Native.GetKeyboardState(state),"Read private idle state");Native.Check(Native.SetKeyboardState(Idle(state)),"Set explicit private idle state");
    }
    internal object Seed(nint window,int[] keys)
    {
        if(!Enabled||keys.Any(k=>k is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)))throw new ArgumentException("Seed only private test modifiers in an enabled session.");
        Ensure(window);var state=new byte[256];Native.Check(Native.GetKeyboardState(state),"Read private test state");foreach(int key in keys)state[key]|=0x80;
        Native.Check(Native.SetKeyboardState(state),"Seed disposable private modifiers");seeds++;return new{seeded=keys,target,note="No physical input devices or default-desktop queues"};
    }
    internal object State()=>new{Enabled,LowLatency,PreciseMouseWaits,StepMilliseconds,ChordHoldMilliseconds,target,attachments,switches,seeds,threadHandleHeld=threadHandle!=0};
    void DisposeConnection()
    {
        if(threadHandle==0)return;
        try{if(target!=0&&Native.WaitForSingleObject(threadHandle,0)!=0)Native.Check(Native.AttachThreadInput(Native.GetCurrentThreadId(),target,false),"Disconnect private input queue");}
        finally{Native.CloseHandle(threadHandle);threadHandle=0;target=0;switches++;}
    }
    public void Dispose(){try{DisposeConnection();}finally{pacingTimer?.Dispose();pacingTimer=null;}}
}

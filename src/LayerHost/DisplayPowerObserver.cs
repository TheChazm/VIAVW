using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LayerHost;

// Hidden notification HWND on its own message-pump thread. Never shown or activated;
// these APIs observe the session and do not request display wake or change power policy.
internal sealed class DisplayPowerObserver : IDisposable
{
    static readonly Guid Display = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"), Presence = new("3C0F4548-C03F-4C4D-B9F2-237EDE686376");
    [DllImport("user32.dll", SetLastError = true)] static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterPowerSettingNotification(nint registration);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(nint userRoot, out nint scheme);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint memory);
    readonly object gate = new(); readonly ManualResetEventSlim ready = new(); readonly Thread thread;
    readonly List<object> events = []; nint window; Exception? error; int? display, presence; bool disposed;
    volatile bool stopping;
    internal bool ThreadStopped => !thread.IsAlive;
    internal DisplayPowerObserver()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "VIAVW passive display observation" }; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!ready.Wait(5000)) { stopping = true; if (window != 0) Native.PostMessageW(window, 0x8001, 0, 0); thread.Join(3000); throw new TimeoutException("Power observation startup deadline."); }
        if (error != null) { thread.Join(1000); throw new InvalidOperationException("Passive power observation unavailable.", error); }
    }
    internal object Snapshot()
    {
        string? scheme = null; uint result = PowerGetActiveScheme(0, out nint memory);
        if (result == 0 && memory != 0) { try { scheme = Marshal.PtrToStructure<Guid>(memory).ToString(); } finally { LocalFree(memory); } }
        lock (gate) return new { utc = DateTime.UtcNow, sessionDisplay = display, sessionPresence = presence, activePowerScheme = scheme, activeSchemeResult = result, lastInputTick = Native.LastInputTick(), tickCount = Environment.TickCount64, eventCount = events.Count };
    }
    internal object[] Events() { lock (gate) return events.ToArray(); }
    void Run()
    {
        try
        {
            using var context = new ApplicationContext(); using var observerWindow = new NotificationWindow(this, context);
            window = observerWindow.Handle; ready.Set(); if (!stopping) Application.Run(context);
        }
        catch (Exception e) { error = e; ready.Set(); }
    }
    sealed class NotificationWindow : NativeWindow, IDisposable
    {
        readonly DisplayPowerObserver owner; readonly ApplicationContext context; readonly List<nint> registrations = [];
        internal NotificationWindow(DisplayPowerObserver owner, ApplicationContext context)
        {
            this.owner = owner; this.context = context;
            CreateHandle(new CreateParams { Caption = "VIAVW passive power diagnostics", Style = 0 });
            try
            {
                foreach (var setting in new[] { Display, Presence })
                {
                    Guid copy = setting; nint handle = RegisterPowerSettingNotification(Handle, ref copy, 0);
                    if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Register passive power notification"); registrations.Add(handle);
                }
            }
            catch { Dispose(); throw; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8001) { context.ExitThread(); return; }
            if (message.Msg == 0x218 && message.WParam == 0x8013 && message.LParam != 0)
            {
                Guid setting = Marshal.PtrToStructure<Guid>(message.LParam); int length = Marshal.ReadInt32(message.LParam, 16);
                if (length == 4 && (setting == Display || setting == Presence))
                {
                    int value = Marshal.ReadInt32(message.LParam, 20);
                    lock (owner.gate)
                    {
                        if (setting == Display) owner.display = value; else owner.presence = value;
                        if (owner.events.Count < 256) owner.events.Add(new { utc = DateTime.UtcNow, setting = setting == Display ? "session-display" : "session-presence", value, tickCount = Environment.TickCount64, lastInputTick = Native.LastInputTick() });
                    }
                }
            }
            base.WndProc(ref message);
        }
        public void Dispose() { foreach (nint handle in registrations) UnregisterPowerSettingNotification(handle); registrations.Clear(); DestroyHandle(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        stopping = true;
        if (window != 0 && thread.IsAlive) Native.PostMessageW(window, 0x8001, 0, 0);
        if (!thread.Join(3000)) throw new TimeoutException("Passive power observation shutdown deadline.");
        ready.Dispose();
    }
}

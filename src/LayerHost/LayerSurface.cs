namespace LayerHost;

/// <summary>Common interaction API; each backend documents its input and capture semantics.</summary>
public interface ILayerSurface
{
    string Id { get; }
    int ApplicationProcessId { get; }
    ControlOwner Owner { get; }
    string? AttentionReason { get; }
    void SendKeyboardInput(string text, ControlOwner sender = ControlOwner.Automation);
    void SendMouseInput(int x, int y, ControlOwner sender = ControlOwner.Automation);
    CaptureFrame CaptureVisualOutput();
    void RequestAttention(string reason);
    void TakeHumanControl();
    void ReturnToAutomation();
}

/// <summary>Client-pixel window surface over a native application layer.</summary>
public sealed class NativeWindowSurface : ILayerSurface
{
    readonly ApplicationLayer layer;
    readonly long root;
    readonly object gate = new();
    long keyboardTarget;
    public string Id => layer.Id;
    public int ApplicationProcessId => layer.ApplicationProcessId;
    public ControlOwner Owner => layer.Owner;
    public string? AttentionReason => layer.AttentionReason;

    public NativeWindowSurface(ApplicationLayer layer, long root, long? keyboardTarget = null)
    { this.layer = layer; this.root = root; this.keyboardTarget = keyboardTarget ?? root; }
    public void SelectKeyboardTarget(long hwnd) { lock (gate) keyboardTarget = hwnd; }
    public void SendKeyboardInput(string text, ControlOwner sender = ControlOwner.Automation)
    { lock (gate) layer.SendKeyboardInput(keyboardTarget, text, sender); }
    public void SendMouseInput(int x, int y, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate)
        {
            var hit = layer.HitTest(root, x, y);
            layer.SendMouseInput(hit.Window, hit.X, hit.Y, sender);
            keyboardTarget = hit.Window;
        }
    }
    public CaptureFrame CaptureVisualOutput() => layer.CaptureVisualOutput(root);
    public void RequestAttention(string reason) => layer.RequestAttention(reason);
    public void TakeHumanControl() => layer.TakeHumanControl();
    public void ReturnToAutomation() => layer.ReturnToAutomation();
}

using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace LayerHost;

// Application-specific adapter: CDP input targets a browser page, not Windows input devices.
// It uses a disposable profile and the same desktop/job/process lifetime as the native backend.
public sealed class ChromiumLayer : ILayerSurface, IDisposable
{
    readonly object gate = new();
    readonly LayerManager manager = new();
    readonly ApplicationLayer nativeLayer;
    readonly ClientWebSocket socket = new();
    int sequence;
    ControlOwner owner = ControlOwner.Automation;
    public string Id => nativeLayer.Id;
    public int ApplicationProcessId => nativeLayer.ApplicationProcessId;
    public string TargetId { get; }
    public string? AttentionReason { get; private set; }
    public ControlOwner Owner { get { lock (gate) return owner; } }
    public int[] GetProcessIds() => nativeLayer.GetProcessIds();

    public ChromiumLayer(string executable, string profileDirectory, bool headless = true)
    {
        profileDirectory = Path.GetFullPath(profileDirectory);
        if (Directory.Exists(profileDirectory)) throw new ArgumentException("Use a new disposable profile directory.");
        Directory.CreateDirectory(profileDirectory);
        nativeLayer = manager.CreateLayer();
        try
        {
            var launchArguments = new List<string> { "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profileDirectory,
                 "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions",
                 "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,450", "about:blank" };
            if (headless) launchArguments.Insert(0, "--headless=new");
            nativeLayer.LaunchApplication(executable, launchArguments.ToArray(), profileDirectory);
            var portFile = Path.Combine(profileDirectory, "DevToolsActivePort");
            var watch = Stopwatch.StartNew();
            while (!File.Exists(portFile) && watch.ElapsedMilliseconds < 12000) Thread.Sleep(100);
            var lines = File.ReadAllLines(portFile);
            int port = int.Parse(lines[0]);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
            string list = http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(list);
            var target = doc.RootElement.EnumerateArray().First(t => t.GetProperty("type").GetString() == "page");
            TargetId = target.GetProperty("id").GetString()!;
            var uri = new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
            if (uri.Host != "127.0.0.1" && uri.Host != "localhost") throw new InvalidOperationException("Refuse a non-local DevTools endpoint.");
            using var cancellation = new CancellationTokenSource(5000);
            socket.ConnectAsync(uri, cancellation.Token).GetAwaiter().GetResult();
            Call("Page.enable", new { });
            Call("Emulation.setDeviceMetricsOverride", new { width = 800, height = 450, deviceScaleFactor = 1, mobile = false });
        }
        catch { socket.Dispose(); manager.Dispose(); throw; }
    }

    JsonElement Call(string method, object parameters)
    {
        int id = ++sequence;
        using var cancellation = new CancellationTokenSource(5000);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        try
        {
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation.Token).GetAwaiter().GetResult();
            var buffer = new byte[65536];
            while (true)
            {
                using var message = new MemoryStream(); WebSocketReceiveResult part;
                do
                {
                    part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token).GetAwaiter().GetResult();
                    if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Browser CDP disconnected.");
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 32000000) throw new IOException("CDP response too large.");
                } while (!part.EndOfMessage);
                using var doc = JsonDocument.Parse(message.ToArray());
                if (!doc.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                if (doc.RootElement.TryGetProperty("error", out var error)) throw new LayerOperationException(error.ToString());
                return doc.RootElement.GetProperty("result").Clone();
            }
        }
        catch (LayerOperationException) { throw; }
        catch { nativeLayer.Dispose(); throw; }
    }
    void CheckOwner(ControlOwner sender) { if (sender != owner) throw new InvalidOperationException($"Input rejected: {owner} owns this page."); }
    public void Navigate(string url) { lock (gate) Call("Page.navigate", new { url }); }
    public JsonElement Inspect(string expression)
    {
        lock (gate)
        {
            var value = Call("Runtime.evaluate", new { expression, returnByValue = true });
            if (value.TryGetProperty("exceptionDetails", out var exception)) throw new LayerOperationException(exception.ToString());
            return value.GetProperty("result").GetProperty("value").Clone();
        }
    }
    public void SendKeyboardInput(string text, ControlOwner sender = ControlOwner.Automation)
    {
        if (text.Length > 4096) throw new ArgumentException("Bound text batches to 4096 code units.");
        lock (gate)
        {
            CheckOwner(sender);
            foreach (char c in text)
            {
                // Text-bearing keyDown then keyUp: Blink's input path, no DOM value assignment.
                Call("Input.dispatchKeyEvent", new { type = "keyDown", key = c.ToString(), text = c.ToString(), unmodifiedText = c.ToString() });
                Call("Input.dispatchKeyEvent", new { type = "keyUp", key = c.ToString() });
            }
        }
    }
    public void SendMouseInput(int x, int y, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate)
        {
            CheckOwner(sender);
            Call("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y, button = "none" });
            Call("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
            Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
        }
    }
    public void SendKey(string key, string code, int virtualKey, int modifiers = 0, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate)
        {
            CheckOwner(sender);
            Call("Input.dispatchKeyEvent", new { type = "keyDown", key, code, windowsVirtualKeyCode = virtualKey, modifiers });
            Call("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = virtualKey, modifiers });
        }
    }
    public CaptureFrame CaptureVisualOutput()
    {
        lock (gate)
        {
            var watch = Stopwatch.StartNew();
            var result = Call("Page.captureScreenshot", new { format = "png", fromSurface = true, captureBeyondViewport = false });
            return new(Convert.FromBase64String(result.GetProperty("data").GetString()!), 800, 450, watch.Elapsed.TotalMilliseconds, "Chromium / CDP Page.captureScreenshot");
        }
    }
    public void RequestAttention(string reason) { lock (gate) AttentionReason = reason; }
    internal WindowInfo[] NativeWindows() => nativeLayer.GetWindows();
    internal CaptureFrame NativeCapture(long window, CaptureBackend backend) => nativeLayer.CaptureVisualOutput(window, backend);
    internal void HideNative(long window) => nativeLayer.HideLayer(window);
    internal void ShowNative(long window) => nativeLayer.ShowLayer(window);
    internal JsonElement NativePointer(long window, int x, int y) => nativeLayer.SendQueueMouseSequence(window, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
    internal void NativeText(long window, string text) => nativeLayer.SendKeyboardInput(window, text);
    internal void NativeChord(long window, int[] keys) => nativeLayer.SendQueueStateChord(window, keys);
    public void TakeHumanControl() { lock (gate) owner = ControlOwner.Human; }
    public void ReturnToAutomation() { lock (gate) { owner = ControlOwner.Automation; AttentionReason = null; } }
    public void Dispose() { lock (gate) { socket.Dispose(); manager.Dispose(); } }
}

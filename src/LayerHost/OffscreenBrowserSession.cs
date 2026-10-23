using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

// Experimental presentation backend. Browser startup/input use CDP; pixels use native WGC.
// This shares Default's compositor, so it is deliberately separate from ApplicationLayer.
public sealed class OffscreenBrowserSession : ILayerSurface, IDisposable
{
    readonly object gate = new();
    readonly string directory;
    nint job, root;
    LocalCdp cdp = null!;
    string session = "";
    bool closed;
    readonly bool reusable, faultFixture;
    ReusableCaptureClient? captureClient;
    bool visualUpdatePending;
    ControlOwner owner = ControlOwner.Automation;
    public string Id { get; } = "OffscreenChrome_" + Guid.NewGuid().ToString("N");
    public int ApplicationProcessId { get; private set; }
    public ControlOwner Owner { get { lock (gate) return owner; } }
    public string? AttentionReason { get; private set; }
    public int ViewportOffsetY { get; private set; }
    public string Renderer { get; private set; } = "";
    public double? LastCaptureHelperCpuMilliseconds { get; private set; }
    public int CaptureWorkerProcessId { get { lock (gate) return captureClient?.ProcessId ?? 0; } }
    public JsonElement CaptureDiagnostics { get { lock (gate) return captureClient?.LastResult ?? default; } }

    public OffscreenBrowserSession(string directory) : this(directory, true) { }
    internal OffscreenBrowserSession(string directory, bool reusable, bool faultFixture = false)
    {
        this.reusable = reusable; this.faultFixture = faultFixture;
        this.directory = Path.GetFullPath(directory);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string relative = Path.GetRelativePath(Path.Combine(project, "artifacts"), this.directory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new ArgumentException("Use a fresh experiment directory below project artifacts.");
        if (Directory.Exists(this.directory)) throw new ArgumentException("Use a fresh experiment directory.");
        if (Native.InputDesktopName() != "Default" || Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())) != "Default") throw new InvalidOperationException("This experimental backend requires the ordinary Default desktop.");
        Directory.CreateDirectory(this.directory);
        try
        {
            job = Native.CreateJobObjectW(0, null); Native.Check(job != 0, "GPU browser job");
            var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
            Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "GPU browser kill-on-close job");
            var bounds = SystemInformation.VirtualScreen; string profile = Path.Combine(this.directory, "profile");
            var app = Native.LaunchSuspended("C:/Program Files/Google/Chrome/Application/chrome.exe", ["--no-startup-window", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=680,640", $"--window-position={bounds.Right + 200},{bounds.Top}"], "WinSta0\\Default", this.directory, startupShow: 0);
            try { Native.Check(Native.AssignProcessToJobObject(job, app.hProcess), "Own GPU browser"); ApplicationProcessId = (int)app.dwProcessId; Native.Check(Native.ResumeThread(app.hThread) != uint.MaxValue, "Resume GPU browser"); }
            catch { Native.TerminateProcess(app.hProcess, 1); throw; }
            finally { Native.CloseHandle(app.hThread); Native.CloseHandle(app.hProcess); }
            string portFile = Path.Combine(profile, "DevToolsActivePort"); var watch = Stopwatch.StartNew();
            while (!File.Exists(portFile) && watch.ElapsedMilliseconds < 12000) Thread.Sleep(50);
            var lines = File.ReadAllLines(portFile); cdp = new LocalCdp("ws://127.0.0.1:" + lines[0] + lines[1]);
            string target = cdp.Call("Target.createTarget", new { url = new Uri(Path.Combine(project, "experiments/gpu-browser.html")).AbsoluteUri, newWindow = true, background = true, left = bounds.Right + 200, top = bounds.Top, width = 680, height = 640 }).GetProperty("targetId").GetString()!;
            session = cdp.Call("Target.attachToTarget", new { targetId = target, flatten = true }).GetProperty("sessionId").GetString()!;
            watch.Restart();
            while (root == 0 && watch.ElapsedMilliseconds < 5000)
            {
                var ids = GetProcessIds();
                Native.EnumProc collect = (hwnd, _) => { Native.GetWindowThreadProcessId(hwnd, out uint pid); if (ids.Contains((int)pid) && Native.ClassName(hwnd) == "Chrome_WidgetWin_1" && Native.IsWindowVisible(hwnd)) root = hwnd; return true; };
                Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), collect, 0); Thread.Sleep(50);
            }
            if (root == 0) throw new InvalidOperationException("Owned GPU browser window unavailable.");
            ValidateRoot(); Native.SetWindowLongPtr(root, -20, Native.GetWindowLongPtr(root, -20) | 0x08000080);
            Native.Check(Native.SetWindowPos(root, 0, bounds.Right + 200, bounds.Top, 0, 0, 0x0010 | 0x0004 | 0x0001 | 0x0020), "Place owned GPU window offscreen");
            Thread.Sleep(400); Renderer = Inspect("observations.renderer").GetString() ?? "WebGL renderer unavailable";
            if (!Inspect("observations.webgl").GetBoolean()) throw new InvalidOperationException("WebGL did not initialize.");
            if (Renderer.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase) || Renderer.Contains("software", StringComparison.OrdinalIgnoreCase) || Renderer.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The GPU demonstration requires an identifiable hardware WebGL renderer: " + Renderer);
            File.WriteAllText(Path.Combine(this.directory, "session.json"), JsonSerializer.Serialize(new { Id, ApplicationProcessId, Renderer, pids = GetProcessIds(), mode = "experimental offscreen Default desktop; CDP startup/input; native WGC capture" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { Dispose(); throw; }
    }
    void ValidateRoot()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!Native.IsWindow(root)) throw new InvalidOperationException("Owned browser window closed.");
        uint thread = Native.GetWindowThreadProcessId(root, out uint pid);
        if (!Native.JobProcessIds(job).Contains((int)pid) || Native.ObjectName(Native.GetThreadDesktop(thread)) != "Default") throw new InvalidOperationException("Browser HWND no longer belongs to this job/desktop.");
    }
    void CheckOwner(ControlOwner sender)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (sender != owner) throw new InvalidOperationException($"Input rejected: {owner} owns this GPU browser.");
    }
    public int[] GetProcessIds() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); return Native.JobProcessIds(job); } }
    public JsonElement Inspect(string expression)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var result = cdp.Call("Runtime.evaluate", new { expression, returnByValue = true }, session);
            if (result.TryGetProperty("exceptionDetails", out var error)) throw new LayerOperationException(error.ToString());
            return result.GetProperty("result").GetProperty("value").Clone();
        }
    }
    public void SendKeyboardInput(string text, ControlOwner sender = ControlOwner.Automation)
    {
        if (text.Length > 4096) throw new ArgumentException("Bound text batches to 4096 code units.");
        lock (gate)
        {
            CheckOwner(sender);
            foreach (char c in text) { cdp.Call("Input.dispatchKeyEvent", new { type = "keyDown", key = c.ToString(), text = c.ToString(), unmodifiedText = c.ToString() }, session); cdp.Call("Input.dispatchKeyEvent", new { type = "keyUp", key = c.ToString() }, session); }
            visualUpdatePending |= text.Length != 0;
        }
    }
    public void SendKey(string key, string code, int virtualKey, int modifiers = 0, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate) { CheckOwner(sender); cdp.Call("Input.dispatchKeyEvent", new { type = "keyDown", key, code, windowsVirtualKeyCode = virtualKey, modifiers }, session); cdp.Call("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = virtualKey, modifiers }, session); visualUpdatePending = true; }
    }
    public void SendMouseInput(int x, int y, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate) { CheckOwner(sender); cdp.Call("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, session); cdp.Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, session); visualUpdatePending = true; }
    }
    public void ClickElement(string id, ControlOwner sender = ControlOwner.Automation)
    {
        lock (gate)
        {
            CheckOwner(sender);
            if (id is not ("editor" or "toggle")) throw new ArgumentException("This demonstration exposes only its disposable page controls.");
            var rect = Inspect($"(()=>{{let r=document.getElementById('{id}').getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2}}}})()");
            SendMouseInput((int)rect.GetProperty("x").GetDouble(), (int)rect.GetProperty("y").GetDouble(), sender);
        }
    }
    public CaptureFrame CaptureVisualOutput()
        => Capture(reusable, true);
    internal CaptureFrame CaptureColdForComparison() => Capture(false);
    internal CaptureFrame CaptureImmediateForResearch() => Capture(true);
    CaptureFrame Capture(bool reuse, bool settle = false)
    {
        lock (gate)
        {
            ValidateRoot(); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string png = Path.Combine(directory, "latest-wgc.png"); if (File.Exists(png)) File.Delete(png);
            var watch = Stopwatch.StartNew();
            if (settle && visualUpdatePending)
            {
                // Browser-specific visual scheduling barrier, not a universal application acknowledgement.
                cdp.Call("Runtime.evaluate", new { expression = "new Promise(resolve=>{let done=false;const finish=()=>{if(!done){done=true;resolve(true)}};requestAnimationFrame(()=>requestAnimationFrame(finish));setTimeout(finish,100)})", awaitPromise = true, returnByValue = true }, session);
                visualUpdatePending = false;
            }
            if (reuse)
            {
                captureClient ??= new ReusableCaptureClient(job, "Default", directory, faultFixture);
                var result = captureClient.Capture(root, png); LastCaptureHelperCpuMilliseconds = result.GetProperty("cpuMs").GetDouble();
                byte[] pixels = File.ReadAllBytes(png); using var decoded = new MemoryStream(pixels); using var bitmap = new Bitmap(decoded);
                ViewportOffsetY = bitmap.Height - Inspect("innerHeight").GetInt32();
                return new(pixels, bitmap.Width, bitmap.Height, watch.Elapsed.TotalMilliseconds, "WindowsGraphicsCapture / reusable device / offscreen Chrome");
            }
            var helper = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/WgcCapture.exe"), [root.ToString(), png], "WinSta0\\Default", directory);
            uint exit;
            try
            {
                Native.Check(Native.AssignProcessToJobObject(job, helper.hProcess), "Own GPU capture helper"); Native.Check(Native.ResumeThread(helper.hThread) != uint.MaxValue, "Resume GPU capture helper");
                if (Native.WaitForSingleObject(helper.hProcess, 4500) != 0) { Dispose(); throw new TimeoutException("GPU capture deadline; owned session terminated."); }
                Native.Check(Native.GetExitCodeProcess(helper.hProcess, out exit), "GPU capture result");
                LastCaptureHelperCpuMilliseconds = Native.GetProcessTimes(helper.hProcess, out _, out _, out long kernel, out long user) ? (kernel + user) / 10000.0 : null;
            }
            catch { if (!closed) Native.TerminateProcess(helper.hProcess, 1); throw; }
            finally { Native.CloseHandle(helper.hThread); Native.CloseHandle(helper.hProcess); }
            if (exit != 0) throw new LayerOperationException(File.Exists(Path.ChangeExtension(png, ".error.txt")) ? File.ReadAllText(Path.ChangeExtension(png, ".error.txt")) : "Native GPU capture failed.");
            byte[] bytes = File.ReadAllBytes(png); using var stream = new MemoryStream(bytes); using var image = new Bitmap(stream);
            ViewportOffsetY = image.Height - Inspect("innerHeight").GetInt32();
            return new(bytes, image.Width, image.Height, watch.Elapsed.TotalMilliseconds, "WindowsGraphicsCapture / offscreen Chrome on Default");
        }
    }
    public void RequestAttention(string reason) { lock (gate) AttentionReason = reason; }
    public void RestartCaptureWorker() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); captureClient?.Restart(); } }
    internal void StallCaptureWorkerForTest() { lock (gate) { captureClient ??= new ReusableCaptureClient(job, "Default", directory, faultFixture); captureClient.StallForTest(); } }
    internal void ResizeForTest(int width, int height)
    {
        lock (gate) { ValidateRoot(); if (width is < 320 or > 1600 || height is < 320 or > 1200) throw new ArgumentException("Bound test size"); var bounds = SystemInformation.VirtualScreen; Native.Check(Native.SetWindowPos(root, 0, bounds.Right + 200, bounds.Top, width, height, 0x0010 | 0x0004), "Resize owned offscreen source without activation"); }
    }
    public void TakeHumanControl() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); owner = ControlOwner.Human; } }
    public void ReturnToAutomation() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); owner = ControlOwner.Automation; AttentionReason = null; } }
    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return; closed = true;
            captureClient?.Dispose(); cdp?.Dispose(); if (job != 0) { Native.CloseHandle(job); job = 0; }
        }
    }
}

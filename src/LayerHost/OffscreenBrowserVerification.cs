using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class OffscreenBrowserVerification
{
    internal static int Run(string output, string rendering = "occluded")
    {
        Directory.CreateDirectory(output); string profile = Path.Combine(output, "profile");
        if (Directory.Exists(profile)) throw new ArgumentException("Use a fresh output folder and browser profile.");
        if (Native.InputDesktopName() != "Default") throw new InvalidOperationException("Requires ordinary Default input desktop.");
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var before = Native.GetForegroundWindow(); var samples = new List<object>(); var captures = new List<object>(); object? gpu = null; int[] pids = []; int targetForegroundHits = 0, foregroundSamples = 0; string? error = null; bool pixelChecksPassed = true, inputChecksPassed = false;
        nint job = Native.CreateJobObjectW(0, null); Native.Check(job != 0, "Browser experiment job");
        var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
        Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Browser job limits");
        using var monitoring = new CancellationTokenSource();
        var monitor = Task.Run(() =>
        {
            while (!monitoring.IsCancellationRequested)
            {
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
                if (Native.JobProcessIds(job).Contains((int)foregroundPid)) Interlocked.Increment(ref targetForegroundHits);
                Interlocked.Increment(ref foregroundSamples); Thread.Sleep(10);
            }
        });
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            var browserArguments = new List<string> { "--no-startup-window", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--window-size=680,540", $"--window-position={bounds.Right + 200},{bounds.Top}" };
            if (rendering is "full" or "occluded") browserArguments.Add("--disable-backgrounding-occluded-windows");
            if (rendering == "full") browserArguments.Add("--disable-renderer-backgrounding");
            if (rendering is "full" or "native") browserArguments.Add("--disable-features=CalculateNativeWinOcclusion,WinUseBrowserNativeOcclusion");
            var app = Native.LaunchSuspended("C:/Program Files/Google/Chrome/Application/chrome.exe", browserArguments, "WinSta0\\Default", output, startupShow: 0);
            try { Native.Check(Native.AssignProcessToJobObject(job, app.hProcess), "Own disposable browser"); Native.ResumeThread(app.hThread); }
            finally { Native.CloseHandle(app.hThread); Native.CloseHandle(app.hProcess); }
            string portFile = Path.Combine(profile, "DevToolsActivePort"); var clock = Stopwatch.StartNew();
            while (!File.Exists(portFile) && clock.ElapsedMilliseconds < 12000) Thread.Sleep(50);
            var lines = File.ReadAllLines(portFile); using var cdp = new LocalCdp("ws://127.0.0.1:" + lines[0] + lines[1]);
            // Background target creation is an explicit browser adapter; capture below is native WGC.
            var created = cdp.Call("Target.createTarget", new { url = new Uri(Path.Combine(project, "experiments/gpu-browser.html")).AbsoluteUri, newWindow = true, background = true, left = bounds.Right + 200, top = bounds.Top, width = 680, height = 540 });
            string target = created.GetProperty("targetId").GetString()!;
            string session = cdp.Call("Target.attachToTarget", new { targetId = target, flatten = true }).GetProperty("sessionId").GetString()!;
            gpu = cdp.Call("SystemInfo.getInfo", new { }).GetProperty("gpu").Clone();
            JsonElement Inspect() => cdp.Call("Runtime.evaluate", new { expression = "({text:editor.value,...observations})", returnByValue = true }, session).GetProperty("result").GetProperty("value").Clone();
            nint root = 0, render = 0; clock.Restart();
            while (root == 0 && clock.ElapsedMilliseconds < 5000)
            {
                pids = Native.JobProcessIds(job);
                Native.EnumProc collect = (hwnd, _) => { Native.GetWindowThreadProcessId(hwnd, out uint pid); if (pids.Contains((int)pid) && Native.ClassName(hwnd) == "Chrome_WidgetWin_1" && Native.IsWindowVisible(hwnd)) root = hwnd; return true; };
                Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), collect, 0); Thread.Sleep(50);
            }
            if (root == 0) throw new InvalidOperationException("No owned browser window.");
            Native.GetWindowRect(root, out var rect); bool initiallyOffscreen = rect.Left >= bounds.Right;
            Native.SetWindowLongPtr(root, -20, Native.GetWindowLongPtr(root, -20) | 0x08000080);
            Native.Check(Native.SetWindowPos(root, 0, bounds.Right + 200, bounds.Top, 0, 0, 0x0010 | 0x0004 | 0x0001 | 0x0020), "Place owned browser outside monitor bounds");
            Native.EnumProc child = (hwnd, _) => { if (Native.ClassName(hwnd) == "Chrome_RenderWidgetHostHWND") render = hwnd; return true; }; Native.EnumChildWindows(root, child, 0);
            Thread.Sleep(400);
            foreach (string phase in new[] { "before", "native-text", "adapter-text", "adapter-color" })
            {
                if (phase == "native-text" && render != 0) foreach (char c in "M3_NATIVE_CHROME") Native.Message(render, Native.WM_CHAR, c, 1);
                if (phase == "adapter-text")
                {
                    cdp.Call("Input.dispatchMouseEvent", new { type = "mousePressed", x = 100, y = 145, button = "left", clickCount = 1 }, session);
                    cdp.Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x = 100, y = 145, button = "left", clickCount = 1 }, session);
                    foreach (char c in "M3_GPU_BROWSER") { cdp.Call("Input.dispatchKeyEvent", new { type = "keyDown", key = c.ToString(), text = c.ToString(), unmodifiedText = c.ToString() }, session); cdp.Call("Input.dispatchKeyEvent", new { type = "keyUp", key = c.ToString() }, session); }
                }
                if (phase == "adapter-color")
                {
                    // Page coordinates; ordinary browser input handling. No DOM model writes.
                    var button = cdp.Call("Runtime.evaluate", new { expression = "(()=>{let r=toggle.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()", returnByValue = true }, session).GetProperty("result").GetProperty("value");
                    double x = button.GetProperty("x").GetDouble(), y = button.GetProperty("y").GetDouble();
                    cdp.Call("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, session);
                    cdp.Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, session);
                }
                Thread.Sleep(250); var state = Inspect(); samples.Add(new { phase, initiallyOffscreen, root = (long)root, render = (long)render, state });
                if (phase == "adapter-color") inputChecksPassed = state.GetProperty("changes").GetInt32() == 1 && state.GetProperty("text").GetString() == "M3_GPU_BROWSER";
                string png = Path.Combine(output, phase + ".png"); var helper = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/WgcCapture.exe"), [root.ToString(), png], "WinSta0\\Default", output);
                uint exit;
                try { Native.Check(Native.AssignProcessToJobObject(job, helper.hProcess), "Own capture helper"); Native.ResumeThread(helper.hThread); if (Native.WaitForSingleObject(helper.hProcess, 4500) != 0) throw new TimeoutException("Browser WGC deadline"); Native.Check(Native.GetExitCodeProcess(helper.hProcess, out exit), "Browser capture exit"); }
                finally { Native.CloseHandle(helper.hThread); Native.CloseHandle(helper.hProcess); }
                int green = 0, orange = 0;
                object? sample = null;
                if (File.Exists(png)) { using var image = new Bitmap(png); var center = image.GetPixel(100, 320); sample = new { center.R, center.G, center.B }; for (int y = 0; y < image.Height; y += 2) for (int x = 0; x < image.Width; x += 2) { var pixel = image.GetPixel(x, y); if (pixel.R < 110 && pixel.G > 150 && pixel.B < 140) green++; if (pixel.R > 200 && pixel.G is > 100 and < 180 && pixel.B < 100) orange++; } }
                captures.Add(new { phase, exit, greenSamplePixels = green, orangeSamplePixels = orange, sample, error = File.Exists(Path.ChangeExtension(png, ".error.txt")) ? File.ReadAllText(Path.ChangeExtension(png, ".error.txt")) : null, metadata = File.Exists(Path.ChangeExtension(png, ".json")) ? File.ReadAllText(Path.ChangeExtension(png, ".json")) : null });
                pixelChecksPassed &= exit == 0 && (phase == "adapter-color" ? orange > 15000 : green > 15000);
            }
            pids = Native.JobProcessIds(job);
        }
        catch (Exception e) { error = e.ToString(); pids = Native.JobProcessIds(job); }
        finally { monitoring.Cancel(); monitor.GetAwaiter().GetResult(); Native.CloseHandle(job); }
        Thread.Sleep(400);
        var report = new { rendering, samples, captures, gpu, error, pixelChecksPassed, inputChecksPassed, foregroundSamples, targetForegroundHits, foregroundEquality = Native.GetForegroundWindow() == before, cleanup = pids.All(pid => !CompatibilityVerification.Alive(pid)), pids, note = "Unmodified installed headful Chrome, disposable profile/local page, browser-adapted background startup. Native WGC full window capture; native WM_CHAR attempt; CDP input compatibility option. Foreground sampled every 10 ms, not proof against shorter changes. No global input, cursor movement or focus restoration. Not private-desktop isolation." };
        File.WriteAllText(Path.Combine(output, "offscreen-browser.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, error, pixelChecksPassed, inputChecksPassed, foregroundSamples, targetForegroundHits, report.cleanup }));
        return error == null && pixelChecksPassed && inputChecksPassed && targetForegroundHits == 0 && report.cleanup ? 0 : 1;
    }
}

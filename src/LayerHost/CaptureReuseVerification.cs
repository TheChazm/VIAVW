using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class CaptureReuseVerification
{
    static object Summary(List<double> samples) { var s = samples.Order().ToArray(); return new { count = s.Length, medianMs = s[s.Length / 2], p95Ms = s[Math.Min(s.Length - 1, (int)(s.Length * .95))], maxMs = s[^1] }; }
    static bool Marker(CaptureFrame frame, bool orange)
    {
        using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream); var p = image.GetPixel(100, 320);
        return orange ? p.R > 200 && p.G is > 100 and < 180 && p.B < 100 : p.R < 110 && p.G > 150 && p.B < 140;
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var before = Native.GetForegroundWindow();
        using var browser = new OffscreenBrowserSession(Path.Combine(output, "browser"), true, faultFixture: true);
        var cold = new List<double>(); var coldCpu = new List<double>(); var warm = new List<double>(); var warmCpu = new List<double>(); var observations = new List<object>(); bool pixels = true;
        Console.WriteLine("Capture reuse: four cold-device controls, then twenty warm color changes.");
        for (int i = 0; i < 4; i++) { var frame = browser.CaptureColdForComparison(); cold.Add(frame.CaptureMilliseconds); coldCpu.Add(browser.LastCaptureHelperCpuMilliseconds!.Value); pixels &= Marker(frame, false); if (i == 0) File.WriteAllBytes(Path.Combine(output, "cold.png"), frame.Png); }
        var first = browser.CaptureVisualOutput(); int worker = browser.CaptureWorkerProcessId; pixels &= Marker(first, false); int retries = 0;
        for (int i = 0; i < 20; i++)
        {
            browser.ClickElement("toggle"); bool orange = i % 2 == 0; var clock = Stopwatch.StartNew(); var frame = browser.CaptureImmediateForResearch();
            warm.Add(frame.CaptureMilliseconds); warmCpu.Add(browser.LastCaptureHelperCpuMilliseconds!.Value); int attempts = 1;
            while (!Marker(frame, orange) && attempts < 4) { retries++; attempts++; Thread.Sleep(20); frame = browser.CaptureImmediateForResearch(); }
            pixels &= Marker(frame, orange); observations.Add(new { i, attempts, confirmedMs = clock.Elapsed.TotalMilliseconds, diagnostics = browser.CaptureDiagnostics });
            if (i == 0) File.WriteAllBytes(Path.Combine(output, "warm-orange.png"), frame.Png);
        }
        var reused = browser.CaptureDiagnostics; bool oneDevice = reused.GetProperty("deviceCreations").GetInt32() == 1 && reused.GetProperty("stagingCreations").GetInt32() == 1 && browser.CaptureWorkerProcessId == worker;
        var settled = new List<double>(); int staleSettled = 0;
        for (int i = 0; i < 10; i++) { browser.ClickElement("toggle"); var frame = browser.CaptureVisualOutput(); settled.Add(frame.CaptureMilliseconds); if (!Marker(frame, i % 2 == 0)) staleSettled++; }
        Console.WriteLine("Capture reuse: idle session, resize, restart and contained capture stall.");
        Thread.Sleep(1000); using var helper = Process.GetProcessById(worker); double idleBefore = helper.TotalProcessorTime.TotalMilliseconds; var idle = Stopwatch.StartNew();
        var gpuTask = Task.Run(() => GpuCounters.Sample([worker])); Thread.Sleep(8000); idle.Stop(); helper.Refresh(); double idleCpuMs = helper.TotalProcessorTime.TotalMilliseconds - idleBefore; var idleGpu = gpuTask.GetAwaiter().GetResult();
        var helperMemory = new { workingSetMiB = helper.WorkingSet64 / 1048576.0, privateMiB = helper.PrivateMemorySize64 / 1048576.0 };
        browser.ResizeForTest(900, 700); Thread.Sleep(300); var resized = browser.CaptureVisualOutput(); var resizeDiagnostics = browser.CaptureDiagnostics;
        bool resize = resized.Width == 900 && resized.Height == 700 && Marker(resized, false) && resizeDiagnostics.GetProperty("deviceCreations").GetInt32() == 1 && resizeDiagnostics.GetProperty("stagingCreations").GetInt32() == 2;
        browser.RestartCaptureWorker(); bool oldWorkerGone = !CompatibilityVerification.Alive(worker); var restarted = browser.CaptureVisualOutput(); int nextWorker = browser.CaptureWorkerProcessId;
        browser.TakeHumanControl(); browser.ClickElement("editor", ControlOwner.Human); browser.SendKeyboardInput("M4_PRESERVED", ControlOwner.Human);
        string beforeStall = browser.Inspect("editor.value").GetString()!; bool timeout = false; var stall = Stopwatch.StartNew();
        try { browser.StallCaptureWorkerForTest(); } catch (TimeoutException) { timeout = true; } stall.Stop();
        bool sourcePreserved = CompatibilityVerification.Alive(browser.ApplicationProcessId) && browser.Owner == ControlOwner.Human && browser.Inspect("editor.value").GetString() == beforeStall && !CompatibilityVerification.Alive(nextWorker);
        var recovered = browser.CaptureVisualOutput(); bool recovery = Marker(recovered, false) && browser.CaptureWorkerProcessId != nextWorker;
        browser.ReturnToAutomation(); browser.ClickElement("toggle"); var finalFrame = browser.CaptureVisualOutput(); File.WriteAllBytes(Path.Combine(output, "after-recovery.png"), finalFrame.Png);
        var ids = browser.GetProcessIds(); browser.Dispose(); Thread.Sleep(300); bool cleanup = ids.All(pid => !CompatibilityVerification.Alive(pid));
        var report = new { pixels, oneDevice, cold = Summary(cold), coldCpu = Summary(coldCpu), firstWarmMs = first.CaptureMilliseconds, warm = Summary(warm), warmCpu = Summary(warmCpu), retries, observations, settled = Summary(settled), staleSettled, reused, idleIntervalMs = idle.Elapsed.TotalMilliseconds, idleCpuMs, idleMachinePercent = idleCpuMs / idle.Elapsed.TotalMilliseconds * 100 / Environment.ProcessorCount, idleGpu, helperMemory, resize, resizeDiagnostics, oldWorkerGone, timeout, stallMs = stall.Elapsed.TotalMilliseconds, sourcePreserved, recovery, cleanup, foregroundEquality = Native.GetForegroundWindow() == before, note = "Same unmodified Chrome instance and WebGL source for cold/warm comparison. Capture sessions and pools close after each request; device/item/readback/factory persist. Software capture-stall fixture is enabled only in this test. Immediate source logical changes can precede compositor frames; retries are recorded separately. Settled mode uses a browser-specific animation-frame scheduling barrier with a timer fallback." };
        File.WriteAllText(Path.Combine(output, "capture-reuse.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, pixels, oneDevice, resize, timeout, sourcePreserved, recovery, cleanup, cold = Summary(cold), warm = Summary(warm), warmCpu = Summary(warmCpu), retries }));
        return pixels && oneDevice && staleSettled == 0 && resize && timeout && sourcePreserved && recovery && cleanup ? 0 : 1;
    }
}

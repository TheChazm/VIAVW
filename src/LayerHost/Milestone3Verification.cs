using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class Milestone3Verification
{
    static Dictionary<int, double> Cpu(IEnumerable<int> ids)
    {
        var result = new Dictionary<int, double>();
        foreach (int id in ids.Distinct()) try { using var p = Process.GetProcessById(id); result[id] = p.TotalProcessorTime.TotalMilliseconds; } catch (ArgumentException) { }
        return result;
    }
    static object CpuChange(Dictionary<int, double> before, double elapsed)
    {
        var after = Cpu(before.Keys);
        return before.Select(pair => new { pid = pair.Key, cpuMs = after.TryGetValue(pair.Key, out double value) ? value - pair.Value : (double?)null, oneCorePercent = after.TryGetValue(pair.Key, out value) ? (value - pair.Value) / elapsed * 100 : (double?)null, machinePercent = after.TryGetValue(pair.Key, out value) ? (value - pair.Value) / elapsed * 100 / Environment.ProcessorCount : (double?)null }).ToArray();
    }
    static object Times(List<double> samples)
    {
        var sorted = samples.Order().ToArray(); return new { count = sorted.Length, medianMs = sorted[sorted.Length / 2], p95Ms = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))], maxMs = sorted[^1] };
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var beforeForeground = Native.GetForegroundWindow(); var cleanupIds = new List<int>();
        using var manager = new LayerManager(); using var a = manager.CreateLayer(); using var b = manager.CreateLayer();
        string folderA = Path.Combine(output, "a"), folderB = Path.Combine(output, "b"); Directory.CreateDirectory(folderA); Directory.CreateDirectory(folderB);
        string wpf = Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe");
        a.LaunchApplication(wpf, workingDirectory: folderA); b.LaunchApplication(wpf, workingDirectory: folderB);
        var rootA = CompatibilityVerification.WaitRoot(a, "Disposable WPF application"); var rootB = CompatibilityVerification.WaitRoot(b, "Disposable WPF application");
        bool foreignRejected = false;
        try { a.SendQueueStateChord(rootB.Handle, [0x11, 0x4B]); } catch (LayerOperationException) { foreignRejected = true; }
        var held = Task.Run(() => a.SendQueueStateChord(rootA.Handle, [0x11, 0x4B], posted: true));
        Thread.Sleep(45); b.SendKey(rootB.Handle, 0x4B); held.GetAwaiter().GetResult(); Thread.Sleep(300);
        var isolatedA = CompatibilityVerification.Read(folderA); var isolatedB = CompatibilityVerification.Read(folderB);
        bool independent = isolatedA.GetProperty("shortcuts").GetInt32() == 1 && isolatedB.GetProperty("shortcuts").GetInt32() == 0 && !isolatedB.GetProperty("ctrlState").GetBoolean();
        Console.WriteLine("M3: measuring five seconds of private-desktop shortcut activity.");
        var ids = a.GetProcessIds().Concat(b.GetProcessIds()).Append(Environment.ProcessId).ToArray(); var cpu = Cpu(ids); var watch = Stopwatch.StartNew(); var keyTimes = new List<double>(); int expected = 1;
        while (watch.ElapsedMilliseconds < 5000) { var clock = Stopwatch.StartNew(); a.SendQueueStateChord(rootA.Handle, [0x11, 0x4B]); keyTimes.Add(clock.Elapsed.TotalMilliseconds); expected++; Thread.Sleep(20); }
        watch.Stop(); double shortcutIntervalMs = watch.Elapsed.TotalMilliseconds; var keyCpu = CpuChange(cpu, shortcutIntervalMs); Thread.Sleep(300);
        int actual = CompatibilityVerification.Read(folderA).GetProperty("shortcuts").GetInt32();
        a.SendKey(rootA.Handle, 0x25); Thread.Sleep(250); bool modifierReleased = !CompatibilityVerification.Read(folderA).GetProperty("ctrlState").GetBoolean();
        cleanupIds.AddRange(a.GetProcessIds()); cleanupIds.AddRange(b.GetProcessIds()); a.Dispose(); b.Dispose();
        Console.WriteLine("M3: measuring offscreen GPU browser idle, input and on-demand native captures.");
        using var browser = new OffscreenBrowserSession(Path.Combine(output, "browser"));
        browser.ClickElement("editor"); browser.SendKeyboardInput("M3_BENCHMARK"); browser.CaptureVisualOutput();
        ids = browser.GetProcessIds().Append(Environment.ProcessId).ToArray(); cpu = Cpu(ids); watch.Restart();
        var idleGpuTask = Task.Run(() => GpuCounters.Sample(ids)); Thread.Sleep(5000); watch.Stop(); double idleIntervalMs = watch.Elapsed.TotalMilliseconds; var idleCpu = CpuChange(cpu, idleIntervalMs); var idleGpu = idleGpuTask.GetAwaiter().GetResult();
        cpu = Cpu(ids); watch.Restart(); var captureTimes = new List<double>(); var helperCpu = new List<double>();
        var activeGpuTask = Task.Run(() => GpuCounters.Sample(ids));
        while (watch.ElapsedMilliseconds < 5000)
        {
            browser.ClickElement("toggle"); var frame = browser.CaptureVisualOutput(); captureTimes.Add(frame.CaptureMilliseconds);
            if (browser.LastCaptureHelperCpuMilliseconds is double value) helperCpu.Add(value);
            if (captureTimes.Count == 1) File.WriteAllBytes(Path.Combine(output, "gpu-active.png"), frame.Png);
            Thread.Sleep(50);
        }
        watch.Stop(); double activeIntervalMs = watch.Elapsed.TotalMilliseconds; var activeCpu = CpuChange(cpu, activeIntervalMs); var activeGpu = activeGpuTask.GetAwaiter().GetResult();
        var memory = browser.GetProcessIds().Append(Environment.ProcessId).Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
        var state = browser.Inspect("({text:editor.value,...observations})"); cleanupIds.AddRange(browser.GetProcessIds()); browser.Dispose();
        Thread.Sleep(400); bool cleanup = cleanupIds.All(pid => !CompatibilityVerification.Alive(pid));
        var report = new { foreignRejected, independent, isolatedA, isolatedB, modifierReleased, shortcutExpected = expected, shortcutActual = actual, shortcutTimes = Times(keyTimes), shortcutIntervalMs, keyCpu, logicalProcessors = Environment.ProcessorCount, idleIntervalMs, idleCpu, idleGpu, activeIntervalMs, activeCpu, activeGpu, captureTimes = Times(captureTimes), helperCpuMs = helperCpu.Sum(), helperCpuTimes = Times(helperCpu), memory, state, cleanup, foregroundEquality = Native.GetForegroundWindow() == beforeForeground, note = "Five-second phases: bounded activity measurements, not an endurance benchmark. CPU percentages separate one core from whole machine. WGC helper startup/device/encoding CPU measured with retained process handles; helper engines are not included in Chrome GPU samples. Working sets include shared pages. Diagnostic sampling overhead is included in the manager. Normal browser occlusion throttling disabled only for this disposable instance." };
        File.WriteAllText(Path.Combine(output, "milestone3-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, foreignRejected, independent, modifierReleased, expected, actual, cleanup }));
        return foreignRejected && independent && modifierReleased && expected == actual && cleanup ? 0 : 1;
    }
}

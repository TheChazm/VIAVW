using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class PrivateGpuPerformance
{
    static Dictionary<int, double> Cpu(IEnumerable<int> ids) => ids.Distinct().ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; });
    static object Change(Dictionary<int, double> before, double elapsed) => before.Select(pair => { using var p = Process.GetProcessById(pair.Key); double delta = p.TotalProcessorTime.TotalMilliseconds - pair.Value; return new { pid = pair.Key, name = p.ProcessName, cpuMs = delta, machinePercent = delta / elapsed * 100 / Environment.ProcessorCount }; }).ToArray();
    static object Summary(List<double> samples) { var s = samples.Order().ToArray(); return new { count = s.Length, medianMs = s[s.Length / 2], p95Ms = s[Math.Min(s.Length - 1, (int)(s.Length * .95))] }; }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); using var manager = new LayerManager(); var subjects = new List<(string Name, ApplicationLayer Layer, WindowInfo Root, string Folder)>();
        var beforeForeground = Native.GetForegroundWindow();
        foreach (string name in new[] { "Quick", "D3D" })
        {
            string folder = Path.Combine(output, name); Directory.CreateDirectory(folder); var layer = manager.CreateLayer();
            layer.LaunchApplication(name == "Quick" ? "C:/Python314/python.exe" : Path.Combine(project, "artifacts/native-build/D3DProbe.exe"), name == "Quick" ? [Path.Combine(project, "experiments/quick_probe.py")] : [], folder);
            subjects.Add((name, layer, CompatibilityVerification.WaitRoot(layer, name == "Quick" ? "Disposable Qt Quick application" : "Disposable Direct3D application"), folder));
        }
        int[] ids = subjects.SelectMany(s => s.Layer.GetProcessIds()).Append(Environment.ProcessId).ToArray(); Thread.Sleep(2000); var cpu = Cpu(ids); var watch = Stopwatch.StartNew(); var idleGpuTask = Task.Run(() => GpuCounters.Sample(ids)); Thread.Sleep(5000); watch.Stop(); double idleMs = watch.Elapsed.TotalMilliseconds; var idleCpu = Change(cpu, idleMs); var idleGpu = idleGpuTask.GetAwaiter().GetResult();
        Console.WriteLine("Private GPU performance: two simultaneous native-input/full-content GPU layers.");
        cpu = Cpu(ids); watch.Restart(); var timings = subjects.ToDictionary(s => s.Name, _ => new List<double>()); var roundTrips = subjects.ToDictionary(s => s.Name, _ => new List<double>()); var activeGpuTask = Task.Run(() => GpuCounters.Sample(ids)); int rounds = 0;
        while (watch.ElapsedMilliseconds < 5000)
        {
            foreach (var subject in subjects)
            {
                subject.Layer.SendMouseInput(subject.Root.Handle, subject.Name == "Quick" ? 80 : 310, subject.Name == "Quick" ? 150 : 250);
                Thread.Sleep(35); var clock = Stopwatch.StartNew(); var frame = subject.Layer.CaptureVisualOutput(subject.Root.Handle, CaptureBackend.PrintWindowFullContent); roundTrips[subject.Name].Add(clock.Elapsed.TotalMilliseconds); timings[subject.Name].Add(frame.CaptureMilliseconds);
                if (rounds == 0) File.WriteAllBytes(Path.Combine(subject.Folder, "active.png"), frame.Png);
            }
            rounds++;
        }
        watch.Stop(); double activeMs = watch.Elapsed.TotalMilliseconds; var activeCpu = Change(cpu, activeMs); var activeGpu = activeGpuTask.GetAwaiter().GetResult();
        var memory = ids.Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
        var states = subjects.Select(s => new { s.Name, state = CompatibilityVerification.Read(s.Folder) }).ToArray(); bool independent = states.All(s => s.state.GetProperty("clicks").GetInt32() == rounds);
        var shutdown = subjects.Select(s => new { s.Name, graceful = s.Layer.CloseGracefully(TimeSpan.FromSeconds(2)) }).ToArray(); manager.Dispose(); Thread.Sleep(200); bool cleanup = ids.Where(id => id != Environment.ProcessId).All(id => !CompatibilityVerification.Alive(id));
        var report = new { logicalProcessors = Environment.ProcessorCount, rounds, independent, idleMs, idleCpu, idleGpu, activeMs, activeCpu, activeGpu, captures = timings.ToDictionary(k => k.Key, k => Summary(k.Value)), roundTrips = roundTrips.ToDictionary(k => k.Key, k => Summary(k.Value)), memory, states, shutdown, cleanup, foregroundEquality = Native.GetForegroundWindow() == beforeForeground, note = "Two private GPU applications, native message input and full-content native capture. No grab adapters, software rendering override or resident capture device/helper. Diagnostic and fixture telemetry costs included. GPU engine values remain separate; working sets count shared pages." };
        File.WriteAllText(Path.Combine(output, "private-gpu-performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, rounds, independent, cleanup, report.captures })); return independent && cleanup ? 0 : 1;
    }
}

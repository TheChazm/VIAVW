using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LayerHost;

internal static class GpuCounters
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string? source, nuint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(nint query, string path, nuint user, out nint counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(nint query);
    internal static object Sample(int[] owned)
    {
        nint query = 0;
        try
        {
            void Check(uint code) { if (code != 0) throw new InvalidOperationException("PDH status 0x" + code.ToString("X8")); }
            Check(PdhOpenQueryW(null, 0, out query)); Check(PdhAddEnglishCounterW(query, "\\GPU Engine(*)\\Utilization Percentage", 0, out nint counter));
            Check(PdhCollectQueryData(query)); Thread.Sleep(1500); Check(PdhCollectQueryData(query));
            uint size = 0; uint code = PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out _, 0);
            if (code != 0x800007D2) Check(code);
            nint buffer = Marshal.AllocHGlobal((int)size);
            var values = new List<object>();
            try
            {
                Check(PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out uint count, buffer));
                for (int i = 0; i < count; i++)
                {
                    nint item = buffer + i * 24; string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item))!;
                    var match = Regex.Match(name, @"pid_(\d+)_");
                    if (!match.Success || !owned.Contains(int.Parse(match.Groups[1].Value))) continue;
                    int status = Marshal.ReadInt32(item, 8); if (status is not (0 or 1)) continue;
                    double percentage = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    values.Add(new { pid = int.Parse(match.Groups[1].Value), engine = name, percentage });
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return new { supported = true, values, note = "One 1.5-second GPU-engine counter interval for owned PIDs only. Engine percentages are not summed into a whole-GPU utilization claim." };
        }
        catch (Exception e) { return new { supported = false, error = e.Message }; }
        finally { if (query != 0) PdhCloseQuery(query); }
    }
}
internal static class PerformanceVerification
{
    sealed record Subject(string Name, ApplicationLayer Layer, WindowInfo Root, long TextTarget, CaptureBackend Backend, string Directory, double StartupMs);
    static Dictionary<int, double> Cpu(IEnumerable<int> ids) => ids.Where(CompatibilityVerification.Alive).ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; });
    static object CpuResult(Dictionary<int, double> before, double intervalMs) => before.Keys.Where(CompatibilityVerification.Alive).Select(id => { using var p = Process.GetProcessById(id); double delta = p.TotalProcessorTime.TotalMilliseconds - before[id]; return new { pid = id, name = p.ProcessName, cpuMilliseconds = delta, percentOfOneCore = 100 * delta / intervalMs, percentOfMachine = 100 * delta / intervalMs / Environment.ProcessorCount }; }).ToArray();
    static object Summary(List<double> values) { var ordered = values.Order().ToArray(); return new { samples = ordered.Length, min = ordered[0], median = ordered[ordered.Length / 2], p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], max = ordered[^1] }; }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var manager = new LayerManager(); var subjects = new List<Subject>();
        void Add(string name, string exe, string title, CaptureBackend backend, params string[] args)
        {
            string directory = Path.Combine(output, name); Directory.CreateDirectory(directory);
            var watch = Stopwatch.StartNew(); var layer = manager.CreateLayer(); layer.LaunchApplication(exe, args, directory); var root = CompatibilityVerification.WaitRoot(layer, title);
            var editor = layer.GetWindows().FirstOrDefault(w => w.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase));
            subjects.Add(new(name, layer, root, editor?.Handle ?? root.Handle, backend, directory, watch.Elapsed.TotalMilliseconds));
        }
        Add("WinForms", Program.FindProbe(), "Disposable application", CaptureBackend.PrintWindow);
        Add("WPF", Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), "Disposable WPF application", CaptureBackend.CooperativeFixture, "--enable-grab-adapter");
        Add("Quick", @"C:\Python314\python.exe", "Disposable Qt Quick application", CaptureBackend.CooperativeFixture, Path.Combine(project, "experiments/quick_probe.py"), "--enable-grab-adapter");
        Add("D3D", Path.Combine(project, "artifacts/native-build/D3DProbe.exe"), "Disposable Direct3D application", CaptureBackend.PrintWindow);
        Console.WriteLine("Performance: four concurrent layers, runtime warmup then 30-second idle interval.");
        Thread.Sleep(4000);
        int[] ids = new[] { Environment.ProcessId }.Concat(subjects.SelectMany(s => s.Layer.GetProcessIds())).Distinct().ToArray();
        var idleBefore = Cpu(ids); var idleWatch = Stopwatch.StartNew(); var idleGpuTask = Task.Run(() => GpuCounters.Sample(ids)); Thread.Sleep(30000); idleWatch.Stop();
        var idleCpu = CpuResult(idleBefore, idleWatch.Elapsed.TotalMilliseconds); var idleGpu = idleGpuTask.GetAwaiter().GetResult();
        Console.WriteLine("Performance: six seconds of targeted text input and on-demand inspection.");
        var activityBefore = Cpu(ids); var activity = Stopwatch.StartNew(); var inputTimes = new List<double>(); var captureTimes = subjects.ToDictionary(s => s.Name, _ => new List<double>());
        var gpuTask = Task.Run(() => GpuCounters.Sample(ids)); int batches = 0;
        while (activity.ElapsedMilliseconds < 6000)
        {
            foreach (var subject in subjects)
            {
                var clock = Stopwatch.StartNew(); subject.Layer.SendKeyboardInput(subject.TextTarget, "x"); inputTimes.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart(); var frame = subject.Layer.CaptureVisualOutput(subject.Root.Handle, subject.Backend); captureTimes[subject.Name].Add(clock.Elapsed.TotalMilliseconds);
                if (batches == 0) CompatibilityVerification.PixelEvidence(frame, Path.Combine(subject.Directory, "inspected.png"), subject.Name == "D3D" ? 310 : subject.Name == "WinForms" ? 50 : 490, subject.Name == "D3D" ? 250 : subject.Name == "WinForms" ? 80 : 160, false);
            }
            batches++; Thread.Sleep(25);
        }
        activity.Stop(); var activeCpu = CpuResult(activityBefore, activity.Elapsed.TotalMilliseconds); var activeGpu = gpuTask.GetAwaiter().GetResult();
        var finalText = subjects.Select(s => new { s.Name, text = s.Name == "WinForms" ? s.Layer.GetWindows().First(w => w.Handle == s.TextTarget).Text : CompatibilityVerification.Read(s.Directory).TryGetProperty("text", out var v) ? v.GetString() : CompatibilityVerification.Read(s.Directory).GetProperty("textValue").GetString() }).ToArray();
        var memory = new[] { Environment.ProcessId }.Concat(subjects.SelectMany(s => s.Layer.GetProcessIds())).Distinct().Where(CompatibilityVerification.Alive).Select(id => { using var p = Process.GetProcessById(id); var subject = subjects.FirstOrDefault(s => s.Layer.GetProcessIds().Contains(id)); return new { pid = id, name = p.ProcessName, layer = subject?.Name ?? "manager", role = subject == null ? "manager" : subject.Layer.GetInfrastructureProcessIds().Contains(id) ? "wrapper infrastructure" : "application", workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
        var shutdowns = new List<object>();
        foreach (var subject in subjects) { var watch = Stopwatch.StartNew(); bool graceful = subject.Layer.CloseGracefully(TimeSpan.FromSeconds(2)); if (!graceful) subject.Layer.Dispose(); shutdowns.Add(new { subject.Name, graceful, milliseconds = watch.Elapsed.TotalMilliseconds }); }
        var report = new { logicalProcessors = Environment.ProcessorCount, startup = subjects.Select(s => new { s.Name, s.StartupMs }), idleIntervalMs = idleWatch.Elapsed.TotalMilliseconds, idleCpu, idleGpu, activityIntervalMs = activity.Elapsed.TotalMilliseconds, activeCpu, activeGpu, input = Summary(inputTimes), captures = captureTimes.ToDictionary(kv => kv.Key, kv => Summary(kv.Value)), batches, finalText, memory, shutdowns, note = "Diagnostic fixture telemetry costs are included in application CPU. GPU samples are short engine intervals. D3D PrintWindow is deliberately timed despite incorrect pixels; do not interpret that timing as useful frame acquisition. This is a bounded 30-second idle / 6-second activity experiment, not an endurance guarantee." };
        File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Performance evidence: " + output); return 0;
    }
}

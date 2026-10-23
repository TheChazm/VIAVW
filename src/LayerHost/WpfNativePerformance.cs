using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class WpfNativePerformance
{
    static Dictionary<int, double> Cpu(int[] ids) => ids.ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; });
    static object Summary(List<double> samples) { var sorted = samples.Order().ToArray(); return new { count = sorted.Length, median = sorted[sorted.Length / 2], p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * .95) - 1)], minimum = sorted[0], maximum = sorted[^1] }; }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var power = new DisplayPowerObserver();
        var foreground = Native.GetForegroundWindow(); uint? inputTickBefore = Native.LastInputTick(); var results = new List<object>(); bool passed = true; using var manager = new LayerManager();
        foreach (string variant in new[] { "software", "layered" })
        {
            string folder = Path.Combine(output, variant); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer();
            layer.LaunchApplication(Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), ["--" + variant], folder);
            var root = CompatibilityVerification.WaitRoot(layer, "Disposable WPF application"); Thread.Sleep(500); int[] ids = layer.GetProcessIds().Append(Environment.ProcessId).ToArray();
            var idleBefore = Cpu(ids); var idleWatch = Stopwatch.StartNew(); var idleGpuTask = Task.Run(() => GpuCounters.Sample(ids)); Thread.Sleep(5000); idleWatch.Stop(); var idleAfter = Cpu(ids); double idleCpuMs = ids.Sum(id => idleAfter[id] - idleBefore[id]); var idleGpu = idleGpuTask.GetAwaiter().GetResult();
            var activeBefore = Cpu(ids); var activeWatch = Stopwatch.StartNew(); var activeGpuTask = Task.Run(() => GpuCounters.Sample(ids)); var modes = new List<object>(); int clicks = 0;
            foreach (string mode in variant == "software" ? new[] { "pw1", "pw2", "pw3" } : new[] { "pw2", "pw3" })
            {
                var captureTimes = new List<double>(); var roundTrips = new List<double>(); var samples = new List<object>(); int incorrectFirstFrames = 0, retries = 0; bool correct = true;
                for (int i = 0; i < 12; i++)
                {
                    clicks++; layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 80, 150), new(MouseMessage.LeftDown, 80, 150, 1), new(MouseMessage.LeftUp, 80, 150)], true);
                    var observe = Stopwatch.StartNew(); while (CompatibilityVerification.Read(folder).GetProperty("clicks").GetInt32() != clicks && observe.ElapsedMilliseconds < 2000) Thread.Sleep(25);
                    bool expectedOrange = clicks % 2 == 1;
                    bool Changed(CaptureFrame frame) { using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream); var p = image.GetPixel(490, variant == "software" && mode is "pw2" ? 190 : 160); return expectedOrange ? p.R > 200 && p.G is > 100 and < 180 && p.B < 100 : p.R < 110 && p.G > 150 && p.B < 140; }
                    var powerBefore = power.Snapshot(); var rpc = Stopwatch.StartNew(); var frame = layer.CaptureResearch(root.Handle, mode); rpc.Stop(); captureTimes.Add(frame.CaptureMilliseconds); roundTrips.Add(rpc.Elapsed.TotalMilliseconds);
                    bool updated = Changed(frame); if (!updated) incorrectFirstFrames++;
                    samples.Add(new { i, captureMs = frame.CaptureMilliseconds, nativeMs = frame.NativeRenderingMilliseconds, encodingMs = frame.EncodingMilliseconds, rpcMs = rpc.Elapsed.TotalMilliseconds, firstFrameUpdated = updated, powerBefore, powerAfter = power.Snapshot() });
                    var until = Stopwatch.StartNew(); while (!updated && until.ElapsedMilliseconds < 1500) { Thread.Sleep(25); frame = layer.CaptureResearch(root.Handle, mode); retries++; updated = Changed(frame); }
                    correct &= updated; if (i is 0 or 11) File.WriteAllBytes(Path.Combine(folder, mode + "-" + i + ".png"), frame.Png);
                }
                passed &= correct; modes.Add(new { mode, captureMs = Summary(captureTimes), rpcMs = Summary(roundTrips), samples, incorrectFirstFrames, retries, correct });
            }
            activeWatch.Stop(); var activeAfter = Cpu(ids); double activeCpuMs = ids.Sum(id => activeAfter[id] - activeBefore[id]); var activeGpu = activeGpuTask.GetAwaiter().GetResult();
            var memory = ids.Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
            var state = CompatibilityVerification.Read(folder); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); if (!graceful) layer.Dispose(); Thread.Sleep(250); bool cleanup = ids.Where(id => id != Environment.ProcessId).All(id => !CompatibilityVerification.Alive(id)); passed &= cleanup;
            results.Add(new { variant, modes, state, idleMs = idleWatch.Elapsed.TotalMilliseconds, idleCpuMs, idleWholeMachinePercent = idleCpuMs / idleWatch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, idleGpu, activeMs = activeWatch.Elapsed.TotalMilliseconds, activeCpuMs, activeWholeMachinePercent = activeCpuMs / activeWatch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, activeGpu, memory, graceful, cleanup });
            Console.WriteLine(JsonSerializer.Serialize(new { variant, modes = modes.Select(mode => { var item = JsonSerializer.SerializeToElement(mode); return new { mode = item.GetProperty("mode").GetString(), captureMs = item.GetProperty("captureMs"), rpcMs = item.GetProperty("rpcMs"), incorrectFirstFrames = item.GetProperty("incorrectFirstFrames"), retries = item.GetProperty("retries"), correct = item.GetProperty("correct") }; }), cleanup }));
        }
        var powerEvents = power.Events(); power.Dispose();
        File.WriteAllText(Path.Combine(output, "wpf-native-performance.json"), JsonSerializer.Serialize(new { passed, logicalProcessors = Environment.ProcessorCount, results, powerEvents, powerObserverStopped = power.ThreadStopped, inputTickBefore, inputTickAfter = Native.LastInputTick(), foregroundEquality = foreground == Native.GetForegroundWindow(), note = "Separate private WPF fixture software/layered variants, native capture only. Twelve changed-frame controls per mode. Owned CPU includes test diagnostics and passive observation; GPU engine counters are separate short samples. Passive session display: 0 off, 1 on, 2 dim; presence: 0 present, 1 not present, 2 inactive. Null means no notification yet. Native/PNG timing split; no display wake or power policy changes. RenderCapability tier or Default preference alone does not prove hardware execution." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

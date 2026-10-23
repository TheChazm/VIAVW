using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class NotificationActivityResearch
{
    internal record Result(bool IdleAnimationPaused, List<double> InputMilliseconds, List<(string Name, object Value)> Measurements);
    internal static Result Run(ApplicationLayer layer, long root, Func<JsonElement> state, Func<Func<JsonElement, bool>, JsonElement> wait, int settle)
    {
        var times = new List<double>(); var measurements = new List<(string, object)>(); Thread.Sleep(1500);
        int[] ids = layer.GetProcessIds(); Dictionary<int, double> Cpu() => ids.ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
        var initial = state(); var beforeCpu = Cpu(); var gpu = Task.Run(() => GpuCounters.Sample(ids)); var interval = Stopwatch.StartNew(); int commands = 0;
        while (interval.ElapsedMilliseconds < 4000)
        {
            string value = "NOTIFICATION_ACTIVITY_" + commands++; var packet = Stopwatch.StartNew();
            var result = layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: value)], settleMilliseconds: settle); times.Add(packet.Elapsed.TotalMilliseconds);
            wait(observed => observed.GetProperty("otherText").GetString() == value && observed.GetProperty("visibility").GetString() == "hidden");
            if (!result.HiddenAfter || layer.GetWindows().First(window => window.Handle == root).Visible) throw new InvalidOperationException("Notification input source became visible."); Thread.Sleep(100);
        }
        double elapsed = interval.Elapsed.TotalMilliseconds; var afterCpu = Cpu(); var last = state(); double cpu = beforeCpu.Sum(pair => afterCpu[pair.Key] - pair.Value);
        measurements.Add(("notification-input-activity", new { commands, elapsedMs = elapsed, cpuMs = cpu, wholeMachinePercent = cpu / elapsed / Environment.ProcessorCount * 100, gpu = gpu.GetAwaiter().GetResult(), initial, last }));
        Thread.Sleep(1500); initial = state(); beforeCpu = Cpu(); gpu = Task.Run(() => GpuCounters.Sample(ids)); interval.Restart(); Thread.Sleep(2500);
        elapsed = interval.Elapsed.TotalMilliseconds; afterCpu = Cpu(); last = state(); cpu = beforeCpu.Sum(pair => afterCpu[pair.Key] - pair.Value);
        bool paused = last.GetProperty("visibility").GetString() == "hidden" && last.GetProperty("frames").GetInt32() == initial.GetProperty("frames").GetInt32();
        measurements.Add(("notification-post-activity-idle", new { elapsedMs = elapsed, cpuMs = cpu, wholeMachinePercent = cpu / elapsed / Environment.ProcessorCount * 100, gpu = gpu.GetAwaiter().GetResult(), initial, last }));
        return new(paused, times, measurements);
    }
}

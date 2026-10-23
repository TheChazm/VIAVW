using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class InspectionResourceResearch
{
    [DllImport("user32.dll", SetLastError = true)] static extern uint GetGuiResources(nint process, uint flags);
    static object WorkerResources(int pid)
    {
        nint handle = Native.OpenProcess(0x1000, false, (uint)pid); Native.Check(handle != 0, "Observe owned worker GUI resources");
        try
        {
            Marshal.SetLastPInvokeError(0); uint gdi = GetGuiResources(handle, 0); int gdiError = Marshal.GetLastPInvokeError();
            Marshal.SetLastPInvokeError(0); uint user = GetGuiResources(handle, 1); int userError = Marshal.GetLastPInvokeError();
            using var process = Process.GetProcessById(pid);
            return new { pid, gdi, gdiError, user, userError, workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0 };
        }
        finally { Native.CloseHandle(handle); }
    }
    internal static (bool AllFresh, List<JsonElement> Phases) Run(ApplicationLayer layer, long root, Func<JsonElement> state, Func<Func<JsonElement, bool>, JsonElement> wait)
    {
        var phases = new List<JsonElement>(); bool allFresh = true; int phase = 0;
        foreach (int settle in new[] { 30, 150, 150, 30 })
        {
            Thread.Sleep(400); int[] ids = layer.GetProcessIds();
            Dictionary<int, double> Cpu() => ids.ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
            var beforeCpu = Cpu(); var workerBefore = WorkerResources(layer.WorkerProcessId); var initial = state(); var gpu = Task.Run(() => GpuCounters.Sample(ids)); var interval = Stopwatch.StartNew(); var samples = new List<object>(); bool phaseFresh = true;
            for (int index = 0; index < 8; index++)
            {
                int changes = state().GetProperty("changes").GetInt32(); string text = "RESOURCE_" + phase + "_" + index;
                layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: text), new(PresentedInputKind.KeyChord, Keys: [0x9]), new(PresentedInputKind.KeyChord, Keys: [0x20]), new(PresentedInputKind.KeyChord, Keys: [0x10, 0x9])]);
                var before = wait(observed => observed.GetProperty("text").GetString() == text && observed.GetProperty("changes").GetInt32() == changes + 1 && observed.GetProperty("visibility").GetString() == "hidden");
                var watch = Stopwatch.StartNew(); var frame = layer.InspectHiddenWindow(root, true, settle); double ms = watch.Elapsed.TotalMilliseconds;
                var after = wait(observed => observed.GetProperty("visibility").GetString() == "hidden"); var pixels = PrivateBrowserVisibilityVerification.Pixels(frame, after); int encoded = pixels.GetProperty("encodedFrame").GetInt32();
                bool fresh = pixels.GetProperty("colorCorrect").GetBoolean() && encoded > before.GetProperty("frames").GetInt32() && encoded <= after.GetProperty("frames").GetInt32(); phaseFresh &= fresh;
                if (layer.GetWindows().First(window => window.Handle == root).Visible) throw new InvalidOperationException("Resource test did not restore hidden source.");
                samples.Add(new { ms, fresh, encoded, framesBefore = before.GetProperty("frames"), framesAfter = after.GetProperty("frames") });
            }
            double elapsed = interval.Elapsed.TotalMilliseconds; var afterCpu = Cpu(); double cpuMs = beforeCpu.Sum(pair => afterCpu[pair.Key] - pair.Value); var workerAfter = WorkerResources(layer.WorkerProcessId); var final = state();
            phases.Add(JsonSerializer.SerializeToElement(new { phase = phase++, settle, captures = 8, allFresh = phaseFresh, elapsedMs = elapsed, ownedCpuMs = cpuMs, wholeMachinePercent = cpuMs / elapsed / Environment.ProcessorCount * 100, workerBefore, workerAfter, framesBefore = initial.GetProperty("frames"), framesAfter = final.GetProperty("frames"), gpu = gpu.GetAwaiter().GetResult(), samples, note = "Fixed-count input plus inspection, paired ABBA order. GPU sampling may extend past the shorter activity block; per-engine observations are not whole-device utilization. Process CPU excludes supervisor and includes passive page instrumentation. GUI-resource zero counts include last-error values to distinguish unsupported/failure." }));
            allFresh &= phaseFresh;
        }
        return (allFresh, phases);
    }
}

using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class GpuBridgeVerification
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SuspendThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetProcessIdOfThread(nint thread);
    internal static void PrepareOutput(string output)
    {
        string artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(output).StartsWith(artifacts, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Research evidence must stay under project artifacts.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new ArgumentException("Use a fresh research evidence directory.");
        Directory.CreateDirectory(output);
    }
    internal static int Run(string output)
    {
        PrepareOutput(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); GpuBridgeResearchSession? bridge = null;
        var checks = new List<object>(); var events = new List<object>(); bool passed = true, graceful = false; string? failure = null, mappingName = null; int[] owned = []; long root = 0; int pid = 0;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        JsonElement State() => CompatibilityVerification.Read(output);
        JsonElement Wait(Func<JsonElement, bool> condition)
        {
            var watch = Stopwatch.StartNew(); JsonElement state; do { state = State(); if (condition(state)) return state; Thread.Sleep(20); } while (watch.ElapsedMilliseconds < 4000); throw new TimeoutException("GPU bridge source observation: " + state);
        }
        object[] Processes() => layer.GetProcessIds().Append(Environment.ProcessId).Distinct().Select(id => { using var process = Process.GetProcessById(id); return (object)new { pid = id, name = process.ProcessName, cpuMs = process.TotalProcessorTime.TotalMilliseconds, workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
        Dictionary<int, double> Cpu() => layer.GetProcessIds().Append(Environment.ProcessId).Distinct().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
        object Cost(Dictionary<int, double> before, double ms) => before.Select(pair => { using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value; return new { pid = pair.Key, name = process.ProcessName, cpuMs = cpu, oneCorePercent = cpu / ms * 100, machinePercent = cpu / ms * 100 / Environment.ProcessorCount }; }).ToArray();
        JsonElement Capture(string name, int clicks, bool invalidate = false)
        {
            string path = Path.Combine(output, name + ".png"); var watch = Stopwatch.StartNew(); var info = bridge!.Capture(path, invalidate); double ms = watch.Elapsed.TotalMilliseconds;
            var frame = new CaptureFrame(File.ReadAllBytes(path), 620, 440, ms, "Experimental in-process Present / shared GPU texture");
            var pixels = JsonSerializer.SerializeToElement(CompatibilityVerification.PixelEvidence(frame, path, 310, 250, clicks % 2 != 0));
            bool hidden = !layer.GetWindows().First(window => window.Handle == root).Visible;
            Check(name, pixels.GetProperty("correctMarker").GetBoolean() && hidden && layer.ApplicationProcessId == pid, new { info, ms, hidden });
            events.Add(new { name, info, ms, pixels, source = State(), bridge = bridge.State });
            return info;
        }
        try
        {
            pid = layer.LaunchApplication(Path.Combine(project, "artifacts/gpu-research-build/D3DProbe.exe"), [], output); root = CompatibilityVerification.WaitRoot(layer, "Disposable Direct3D application").Handle;
            Check("unchanged hardware source", State().GetProperty("adapter").GetString()!.Contains("NVIDIA"));
            layer.HideLayer(root); Thread.Sleep(200); layer.SendQueueStateChord(root, [0x11, 0x4B]); var beforeHook = Wait(state => state.GetProperty("shortcuts").GetInt32() == 1);
            Check("baseline async physical state remains separate", beforeHook.GetProperty("ctrlState").GetBoolean() && !beforeHook.GetProperty("asyncCtrlState").GetBoolean());
            var baselineCpu = Cpu(); var baselineWatch = Stopwatch.StartNew(); var baselineGpu = Task.Run(() => GpuCounters.Sample(layer.GetProcessIds())); Thread.Sleep(2200); events.Add(new { name = "baseline-hidden-activity", ms = baselineWatch.Elapsed.TotalMilliseconds, cpu = Cost(baselineCpu, baselineWatch.Elapsed.TotalMilliseconds), gpu = baselineGpu.GetAwaiter().GetResult(), processes = Processes() });
            var installWatch = Stopwatch.StartNew(); bridge = new(layer, root); events.Add(new { name = "adapter-install", ms = installWatch.Elapsed.TotalMilliseconds }); mappingName = bridge.MappingName; Check("process-local adapter installed on owned hidden source", bridge.State.GetProperty("initialized").GetInt32() == 1 && !layer.GetWindows().First(window => window.Handle == root).Visible);
            Capture("hidden-green", 0); layer.SendKey(root, 0x20); Wait(state => state.GetProperty("clicks").GetInt32() == 1); Capture("hidden-orange", 1);
            for (int i = 0; i < 12; i++) { layer.SendKey(root, 0x20); int expected = i + 2; Wait(state => state.GetProperty("clicks").GetInt32() == expected); Capture("cycle-" + i, expected); }
            var activeCpu = Cpu(); var activeWatch = Stopwatch.StartNew(); var activeGpu = Task.Run(() => GpuCounters.Sample(layer.GetProcessIds())); var paintFrames = new List<JsonElement>();
            for (int i = 0; i < 20; i++) { layer.SendKey(root, 0x20); int expected = i + 14; Wait(state => state.GetProperty("clicks").GetInt32() == expected); paintFrames.Add(Capture("paint-" + i, expected, true)); Thread.Sleep(110); }
            double activeMs = activeWatch.Elapsed.TotalMilliseconds; var activeCost = Cost(activeCpu, activeMs); events.Add(new { name = "paint-request-capture-activity", count = paintFrames.Count, ms = activeMs, cpu = activeCost, gpu = activeGpu.GetAwaiter().GetResult(), processes = Processes(), note = "20 real input/color/capture cycles with 110 ms pacing, including observation, PNG output and pixel verification. This is a paced activity sample, not maximum throughput." });
            Check("warm graphics device and surfaces reused", paintFrames.All(frame => frame.GetProperty("deviceCreations").GetInt32() == 1 && frame.GetProperty("resourceOpens").GetInt32() == 1 && frame.GetProperty("stagingCreations").GetInt32() == 1));
            short physicalCtrl = GetAsyncKeyState(0x11); bridge.Chord([0x11, 0x4B]); var virtualState = Wait(state => state.GetProperty("shortcuts").GetInt32() == 2);
            Check("scoped asynchronous key state reaches source only", virtualState.GetProperty("asyncCtrlState").GetBoolean() && (physicalCtrl & 0x8000) == (GetAsyncKeyState(0x11) & 0x8000) && bridge.State.GetProperty("activeKeys").GetInt32() == 0, virtualState);
            layer.SendQueueStateChord(root, [0x11, 0x4B]); var released = Wait(state => state.GetProperty("shortcuts").GetInt32() == 3); Check("virtual state restored after chord", !released.GetProperty("asyncCtrlState").GetBoolean());
            layer.TakeHumanControl(); bool rejected = false; try { bridge.Chord([0x11, 0x4B]); } catch (InvalidOperationException) { rejected = true; }
            bridge.Chord([0x11, 0x4B], ControlOwner.Human); Wait(state => state.GetProperty("shortcuts").GetInt32() == 4); Check("explicit owner gates process-local input", rejected && bridge.State.GetProperty("activeKeys").GetInt32() == 0);
            layer.RestartWorker(); Check("worker recovery preserves instrumented source and owner", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human); Capture("after-worker-recovery", 33, true);
            int oldHelper = bridge.HelperPid; bridge.RestartHelper(); Capture("after-reader-recovery", 33, true); Check("reader recovery preserves source", oldHelper != bridge.HelperPid && !CompatibilityVerification.Alive(oldHelper) && CompatibilityVerification.Alive(pid));
            // Deliberately pause ONLY the disposable application's UI thread, with a
            // finally-resume, to verify that an absent Present has a bounded deadline.
            nint thread = OpenThread(0x802, false, bridge.SourceThreadId); Native.Check(thread != 0, "Open owned source thread"); bool suspended = false, timeoutObserved = false; var stallWatch = Stopwatch.StartNew();
            try { if (GetProcessIdOfThread(thread) != pid) throw new InvalidOperationException("Source thread identity changed."); Native.Check(SuspendThread(thread) != uint.MaxValue, "Pause disposable source"); suspended = true; try { bridge.Capture(Path.Combine(output, "stalled.png")); } catch (InvalidOperationException e) when (e.Message.Contains("-2147024638")) { timeoutObserved = true; } }
            finally { try { if (suspended) Native.Check(Native.ResumeThread(thread) != uint.MaxValue, "Resume disposable source"); } finally { Native.CloseHandle(thread); } }
            Check("missing Present returns a bounded timeout", timeoutObserved && stallWatch.ElapsedMilliseconds < 4000, new { ms = stallWatch.Elapsed.TotalMilliseconds });
            Thread.Sleep(150); bridge.ResetResources(); var reset = Capture("after-source-stall", 33, true); Check("surface reset preserves receiver device", reset.GetProperty("deviceCreations").GetInt32() == 1 && reset.GetProperty("resourceOpens").GetInt32() == 2);
            int stalledHelper = bridge.HelperPid; uint helperThread;
            using (var readerProcess = Process.GetProcessById(stalledHelper)) helperThread = (uint)readerProcess.Threads.Cast<ProcessThread>().OrderBy(candidate => candidate.StartTime).First().Id;
            nint readerThread = OpenThread(0x802, false, helperThread); Native.Check(readerThread != 0, "Open owned reader thread"); bool readerSuspended = false, readerTimeout = false; var readerStall = Stopwatch.StartNew();
            try
            {
                if (GetProcessIdOfThread(readerThread) != stalledHelper) throw new InvalidOperationException("Reader thread identity changed.");
                Native.Check(SuspendThread(readerThread) != uint.MaxValue, "Pause disposable reader"); readerSuspended = true;
                try { bridge.Capture(Path.Combine(output, "stalled-reader.png")); } catch (TimeoutException) { readerTimeout = true; }
            }
            finally { try { if (readerSuspended && CompatibilityVerification.Alive(stalledHelper)) Native.Check(Native.ResumeThread(readerThread) != uint.MaxValue, "Resume disposable reader"); } finally { Native.CloseHandle(readerThread); } }
            Check("parent deadline contains a hung reader only", readerTimeout && !CompatibilityVerification.Alive(stalledHelper) && CompatibilityVerification.Alive(pid) && readerStall.ElapsedMilliseconds < 7000, new { ms = readerStall.Elapsed.TotalMilliseconds });
            bridge.RestartHelper(); Capture("after-reader-stall", 33, true);
            layer.ReturnToAutomation(); bridge.Chord([0x11, 0x4B]); Wait(state => state.GetProperty("shortcuts").GetInt32() == 5);
            var beforeIdle = bridge.State; var idleCpu = Cpu(); var idleWatch = Stopwatch.StartNew(); var idleGpu = Task.Run(() => GpuCounters.Sample(layer.GetProcessIds())); Thread.Sleep(2200); var afterIdle = bridge.State; events.Add(new { name = "adapter-hidden-activity", ms = idleWatch.Elapsed.TotalMilliseconds, cpu = Cost(idleCpu, idleWatch.Elapsed.TotalMilliseconds), gpu = idleGpu.GetAwaiter().GetResult(), processes = Processes() }); Check("no continuous frame copying between requests", beforeIdle.GetProperty("copied").GetInt32() == afterIdle.GetProperty("copied").GetInt32() && afterIdle.GetProperty("presents").GetInt32() > beforeIdle.GetProperty("presents").GetInt32(), new { beforeIdle, afterIdle });
            bridge.StopAdapter(); Check("interceptors restored without application restart", bridge.State.GetProperty("initialized").GetInt32() == 0 && CompatibilityVerification.Alive(pid));
            layer.SendQueueStateChord(root, [0x11, 0x4B]); var stopped = Wait(state => state.GetProperty("shortcuts").GetInt32() == 6); Check("original async input semantics restored", !stopped.GetProperty("asyncCtrlState").GetBoolean());
            bridge.Dispose(); bridge = null; bool mappingReleased = false; try { using var stale = MemoryMappedFile.OpenExisting(mappingName); } catch (FileNotFoundException) { mappingReleased = true; } Check("shared control mapping released", mappingReleased);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            bridge?.Dispose(); if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(300);
        }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer); bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && desktopEquality, new { cleanup, foregroundEquality, pointerEquality, desktopEquality, graceful });
        File.WriteAllText(Path.Combine(output, "gpu-bridge.json"), JsonSerializer.Serialize(new { passed, failure, checks, events, sourcePid = pid, root, owned, graceful, cleanup, foregroundEquality, pointerEquality, desktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Explicit runtime instrumentation of the unchanged, hash-checked project D3DProbe only. Present vtable interception and executable-local GetAsyncKeyState import interception; no system-wide hooks, driver, privilege change or security bypass. Hidden source back buffer is shared across GPU devices/processes into a Default-desktop reader, then PNG-encoded. Native window remains hidden during capture; the source continues its own rendering timer. Human ownership is simulated; physical acceptance pending. DLL remains resident but inactive after stop, until source exit." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

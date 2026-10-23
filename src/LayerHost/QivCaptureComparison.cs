using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Standalone experiment. No default viewer or capture policy is changed here.
internal static class QivCaptureComparison
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(nint process, uint flags);
    static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return a.Length == 0 ? 0 : (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2; }
    static bool Matches(JsonElement pixels, bool second) => pixels.GetProperty(second ? "magenta" : "red").GetInt32() > 1000 && pixels.GetProperty(second ? "red" : "magenta").GetInt32() < 100 && pixels.GetProperty("Width").GetInt32() == 640 && pixels.GetProperty("Height").GetInt32() == 360;
    static object Resources(int pid) { using var p = Process.GetProcessById(pid); return new { pid, name = p.ProcessName, privateMiB = p.PrivateMemorySize64 / 1048576.0, handles = p.HandleCount, gdi = GetGuiResources(p.Handle, 0), user = GetGuiResources(p.Handle, 1) }; }

    internal static int Run(string output, bool coldNative = false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var checks = new List<object>(); var samples = new List<JsonElement>(); var phases = new List<object>(); var physical = new List<JsonElement>();
        bool passed = true, graceful = false, knownSecond = false; string? failure = null; int pid = 0; long root = 0; int[] owned = []; GpuBridgeResearchSession? gpu = null;
        string registryBefore = QivResearch.RegistryState(), desktop = Native.InputDesktopName(); var clip = QivResearch.CursorClip(); Native.GetCursorPos(out var pointer); var foreground = Native.GetForegroundWindow(); uint? inputBefore = Native.LastInputTick();
        using var stop = new CancellationTokenSource(); var physicalClock = Stopwatch.StartNew();
        var sampler = Task.Run(() =>
        {
            string? previous = null;
            while (!stop.IsCancellationRequested)
            {
                Native.GetCursorPos(out var point); var c = QivResearch.CursorClip(); var fg = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(fg, out uint foregroundPid);
                var value = JsonSerializer.SerializeToElement(new { foreground = (long)fg, foregroundPid, point.X, point.Y, c.Left, c.Top, c.Right, c.Bottom, inputTick = Native.LastInputTick(), desktop = Native.InputDesktopName() });
                string key = value.GetRawText(); if (key != previous) { physical.Add(JsonSerializer.SerializeToElement(new { ms = physicalClock.Elapsed.TotalMilliseconds, value })); previous = key; }
                Thread.Sleep(25);
            }
        });
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        bool Hidden() => !layer.GetWindows().First(w => w.Handle == root).Visible;
        (byte[] Png, object Metrics) Read(string strategy, int settle)
        {
            if (strategy == "gpu") { string file = Path.Combine(output, "scratch.png"); var metrics = gpu!.Capture(file, true); return (File.ReadAllBytes(file), metrics); }
            var frame = layer.InspectHiddenWindow(root, true, settle); return (frame.Png, new { frame.CaptureMilliseconds, frame.NativeRenderingMilliseconds, frame.EncodingMilliseconds, frame.Backend });
        }
        JsonElement Inspect(byte[] png) { string file = Path.Combine(output, "pixels.png"); File.WriteAllBytes(file, png); return QivResearch.Pixels(file); }
        JsonElement Sample(string phase, string strategy, int settle, int round, ControlOwner owner = ControlOwner.Automation)
        {
            bool expectedSecond = !knownSecond; var clock = Stopwatch.StartNew(); QivResearch.Wheel(layer, root, expectedSecond ? -120 : 120, owner: owner);
            double inputMs = clock.Elapsed.TotalMilliseconds; var read = Read(strategy, settle); double transactionMs = clock.Elapsed.TotalMilliseconds;
            var pixels = Inspect(read.Png); var firstPixels = pixels; bool fresh = Matches(pixels, expectedSecond), hidden = Hidden();
            if (round == 0 || !fresh) File.WriteAllBytes(Path.Combine(output, $"{phase}-{strategy}-{settle}-{round}-{(fresh ? "fresh" : "stale")}.png"), read.Png);
            int repairAttempts = 0;
            // Retry inspection only to establish the next starting state. Never replay input.
            if (!fresh) { var repair = Stopwatch.StartNew(); do { var next = Read("native", 100); pixels = Inspect(next.Png); repairAttempts++; if (Matches(pixels, expectedSecond)) break; } while (repair.ElapsedMilliseconds < 2000); if (!Matches(pixels, expectedSecond)) throw new InvalidOperationException("Unable to establish the selected image without replaying input."); }
            knownSecond = expectedSecond;
            var sample = JsonSerializer.SerializeToElement(new { phase, strategy, settle, round, expectedSecond, firstAttemptFresh = fresh, inputMs, transactionMs, metrics = read.Metrics, hidden, repairAttempts, pixels = firstPixels, resynchronizedPixels = repairAttempts > 0 ? pixels : (JsonElement?)null }); samples.Add(sample);
            if (!hidden) throw new InvalidOperationException("Capture transaction failed to restore hidden state."); return sample;
        }
        void Phase(string name, string strategy, int settle)
        {
            int[] ids = layer.GetProcessIds(); var before = ids.ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; });
            var resourcesBefore = ids.Select(Resources).ToArray(); using var supervisor = Process.GetCurrentProcess(); double supervisorBefore = supervisor.TotalProcessorTime.TotalMilliseconds;
            var engines = Task.Run(() => GpuCounters.Sample(ids)); var clock = Stopwatch.StartNew(); var measurements = new List<JsonElement>();
            for (int index = 0; index < 16; index++) { measurements.Add(Sample(name, strategy, settle, index)); Thread.Sleep(100); }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            var cpu = ids.Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, cpuMs = p.TotalProcessorTime.TotalMilliseconds - before[id] }; }).ToArray();
            double cpuMs = cpu.Sum(p => p.cpuMs), supervisorMs = supervisor.TotalProcessorTime.TotalMilliseconds - supervisorBefore;
            phases.Add(new { name, strategy, settle, captures = measurements.Count, firstAttemptFresh = measurements.Count(s => s.GetProperty("firstAttemptFresh").GetBoolean()), elapsedMs = elapsed, ownedCpuMs = cpuMs, machinePercent = cpuMs / elapsed * 100 / Environment.ProcessorCount, supervisorCpuMs = supervisorMs, cpu, resourcesBefore, resourcesAfter = ids.Select(Resources).ToArray(), engines = engines.GetAwaiter().GetResult(), note = "Paced changed-image input/capture. Supervisor CPU includes validation and sampling; child CPU excludes it. GPU engine interval is separate. Repair inspection, if needed, contributes to phase CPU and elapsed time." });
        }
        try
        {
            Check("pinned qIV unchanged", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(QivResearch.Executable))) == QivResearch.Sha256);
            string ini = QivResearch.Prepare(output, layer.Id); pid = layer.LaunchApplication(QivResearch.Executable, QivResearch.Arguments(output, ini), output);
            var wait = Stopwatch.StartNew(); do { root = layer.GetWindows().FirstOrDefault(w => w.ProcessId == pid && w.Parent == 0 && w.ClassName == "QuickImageViewer_VIAVW_" + layer.Id[6..])?.Handle ?? 0; if (root == 0) Thread.Sleep(40); } while (root == 0 && wait.ElapsedMilliseconds < 6000);
            if (root == 0) throw new TimeoutException("No configured qIV source.");
            gpu = new(layer, root, ResearchGpuTarget.Qiv); owned = layer.GetProcessIds(); gpu.ResizeClient(640, 360); layer.HideLayer(root);
            if (coldNative)
            {
                int helperPid = gpu.HelperPid; var state = gpu.State; gpu.StopAdapter(); var guard = gpu.CursorGuardState; bool inactive = gpu.State.GetProperty("initialized").GetInt32() == 0;
                gpu.Dispose(); gpu = null;
                Check("cold native starts with zero shared frames and no reader", state.GetProperty("requested").GetInt32() == 0 && state.GetProperty("copied").GetInt32() == 0 && inactive && guard.GetProperty("installed").GetInt32() == 1 && !CompatibilityVerification.Alive(helperPid), new { state, guard, helperPid });
            }
            var warm = Read(coldNative ? "native" : "gpu", 0); var ready = Stopwatch.StartNew(); while (!Matches(Inspect(warm.Png), false) && ready.ElapsedMilliseconds < 3000) { Thread.Sleep(50); warm=Read(coldNative ? "native" : "gpu",0); }
            Check("initial image ready and hidden", Matches(Inspect(warm.Png), false) && Hidden(), warm.Metrics);
            var conditions = coldNative ? new[] { ("native", 0), ("native", 10), ("native", 30), ("native", 100) } : new[] { ("gpu", 0), ("native", 0), ("native", 10), ("native", 30), ("native", 100) };
            for (int round = 0; round < 8; round++) for (int index = 0; index < conditions.Length; index++) { var c = conditions[(index + round * 2) % conditions.Length]; Sample("interleaved", c.Item1, c.Item2, round); }
            var reliable = samples.Where(s => s.GetProperty("phase").GetString() == "interleaved" && s.GetProperty("strategy").GetString() == "native").GroupBy(s => s.GetProperty("settle").GetInt32()).Where(g => g.All(s => s.GetProperty("firstAttemptFresh").GetBoolean())).Select(g => g.Key).Order().ToArray();
            int selected = reliable.Length > 0 ? reliable[0] : 100;
            Check("native presentation has an all-fresh tested condition", reliable.Length > 0, new { selected, reliable });
            if (!coldNative)
            {
                Phase("abba-native-a", "native", selected); Phase("abba-gpu-a", "gpu", 0); Phase("abba-gpu-b", "gpu", 0); Phase("abba-native-b", "native", selected);
                int helperPid = gpu!.HelperPid; var stoppedState = gpu.State; gpu.StopAdapter(); var guard = gpu.CursorGuardState; bool inactive = gpu.State.GetProperty("initialized").GetInt32() == 0; gpu.Dispose(); gpu = null;
                Check("reader exits and capture interception stops before native-only work", inactive && !CompatibilityVerification.Alive(helperPid) && guard.GetProperty("installed").GetInt32() == 1 && CompatibilityVerification.Alive(pid), new { helperPid, guard, stoppedState });
            }
            for (int round = 0; round < 8; round++) foreach (int settle in new[] { 0, 10, 30, 100 }.Skip(round % 4).Concat(new[] { 0, 10, 30, 100 }.Take(round % 4))) Sample("native-only", "native", settle, round);
            Phase("native-only-paced", "native", selected);
            layer.TakeHumanControl(); bool rejected = false; try { QivResearch.Wheel(layer, root, -120); } catch (InvalidOperationException) { rejected = true; } Check("native-only ownership rejects automation", rejected);
            Check("native-only human sender sees changed image", Sample("human", "native", selected, 0, ControlOwner.Human).GetProperty("firstAttemptFresh").GetBoolean());
            layer.RestartWorker(); Check("native-only worker recovery retains source and owner", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human);
            Check("native-only human sender works after recovery", Sample("recovered", "native", selected, 0, ControlOwner.Human).GetProperty("firstAttemptFresh").GetBoolean());
            layer.ReturnToAutomation(); Check("native-only automation resumes", Sample("resumed", "native", selected, 0).GetProperty("firstAttemptFresh").GetBoolean());
            Check("reliable paired workloads return fresh images", samples.Where(s => s.GetProperty("phase").GetString()!.StartsWith("abba-") || s.GetProperty("phase").GetString() == "native-only-paced").All(s => s.GetProperty("firstAttemptFresh").GetBoolean()));
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            gpu?.Dispose(); if (!layer.Closed) { owned = owned.Concat(layer.GetProcessIds()).Distinct().ToArray(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(250); stop.Cancel(); sampler.GetAwaiter().GetResult();
        }
        string registryAfter = QivResearch.RegistryState(); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var finalPointer); uint? inputAfter = Native.LastInputTick();
        bool pointerEquality = pointer.X == finalPointer.X && pointer.Y == finalPointer.Y, foregroundEquality = foreground == Native.GetForegroundWindow(), desktopEquality = desktop == Native.InputDesktopName();
        bool confinementStable = QivResearch.CursorClip().Equals(clip) && physical.All(s => { var v=s.GetProperty("value"); return v.GetProperty("Left").GetInt32()==clip.Left && v.GetProperty("Top").GetInt32()==clip.Top && v.GetProperty("Right").GetInt32()==clip.Right && v.GetProperty("Bottom").GetInt32()==clip.Bottom; });
        bool ownedNeverForeground = physical.All(s => !owned.Contains(s.GetProperty("value").GetProperty("foregroundPid").GetInt32()));
        bool desktopsStable = physical.All(s => s.GetProperty("value").GetProperty("desktop").GetString() == desktop);
        Check("registry targets unchanged", registryBefore == registryAfter);
        Check("owned cleanup and private desktop containment", cleanup && confinementStable && desktopEquality && desktopsStable && ownedNeverForeground && (inputBefore != null && inputBefore == inputAfter ? pointerEquality : true), new { cleanup, graceful, confinementStable, desktopEquality, desktopsStable, ownedNeverForeground, pointerEquality, foregroundEquality, inputBefore, inputAfter });
        var groups = samples.GroupBy(s => (phase:s.GetProperty("phase").GetString(), strategy:s.GetProperty("strategy").GetString(), settle:s.GetProperty("settle").GetInt32())).Select(g => new { g.Key.phase, g.Key.strategy, g.Key.settle, captures=g.Count(), fresh=g.Count(s=>s.GetProperty("firstAttemptFresh").GetBoolean()), medianTransactionMs=Median(g.Select(s=>s.GetProperty("transactionMs").GetDouble())), minTransactionMs=g.Min(s=>s.GetProperty("transactionMs").GetDouble()), maxTransactionMs=g.Max(s=>s.GetProperty("transactionMs").GetDouble()) });
        File.WriteAllText(Path.Combine(output,"capture-comparison.json"),JsonSerializer.Serialize(new {passed,failure,coldNative,checks,groups,samples,phases,physical,pid,owned,cleanup,graceful,registryBefore,registryAfter,note="First-attempt image identity after real native wheel input; full input/show/settle/capture/hide plus IPC latency until PNG bytes are available in supervisor memory. GPU path includes helper file write/read; native path returns PNG over RPC. Pixel validation/evidence writing excluded from per-transaction latency. Failed first attempts remain observations even after inspection-only resynchronization. Guard remains in exact owned source after Present interception and reader stop. No physical-input origin inference; simulated human ownership only."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new {output,passed,failure,groups,cleanup})); return passed ? 0 : 1;
    }
}

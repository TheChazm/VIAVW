using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class VisualReadinessResearch
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        QivCursorGuardSession? guard = null; GpuBridgeResearchSession? gpu = null;
        var checks = new List<object>(); var samples = new List<object>(); var costs = new List<object>(); bool passed = true, graceful = false; string? failure = null;
        int[] owned = []; int pid = 0, navigationCommands = 0; string registry = QivResearch.RegistryState(); long root = 0;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        bool Matches(CaptureFrame frame, int expected) { var id = QivLargeImageResearch.Identity(frame.Png); return id.GetProperty("valid").GetBoolean() && id.GetProperty("id").GetInt32() == expected; }
        CaptureFrame Read(bool shared)
        {
            if (!shared) return layer.InspectHiddenWindow(root, true, 0);
            string file = Path.Combine(output, "scratch.png"); var clock = Stopwatch.StartNew(); var metrics = gpu!.Capture(file, true);
            return new(File.ReadAllBytes(file), metrics.GetProperty("width").GetInt32(), metrics.GetProperty("height").GetInt32(), clock.Elapsed.TotalMilliseconds, "Experimental GPU bridge / D3D11");
        }
        Task<VisualObservationResult> Observe(Func<CaptureFrame> capture, Func<CaptureFrame, bool> condition, int budget = 3000, int interval = 15, CancellationToken cancel = default)
        {
            long revision = layer.BeginVisualObservation(ControlOwner.Automation);
            return VisualObservation.WaitAsync(capture, condition, () => layer.ValidateVisualObservation(revision), TimeSpan.FromMilliseconds(budget), TimeSpan.FromMilliseconds(interval), cancel);
        }
        try
        {
            string ini = QivResearch.Prepare(output, layer.Id); QivLargeImageResearch.Prepare(output, ini);
            pid = layer.LaunchApplication(QivResearch.Executable, QivResearch.Arguments(output, ini), output); root = QivGuardResearch.WaitRoot(layer, pid);
            guard = new(layer, root); layer.HideLayer(root);
            Check("initial marker verified", Observe(() => Read(false), f => Matches(f, 1)).GetAwaiter().GetResult().Matched);
            foreach (bool shared in new[] { false, true })
            {
                if (shared) gpu = new(layer, root, ResearchGpuTarget.Qiv);
                var ids = layer.GetProcessIds().Append(Environment.ProcessId).Distinct().ToArray();
                var cpuBefore = ids.ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; }); var active = Stopwatch.StartNew();
                for (int round = 0; round < 8; round++)
                {
                    int expected = round % 2 == 0 ? 4 : 1; layer.SendKey(root, expected == 4 ? 0x23 : 0x24); navigationCommands++;
                    CaptureFrame? first = null; int captures = 0; var clock = Stopwatch.StartNew();
                    var result = Observe(() => { var frame = Read(shared); first ??= frame; captures++; return frame; }, f => Matches(f, expected)).GetAwaiter().GetResult();
                    bool firstFresh = first != null && Matches(first, expected);
                    if (round < 2 || !firstFresh) { File.WriteAllBytes(Path.Combine(output, $"{(shared ? "gpu" : "native")}-{round}-first.png"), first!.Png); File.WriteAllBytes(Path.Combine(output, $"{(shared ? "gpu" : "native")}-{round}-ready.png"), result.Frame!.Png); }
                    Check($"{(shared ? "GPU" : "native")} image {expected} round {round}", result.Matched && result.Attempts == captures && !layer.GetWindows().Single(w => w.Handle == root).Visible);
                    samples.Add(new { backend = shared ? "GPU" : "native", round, expected, firstFresh, firstIdentity = QivLargeImageResearch.Identity(first!.Png), finalIdentity = QivLargeImageResearch.Identity(result.Frame!.Png), result.Attempts, result.ElapsedMilliseconds, result.CaptureMilliseconds, totalMilliseconds = clock.Elapsed.TotalMilliseconds });
                }
                double ms = active.Elapsed.TotalMilliseconds;
                costs.Add(new { backend = shared ? "GPU" : "native", ms, processes = ids.Select(id => { using var p = Process.GetProcessById(id); double cpuMs = p.TotalProcessorTime.TotalMilliseconds - cpuBefore[id]; return new { pid = id, name = p.ProcessName, supervisor = id == Environment.ProcessId, cpuMs, machinePercent = cpuMs / ms * 100 / Environment.ProcessorCount, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray(), note = "Eight active transitions including validation and retained PNG evidence. Supervisor included; image generation excluded. Short interval and process CPU tick quantization limit comparison." });
            }
            Check("one navigation per transition", navigationCommands == 16);
            var deadline = Observe(() => Read(true), _ => false, budget: 260, interval: 30).GetAwaiter().GetResult();
            Check("unmet condition returns bounded non-match", !deadline.Matched && deadline.Attempts is >= 1 and <= 9 && deadline.ElapsedMilliseconds >= 250 && deadline.ElapsedMilliseconds < 1800, deadline with { Frame = null });
            var cached = Read(false);
            var late = Observe(() => { Thread.Sleep(90); return cached; }, _ => true, budget: 25).GetAwaiter().GetResult();
            Check("late matching capture is reported as deadline rather than success", !late.Matched && late.Attempts == 1 && late.ElapsedMilliseconds >= 80, late with { Frame = null });
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); int captures = 0; bool rejected = false;
                try { Observe(() => { captures++; return Read(false); }, _ => true, cancel: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
                Check("pre-cancelled observation performs no capture", rejected && captures == 0);
            }
            using (var cancelled = new CancellationTokenSource())
            {
                int captures = 0; bool rejected = false;
                try { Observe(() => { captures++; var frame = Read(false); cancelled.Cancel(); return frame; }, _ => true, cancel: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
                Check("in-flight cancellation discards success after bounded capture", rejected && captures == 1);
            }
            using (var first = new ManualResetEventSlim())
            {
                int captures = 0; var pending = Observe(() => { var frame = Read(false); Interlocked.Increment(ref captures); first.Set(); return frame; }, _ => false, interval: 400);
                if (!first.Wait(3000)) throw new TimeoutException("No initial observation for handoff.");
                var handoff = Stopwatch.StartNew(); layer.TakeHumanControl(); double handoffMs = handoff.Elapsed.TotalMilliseconds; layer.ReturnToAutomation(); bool rejected = false;
                try { pending.GetAwaiter().GetResult(); } catch (InvalidOperationException e) when (e.Message.Contains("ownership change")) { rejected = true; }
                Check("handoff away and back invalidates pending observation", rejected && captures == 1 && handoffMs < 1000, new { captures, handoffMs });
            }
            using (var cancelled = new CancellationTokenSource())
            {
                int captures = 0; bool rejected = false; var clock = Stopwatch.StartNew();
                try { Observe(() => { captures++; cancelled.CancelAfter(60); return cached; }, _ => false, interval: 400, cancel: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
                Check("cancellation interrupts inspection interval without another capture", rejected && captures == 1 && clock.ElapsedMilliseconds < 500, new { captures, ms = clock.Elapsed.TotalMilliseconds });
            }
            bool leaseRejected = false; layer.TakeHumanControl(); try { layer.BeginVisualObservation(ControlOwner.Automation); } catch (InvalidOperationException) { leaseRejected = true; } finally { layer.ReturnToAutomation(); }
            Check("non-owner cannot start automation observation", leaseRejected);
            using (var other = manager.CreateLayer())
            {
                bool rejected = false; string? reason = null; try { other.CaptureVisualOutput(root); } catch (LayerOperationException e) when (e.Message.Contains("outside this layer") || e.Message == "Window no longer exists.") { rejected = true; reason = e.Message; }
                Check("foreign-desktop capture refused before rendering", rejected && layer.GetWindows().Any(w => w.Handle == root), new { reason }); owned = owned.Concat(other.GetProcessIds()).ToArray();
            }
            Check("timeouts and cancellation preserve source image and owner", Observe(() => Read(true), f => Matches(f, 1)).GetAwaiter().GetResult().Matched && layer.Owner == ControlOwner.Automation && layer.ApplicationProcessId == pid);
            layer.RestartWorker(); Check("new observation survives input-worker recovery", Observe(() => Read(false), f => Matches(f, 1)).GetAwaiter().GetResult().Matched);
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { gpu?.Dispose(); if (!layer.Closed) { owned = owned.Concat(layer.GetProcessIds()).Distinct().ToArray(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); guard?.Dispose(); Thread.Sleep(200); }
        var physical = monitor.Complete(owned); Check("sampled physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical);
        Check("owned process and registry cleanup", owned.All(id => !CompatibilityVerification.Alive(id)) && registry == QivResearch.RegistryState(), new { owned, graceful });
        File.WriteAllText(Path.Combine(output, "visual-readiness.json"), JsonSerializer.Serialize(new { passed, failure, checks, samples, costs, navigationCommands, pid, owned, graceful, note = "Caller-specified test markers only. No input replay, universal readiness classifier, or physical human acceptance. An in-flight native operation uses its existing watchdog and can overrun an observation budget." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

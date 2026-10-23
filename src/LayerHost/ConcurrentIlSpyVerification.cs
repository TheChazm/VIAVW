using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class ConcurrentIlSpyVerification
{
    static JsonElement Node(JsonElement snapshot, string id) => snapshot.GetProperty("nodes").EnumerateArray().First(node => node.GetProperty("id").GetString() == id);
    static string Value(JsonElement snapshot, string id) => Node(snapshot, id).GetProperty("value").GetString() ?? "";
    static string Selection(JsonElement snapshot) => string.Join("", Node(snapshot, "textEditor").GetProperty("selection").EnumerateArray().Select(part => part.GetString()));
    static void Click(ApplicationLayer layer, long root, int x, int y, ControlOwner owner = ControlOwner.Automation) => layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true, owner);
    static JsonElement Wait(ApplicationLayer layer, long root, Func<JsonElement, bool> condition, string? automationId = null)
    {
        var watch = Stopwatch.StartNew(); JsonElement snapshot;
        do { snapshot = layer.ObserveAccessible(root, automationId); if (snapshot.GetProperty("nodes").GetArrayLength() > 0 && condition(snapshot)) return snapshot; Thread.Sleep(80); } while (watch.ElapsedMilliseconds < 6000);
        throw new TimeoutException("Concurrent ILSpy application state deadline.");
    }
    static object Summary(List<double> samples) { var sorted = samples.Order().ToArray(); return new { count = sorted.Length, median = sorted[sorted.Length / 2], p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * .95) - 1)], maximum = sorted[^1] }; }
    static Dictionary<int, double> Cpu(int[] ids) => ids.ToDictionary(id => id, id => { try { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; } catch (ArgumentException) { return 0; } });
    static object[] Processes(int[] ids) => ids.Select(id => { try { using var process = Process.GetProcessById(id); return (object)new { pid = id, name = process.ProcessName, workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0, threads = process.Threads.Count }; } catch (ArgumentException) { return new { pid = id, exited = true }; } }).ToArray();
    internal static int Run(string output, bool narrowFirst = false)
    {
        Directory.CreateDirectory(output); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputBefore = Native.LastInputTick(); string inputDesktop = Native.InputDesktopName();
        var binaries = Directory.GetFiles(Path.GetDirectoryName(IlSpyVerification.Executable)!).ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var manager = new LayerManager(); using var power = new DisplayPowerObserver(); using var a = manager.CreateLayer(); using var b = manager.CreateLayer();
        var layers = new[] { a, b }; string[] folders = [Path.Combine(output, "a"), Path.Combine(output, "b")]; var events = new List<object>(); var phase = "launch";
        void Event(string name, object result) { events.Add(new { name, result = JsonSerializer.SerializeToElement(result), utc = DateTime.UtcNow }); File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { phase, events }, new JsonSerializerOptions { WriteIndented = true })); }
        try
        {
            var launch = Stopwatch.StartNew();
            for (int i = 0; i < 2; i++) layers[i].LaunchApplication(IlSpyVerification.Executable, IlSpyVerification.Prepare(folders[i]), folders[i], WpfHookEnvironment.Create(IlSpyVerification.Executable, folders[i]));
            Task.WhenAll(layers.Select((layer, i) => WpfHookEnvironment.VerifyAsync(folders[i], layer.ApplicationProcessId))).GetAwaiter().GetResult();
            var roots = layers.Select(IlSpyVerification.WaitRoot).ToArray(); var pids = layers.Select(layer => layer.ApplicationProcessId).ToArray();
            for (int i = 0; i < 2; i++) Wait(layers[i], roots[i].Handle, snapshot => Value(snapshot, "textEditor").Contains("class Program"));
            Event("two independent ordinary instances", new { launchMs = launch.Elapsed.TotalMilliseconds, pids, desktops = layers.Select(layer => layer.Id), power = power.Snapshot() });
            Event("initial infrastructure identities", new { layers = layers.Select(layer => new { worker = layer.WorkerProcessId, infrastructure = layer.GetInfrastructureProcessIds(), identities = ProcessCensus.Read(layer.GetProcessIds()) }) });
            bool foreignHandleRejected = false; try { a.CaptureVisualOutput(roots[1].Handle); } catch (LayerOperationException) { foreignHandleRejected = true; }
            if (!foreignHandleRejected) throw new InvalidOperationException("Foreign layer HWND was accepted.");
            phase = "independent-search";
            Task.WhenAll(layers.Select((layer, i) => Task.Run(() => { Click(layer, roots[i].Handle, 610, 38); Wait(layer, roots[i].Handle, snapshot => snapshot.GetProperty("nodes").EnumerateArray().Any(node => node.GetProperty("id").GetString() == "searchBox")); }))).GetAwaiter().GetResult();
            int[] ids = layers.SelectMany(layer => layer.GetProcessIds()).Append(Environment.ProcessId).Distinct().ToArray(); var idleBefore = Cpu(ids); var idleWatch = Stopwatch.StartNew(); Thread.Sleep(3000); var idleAfter = Cpu(ids); double idleCpuMs = ids.Sum(id => idleAfter[id] - idleBefore[id]); double idleMs = idleWatch.Elapsed.TotalMilliseconds;
            var measurements = new List<object>();
            foreach (string observationMode in narrowFirst ? new[] { "narrow", "broad" } : new[] { "broad", "narrow" })
            {
                var before = Cpu(ids); var active = Stopwatch.StartNew(); var allTimes = new List<double>[] { [], [] }; var captureTimes = new List<double>[] { [], [] };
                // Each layer has a single driver task; commands remain serialized within a layer.
                Task.WhenAll(layers.Select((layer, i) => Task.Run(() =>
                {
                    string prefix = i == 0 ? "A_ONLY_" : "B_ONLY_";
                    for (int round = 0; round < 6; round++)
                    {
                        string expected = prefix + observationMode.ToUpperInvariant() + round.ToString("D2"); var timer = Stopwatch.StartNew();
                        layer.SendQueueStateChord(roots[i].Handle, [0x11, 0x41]); layer.SendKeyboardInput(roots[i].Handle, expected);
                        var snapshot = Wait(layer, roots[i].Handle, value => Value(value, "searchBox") == expected, observationMode == "narrow" ? "searchBox" : null);
                        var frame = layer.CaptureVisualOutput(roots[i].Handle); captureTimes[i].Add(frame.CaptureMilliseconds); allTimes[i].Add(timer.Elapsed.TotalMilliseconds);
                        if (round is 0 or 5) { File.WriteAllBytes(Path.Combine(folders[i], observationMode + "-search-" + round + ".png"), frame.Png); File.WriteAllText(Path.Combine(folders[i], observationMode + "-search-" + round + ".json"), JsonSerializer.Serialize(snapshot)); }
                    }
                }))).GetAwaiter().GetResult();
                active.Stop(); var after = Cpu(ids); double activeCpuMs = ids.Sum(id => after[id] - before[id]);
                measurements.Add(new { observationMode, roundsPerLayer = 6, results = Enumerable.Range(0, 2).Select(i => new { layer = i, captureMs = Summary(captureTimes[i]), interactionObservationCaptureMs = Summary(allTimes[i]) }), activeMs = active.Elapsed.TotalMilliseconds, activeCpuMs, activeWholeMachinePercent = activeCpuMs / active.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, cpuByPid = ids.Select(id => new { pid = id, milliseconds = after[id] - before[id] }) });
            }
            Event("concurrent isolated search and capture", new { foreignHandleRejected, measurements, idleMs, idleCpuMs, idleWholeMachinePercent = idleCpuMs / idleMs / Environment.ProcessorCount * 100, idleCpuByPid = ids.Select(id => new { pid = id, milliseconds = idleAfter[id] - idleBefore[id] }), memory = Processes(ids), power = power.Snapshot() });
            phase = "mixed-ownership-recovery";
            a.TakeHumanControl(); bool denied = false; try { a.SendKeyboardInput(roots[0].Handle, "WRONG_OWNER"); } catch (InvalidOperationException) { denied = true; }
            var beforeRecovery = a.ObserveAccessible(roots[0].Handle); int oldWorker = a.WorkerProcessId;
            var secondDriver = Task.Run(() =>
            {
                for (int round = 0; round < 8; round++) { string text = "B_DURING_RECOVERY_" + round; b.SendQueueStateChord(roots[1].Handle, [0x11, 0x41]); b.SendKeyboardInput(roots[1].Handle, text); Wait(b, roots[1].Handle, snapshot => Value(snapshot, "searchBox") == text); b.CaptureVisualOutput(roots[1].Handle); }
            });
            var restart = Stopwatch.StartNew(); a.RestartWorker(); double restartMs = restart.Elapsed.TotalMilliseconds; secondDriver.GetAwaiter().GetResult();
            var recovered = a.ObserveAccessible(roots[0].Handle);
            bool recoveredState = a.Owner == ControlOwner.Human && a.ApplicationProcessId == pids[0] && a.WorkerProcessId != oldWorker && !CompatibilityVerification.Alive(oldWorker) && Value(beforeRecovery, "searchBox") == Value(recovered, "searchBox");
            if (!denied || !recoveredState) throw new InvalidOperationException("Ownership/process/document state failed worker recovery.");
            a.SendQueueStateChord(roots[0].Handle, [0x11, 0x41], sender: ControlOwner.Human); a.SendKeyboardInput(roots[0].Handle, "HUMAN_A", ControlOwner.Human); Wait(a, roots[0].Handle, snapshot => Value(snapshot, "searchBox") == "HUMAN_A");
            a.ReturnToAutomation(); a.SendQueueStateChord(roots[0].Handle, [0x11, 0x41]); a.SendKeyboardInput(roots[0].Handle, "A_RESUMED"); Wait(a, roots[0].Handle, snapshot => Value(snapshot, "searchBox") == "A_RESUMED");
            Event("worker recovery while other layer continues", new { denied, recoveredState, oldWorker, newWorker = a.WorkerProcessId, restartMs, sameSourcePids = layers.Select((layer, i) => layer.ApplicationProcessId == pids[i]).All(value => value), secondLayer = Value(b.ObserveAccessible(roots[1].Handle), "searchBox"), infrastructure = a.GetInfrastructureProcessIds(), identities = ProcessCensus.Read(a.GetProcessIds()) });
            phase = "parallel-code-gestures";
            Task.WhenAll(layers.Select((layer, i) => Task.Run(() =>
            {
                layer.SendQueueStateChord(roots[i].Handle, [0x11, 0x41]); layer.SendKeyboardInput(roots[i].Handle, "ProbeWindow"); Wait(layer, roots[i].Handle, snapshot => Value(snapshot, "searchBox") == "ProbeWindow");
                layer.SendKey(roots[i].Handle, 0x28); layer.SendKey(roots[i].Handle, 0x0D); Wait(layer, roots[i].Handle, snapshot => Value(snapshot, "textEditor").Contains("ProbeWindow(bool"));
                Click(layer, roots[i].Handle, 846, 70); Wait(layer, roots[i].Handle, snapshot => Node(snapshot, "textEditor").GetProperty("height").GetDouble() > 250);
                layer.SendQueueMouseSequence(roots[i].Handle, [new(MouseMessage.Move, 650, 290), new(MouseMessage.Wheel, 650, 290, Delta: i == 0 ? -720 : -360)], true);
                var scrolled = Wait(layer, roots[i].Handle, snapshot => Node(snapshot, "textEditor").GetProperty("verticalScrollPercent").GetDouble() > 0);
                layer.SendQueueMouseSequence(roots[i].Handle, [new(MouseMessage.Move, 400, 145), new(MouseMessage.LeftDown, 400, 145, 1), new(MouseMessage.Move, i == 0 ? 650 : 540, i == 0 ? 190 : 165, 1), new(MouseMessage.LeftUp, i == 0 ? 650 : 540, i == 0 ? 190 : 165)], true);
                var selected = Wait(layer, roots[i].Handle, snapshot => Selection(snapshot).Length > 10); File.WriteAllText(Path.Combine(folders[i], "selected.json"), JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true })); File.WriteAllBytes(Path.Combine(folders[i], "selected.png"), layer.CaptureVisualOutput(roots[i].Handle).Png);
            }))).GetAwaiter().GetResult();
            var selectedA = a.ObserveAccessible(roots[0].Handle); var selectedB = b.ObserveAccessible(roots[1].Handle);
            bool distinctSelections = Selection(selectedA) != Selection(selectedB); if (!distinctSelections) throw new InvalidOperationException("Expected distinct per-layer selections.");
            Event("distinct simultaneous code selections", new { distinctSelections, a = Selection(selectedA), b = Selection(selectedB), scrollA = Node(selectedA, "textEditor").GetProperty("verticalScrollPercent"), scrollB = Node(selectedB, "textEditor").GetProperty("verticalScrollPercent"), power = power.Snapshot() });
            phase = "independent-close"; int[] aIds = a.GetProcessIds(); int[] aInfrastructure = a.GetInfrastructureProcessIds(); bool aGraceful = a.CloseGracefully(TimeSpan.FromSeconds(5)); object[] remainingA = aGraceful ? [] : Processes(a.GetProcessIds()); var identityA = aGraceful ? [] : ProcessCensus.Read(a.GetProcessIds()); if (!aGraceful) a.Dispose(); Thread.Sleep(200);
            b.SendQueueStateChord(roots[1].Handle, [0x11, 0x41]); var survived = b.ObserveAccessible(roots[1].Handle); bool bUnaffected = b.ApplicationProcessId == pids[1] && CompatibilityVerification.Alive(pids[1]) && Value(survived, "textEditor") == Value(selectedB, "textEditor");
            File.WriteAllBytes(Path.Combine(folders[1], "after-peer-close.png"), b.CaptureVisualOutput(roots[1].Handle).Png);
            int[] bIds = b.GetProcessIds(); bool bGraceful = b.CloseGracefully(TimeSpan.FromSeconds(5)); object[] remainingB = bGraceful ? [] : Processes(b.GetProcessIds()); if (!bGraceful) b.Dispose(); Thread.Sleep(200);
            bool cleanup = aIds.Concat(bIds).All(id => !CompatibilityVerification.Alive(id)); bool hashes = binaries.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(IlSpyVerification.Executable)!, pair.Key!)))));
            Event("close and containment", new { aGraceful, bGraceful, aInfrastructure, identityA, remainingA, remainingB, bUnaffected, cleanup, hashes });
            Native.GetCursorPos(out var endPointer); bool focus = foreground == Native.GetForegroundWindow(), pointerEqual = pointer.X == endPointer.X && pointer.Y == endPointer.Y, desktopEqual = inputDesktop == Native.InputDesktopName();
            var powerEvents = power.Events(); power.Dispose(); bool passed = cleanup && hashes && bUnaffected && focus && pointerEqual && desktopEqual;
            File.WriteAllText(Path.Combine(output, "concurrent-ilspy.json"), JsonSerializer.Serialize(new { passed, events, pids, foregroundEquality = focus, pointerEquality = pointerEqual, inputDesktopEquality = desktopEqual, inputBefore, inputAfter = Native.LastInputTick(), powerEvents, powerObserverStopped = power.ThreadStopped, gracefulCloseBoth = aGraceful && bGraceful, note = "Two ordinary unchanged ILSpy instances on separate non-input desktops with isolated config and software startup adapter. Single serialized driver per layer runs concurrently with the other layer. UIA reads results only; targeted input supplies interactions. Human ownership commands are automated test simulations; physical acceptance pending. Graceful close reported separately from successful owned cleanup." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { passed, output, pids, cleanup, gracefulCloseBoth = aGraceful && bGraceful })); return passed ? 0 : 1;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { phase, error = e.ToString(), events, processes = layers.Where(layer => !layer.Closed).SelectMany(layer => Processes(layer.GetProcessIds())) }, new JsonSerializerOptions { WriteIndented = true })); throw;
        }
    }
}

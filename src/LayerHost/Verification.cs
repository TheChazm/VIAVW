using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class Verification
{
    static readonly List<object> checks = [];
    static readonly List<object> desktopSamples = [];
    static int failures;

    static void Check(string name, bool passed, object? detail = null)
    {
        checks.Add(new { name, passed, detail });
        if (!passed) failures++;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {JsonSerializer.Serialize(detail)}");
    }
    static WindowInfo[] WaitWindows(ApplicationLayer layer)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            var windows = layer.GetWindows();
            if (windows.Any(w => w.Parent == 0 && w.Visible && w.Width > 100)) return windows;
            Thread.Sleep(100);
        }
        return layer.GetWindows();
    }
    static WindowInfo Editor(WindowInfo[] w) => w.First(v => v.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase));
    static WindowInfo Button(WindowInfo[] w) => w.First(v => v.Text == "Increment counter");
    static WindowInfo Status(WindowInfo[] w) => w.First(v => v.Text.StartsWith("ticks="));
    static WindowInfo Root(WindowInfo[] w) => w.First(v => v.Parent == 0 && v.Visible && v.Width > 100);
    static int Counter(string status, string name) => int.Parse(status.Split(';').First(s => s.TrimStart().StartsWith(name + "=")).Split('=')[1]);
    static (long Foreground, int X, int Y, string Desktop) Snapshot()
    {
        Native.GetCursorPos(out var p);
        return ((long)Native.GetForegroundWindow(), p.X, p.Y, Native.InputDesktopName());
    }
    static void Observe(string operation, Action action)
    {
        var before = Snapshot(); uint? inputTickBefore = Native.LastInputTick(); action(); uint? inputTickAfter = Native.LastInputTick(); var after = Snapshot();
        desktopSamples.Add(new { operation, before = new { before.Foreground, before.X, before.Y, before.Desktop }, after = new { after.Foreground, after.X, after.Y, after.Desktop }, inputTickBefore, inputTickAfter, unchanged = before == after });
    }
    static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
    static bool WaitExited(IEnumerable<int> pids)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000) { if (pids.All(pid => !Alive(pid))) return true; Thread.Sleep(50); }
        return false;
    }
    static object Stats(int pid)
    {
        using var p = Process.GetProcessById(pid);
        return new { pid, name = p.ProcessName, workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0, cpuMilliseconds = p.TotalProcessorTime.TotalMilliseconds, handles = p.HandleCount };
    }

    internal static int Run(string probe, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        checks.Clear(); desktopSamples.Clear(); failures = 0;
        var started = DateTimeOffset.UtcNow;
        object? measurements = null;
        using var manager = new LayerManager();
        try
        {
            var original = Snapshot();
            var a = manager.CreateLayer(); var b = manager.CreateLayer();
            int pidA = a.LaunchApplication(probe, ["--spawn-child", "--file-probe"], output);
            int pidB = b.LaunchApplication(probe, workingDirectory: output);
            var wa = WaitWindows(a); var wb = WaitWindows(b);
            var rootA = Root(wa); var editA = Editor(wa); var buttonA = Button(wa);
            var rootB = Root(wb); var editB = Editor(wb);
            Check("two independent non-input desktops", a.Id != b.Id && original.Desktop == Native.InputDesktopName(), new { a = a.Id, b = b.Id, physical = original.Desktop });
            Check("ordinary process and filesystem access", Alive(pidA) && File.ReadAllText(Path.Combine(output, "probe-os-access.txt")).Contains("normal filesystem"), new { pidA, pidB });
            Check("child tree in job", a.GetProcessIds().Length >= 3, a.GetProcessIds());
            SaveCapture(a, rootA.Handle, Path.Combine(output, "native-before.png"));
            var textTime = Stopwatch.StartNew();
            Observe("A WM_CHAR batch", () => a.SendKeyboardInput(editA.Handle, "LAYER_A_ONLY"));
            textTime.Stop();
            Observe("B WM_CHAR batch", () => b.SendKeyboardInput(editB.Handle, "LAYER_B_ONLY"));
            Observe("A navigation key", () => a.SendKey(editA.Handle, 0x25));
            Observe("A mouse click", () => a.SendMouseInput(buttonA.Handle, 40, 18));
            wa = a.GetWindows(); wb = b.GetWindows();
            Check("keyboard goes to correct EDIT controls", Editor(wa).Text == "LAYER_A_ONLY" && Editor(wb).Text == "LAYER_B_ONLY", new { a = Editor(wa).Text, b = Editor(wb).Text });
            Check("normal KeyPress and KeyDown event path", Counter(Status(wa).Text, "chars") == 12 && Counter(Status(wa).Text, "keys") >= 1, Status(wa).Text);
            Check("normal button mouse click path", Counter(Status(wa).Text, "clicks") == 1, Status(wa).Text);
            bool rejected = false;
            try { a.SendKeyboardInput(editB.Handle, "BAD"); } catch (LayerOperationException) { rejected = true; }
            Check("cross-layer HWND rejected", rejected && Editor(b.GetWindows()).Text == "LAYER_B_ONLY");
            var afterFrame = a.CaptureVisualOutput(rootA.Handle);
            File.WriteAllBytes(Path.Combine(output, "native-after.png"), afterFrame.Png);
            Check("visual output changes after interaction", !SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "native-before.png"))).SequenceEqual(SHA256.HashData(afterFrame.Png)), new { afterFrame.Width, afterFrame.Height, afterFrame.CaptureMilliseconds, note = "Pixel difference plus manual PNG inspection; timer also changes pixels." });
            int ticks = Counter(Status(wa).Text, "ticks");
            Observe("hide private window", () => a.HideLayer(rootA.Handle));
            Thread.Sleep(1100);
            wa = a.GetWindows();
            Check("hidden window continues workload", !wa.First(v => v.Handle == rootA.Handle).Visible && Counter(Status(wa).Text, "ticks") > ticks, Status(wa).Text);
            SaveCapture(a, rootA.Handle, Path.Combine(output, "native-hidden.png"));
            Observe("show on private desktop", () => a.ShowLayer(rootA.Handle));
            a.RequestAttention("Inspect the counter and text.");
            a.TakeHumanControl();
            rejected = false;
            try { a.SendKeyboardInput(editA.Handle, "AUTOMATION_SHOULD_FAIL"); } catch (InvalidOperationException) { rejected = true; }
            Check("handoff suspends automation", rejected && a.Owner == ControlOwner.Human);
            Observe("human routed key", () => a.SendKeyboardInput(editA.Handle, "H", ControlOwner.Human));
            Observe("human routed mouse", () => a.SendMouseInput(buttonA.Handle, 40, 18, ControlOwner.Human));
            a.ReturnToAutomation();
            a.SendKeyboardInput(editA.Handle, "R");
            wa = a.GetWindows();
            Check("handoff keeps process and document state", a.ApplicationProcessId == pidA && Alive(pidA) && Editor(wa).Text.Contains("HR") && Counter(Status(wa).Text, "clicks") == 2, new { pid = pidA, text = Editor(wa).Text, status = Status(wa).Text });
            var clicks = new List<double>(); var captures = new List<double>();
            for (int i = 0; i < 20; i++) { var s = Stopwatch.StartNew(); a.SendMouseInput(buttonA.Handle, 40, 18); clicks.Add(s.Elapsed.TotalMilliseconds); }
            for (int i = 0; i < 6; i++) { var s = Stopwatch.StartNew(); a.CaptureVisualOutput(rootA.Handle); captures.Add(s.Elapsed.TotalMilliseconds); }
            // Let runtime startup / tiered compilation settle before a quiescent sample.
            Thread.Sleep(4000);
            var ids = new[] { Environment.ProcessId }.Concat(a.GetProcessIds()).Concat(b.GetProcessIds()).Distinct().ToArray();
            var cpuBefore = ids.ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; });
            var idle = Stopwatch.StartNew(); Thread.Sleep(2200); idle.Stop();
            var idleCpu = ids.Select(id =>
            {
                using var p = Process.GetProcessById(id);
                double delta = p.TotalProcessorTime.TotalMilliseconds - cpuBefore[id];
                return new { pid = id, cpuMilliseconds = delta, percentOfOneCore = 100 * delta / idle.Elapsed.TotalMilliseconds };
            }).ToArray();
            measurements = new { processes = ids.Select(Stats).ToArray(), idleIntervalMs = idle.Elapsed.TotalMilliseconds, idleCpu, textBatchMs = textTime.Elapsed.TotalMilliseconds, mouseRoundTripMs = Summary(clicks), captureAndPipeRoundTripMs = Summary(captures), gpu = "not measured; GDI PrintWindow backend, no GPU capture session", note = "Small warm single-machine samples; captures include PNG encoding and IPC, not display refresh latency. Shared runtime pages make summed working sets overestimate unique RAM." };
            var stuck = manager.CreateLayer(); stuck.LaunchApplication(probe, ["--hang-print"], output);
            var stuckRoot = Root(WaitWindows(stuck)); var stuckPids = stuck.GetProcessIds();
            bool timedOut = false;
            try { stuck.CaptureVisualOutput(stuckRoot.Handle); } catch (TimeoutException) { timedOut = true; }
            Check("hung capture kills only affected layer", timedOut && stuck.Closed && WaitExited(stuckPids) && Alive(pidA) && Alive(pidB), stuckPids);
            var closePids = b.GetProcessIds(); b.Dispose();
            Check("closing a layer cleans its tree", WaitExited(closePids) && Alive(pidA), closePids);
            CrashCleanup(probe, output);
            var aPids = a.GetProcessIds(); a.Dispose();
            Check("normal cleanup includes child process", WaitExited(aPids), aPids);
            var cleanupWatch = Stopwatch.StartNew();
            while (new[] { a.Id, b.Id, stuck.Id }.Any(Native.DesktopExists) && cleanupWatch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
            Check("private desktop objects released", new[] { a.Id, b.Id, stuck.Id }.All(id => !Native.DesktopExists(id)));
            var last = Snapshot();
            Check("input desktop never switched", original.Desktop == last.Desktop, new { original.Desktop, finalDesktop = last.Desktop });
            var samples = JsonSerializer.SerializeToElement(desktopSamples).EnumerateArray().ToArray();
            Check("sampled foreground focus unchanged", samples.All(v => v.GetProperty("before").GetProperty("Foreground").GetInt64() == v.GetProperty("after").GetProperty("Foreground").GetInt64()), new { count = samples.Length, note = "Before/after samples, not continuous foreground logging." });
            var inputSamples = samples.Where(v => !v.GetProperty("operation").GetString()!.Contains("private") && !v.GetProperty("operation").GetString()!.StartsWith("show"));
            Check("physical pointer unchanged during input samples", inputSamples.All(v => v.GetProperty("before").GetProperty("X").GetInt32() == v.GetProperty("after").GetProperty("X").GetInt32() && v.GetProperty("before").GetProperty("Y").GetInt32() == v.GetProperty("after").GetProperty("Y").GetInt32()), new { note = "User can move their pointer during a test; inspect samples if this check fails. Real human typing is a separate manual test." });
        }
        catch (Exception e) { Check("test execution", false, e.ToString()); }
        finally
        {
            manager.Dispose();
            var report = new { startedUtc = started, completedUtc = DateTimeOffset.UtcNow, os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), checks, desktopSamples, measurements, failures, manualHumanTypingTest = "not executed by this automated suite" };
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failures == 0 ? 0 : 1;
    }
    static object Summary(List<double> values)
    {
        var ordered = values.Order().ToArray();
        return new { samples = values.Count, min = ordered[0], median = ordered[ordered.Length / 2], p95 = ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * .95) - 1)], max = ordered[^1] };
    }
    static void SaveCapture(ApplicationLayer layer, long hwnd, string path) => File.WriteAllBytes(path, layer.CaptureVisualOutput(hwnd).Png);
    internal static int CrashFixture(string probe, string manifest)
    {
        using var manager = new LayerManager(); var layer = manager.CreateLayer();
        layer.LaunchApplication(probe, ["--spawn-child"], Path.GetDirectoryName(manifest));
        WaitWindows(layer); Thread.Sleep(300);
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { host = Environment.ProcessId, pids = layer.GetProcessIds() }));
        Thread.Sleep(Timeout.Infinite); return 0;
    }
    static void CrashCleanup(string probe, string output)
    {
        var manifest = Path.Combine(output, "crash-pids.json");
        if (File.Exists(manifest)) File.Delete(manifest);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe")) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--crash-fixture"); info.ArgumentList.Add(probe); info.ArgumentList.Add(manifest);
        using var host = Process.Start(info)!;
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(manifest) && !host.HasExited && watch.ElapsedMilliseconds < 12000) Thread.Sleep(100);
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var pids = doc.RootElement.GetProperty("pids").Deserialize<int[]>()!;
            host.Kill(); host.WaitForExit(5000);
            Check("abrupt host death cleans worker/app/child", WaitExited(pids), pids);
        }
        finally { if (!host.HasExited) host.Kill(); }
    }

    internal static int ProbeExternal(string exe, string[] args)
    {
        var output = Path.GetFullPath(Path.Combine("artifacts", "external-" + Path.GetFileNameWithoutExtension(exe) + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(output);
        using var manager = new LayerManager(); var layer = manager.CreateLayer();
        int pid = layer.LaunchApplication(exe, args, output);
        var before = Snapshot();
        var windows = WaitWindows(layer);
        File.WriteAllText(Path.Combine(output, "windows.json"), JsonSerializer.Serialize(new { exe, args, pid, windows }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var root in windows.Where(w => w.Parent == 0 && w.Visible && w.Width > 100).Take(3))
        {
            try { SaveCapture(layer, root.Handle, Path.Combine(output, $"window-{root.Handle}.png")); }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "capture-error.txt"), e.ToString()); break; }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { output, windows = windows.Length, pid, processAlive = Alive(pid), focusAndPointerUnchanged = before == Snapshot() }));
        return windows.Length > 0 ? 0 : 2;
    }
}

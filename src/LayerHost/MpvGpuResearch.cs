using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace LayerHost;

internal static class MpvGpuResearch
{
    internal static int PrepareDemo(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        string video = Path.Combine(output, "disposable.y4m"); MakeVideo(video);
        File.WriteAllText(Path.Combine(output, "launch-arguments.json"), JsonSerializer.Serialize(Arguments(output, video, "VIAVW ordinary GPU player")));
        return 0;
    }
    internal static void MakeVideo(string path)
    {
        const int width = 320, height = 180;
        using var file = File.Create(path); file.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F5:1 Ip A1:1 C420jpeg\n"));
        for (int frame = 1; frame <= 40; frame++)
        {
            file.Write("FRAME\n"u8); byte[] y = new byte[width * height], u = new byte[width * height / 4], v = new byte[u.Length];
            bool orange = frame % 2 == 0; Array.Fill(y, orange ? (byte)150 : (byte)120); Array.Fill(u, orange ? (byte)70 : (byte)90); Array.Fill(v, orange ? (byte)180 : (byte)60);
            for (int bit = 0; bit < 16; bit++) for (int row = height - 20; row < height - 12; row++) for (int column = 10 + bit * 10; column < 18 + bit * 10; column++) { y[row * width + column] = (frame & (1 << bit)) != 0 ? (byte)235 : (byte)16; u[(row / 2) * (width / 2) + column / 2] = v[(row / 2) * (width / 2) + column / 2] = 128; }
            file.Write(y); file.Write(u); file.Write(v);
        }
    }
    internal static string[] Arguments(string output, string video, string title, string format = "bgra8") => ["--no-config", "--load-scripts=no", "--no-terminal", "--osc=no", "--audio=no", "--osd-level=0", "--vo=gpu", "--gpu-context=d3d11", "--d3d11-warp=no", "--d3d11-output-format=" + format, "--d3d11-output-csp=srgb", "--d3d11-sync-interval=0", "--geometry=640x360+40+40", "--border=no", "--title=" + title, "--keep-open=yes", "--loop-file=inf", "--pause=yes", "--save-position-on-quit=no", "--resume-playback=no", "--media-controls=no", "--stop-screensaver=no", "--config-dir=" + Path.Combine(output, "config"), "--gpu-shader-cache-dir=" + Path.Combine(output, "shader-cache"), "--log-file=" + Path.Combine(output, "mpv.log"), "--msg-level=all=v", video];
    internal static int Run(string output, string format = "bgra8", bool offset = false)
    {
        GpuBridgeVerification.PrepareOutput(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); string video = Path.Combine(output, "disposable.y4m"); MakeVideo(video);
        nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); GpuBridgeResearchSession? bridge = null;
        var events = new List<object>(); var checks = new List<object>(); var activity = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = []; int pid = 0; long root = 0;
        Dictionary<int, double> Cpu() => layer.GetProcessIds().Append(Environment.ProcessId).Distinct().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
        object[] Cost(Dictionary<int, double> before, double ms) => before.Select(pair => { using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value; return (object)new { pid = pair.Key, name = process.ProcessName, role = pair.Key == Environment.ProcessId ? "supervising verifier" : pair.Key == pid ? "ordinary application" : "wrapper infrastructure", cpuMs = cpu, machinePercent = cpu / ms * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0, workingSetMiB = process.WorkingSet64 / 1048576.0 }; }).ToArray();
        var physicalSamples = new List<object>(); using var physicalStop = new CancellationTokenSource();
        var physicalWatch = Stopwatch.StartNew(); var physicalTask = Task.Run(() =>
        {
            (long, int, int, uint?, string)? previous = null;
            while (!physicalStop.IsCancellationRequested)
            {
                nint current = Native.GetForegroundWindow(); Native.GetCursorPos(out var position); uint? tick = Native.LastInputTick(); string inputDesktop = Native.InputDesktopName();
                var sample = ((long)current, position.X, position.Y, tick, inputDesktop);
                if (previous != sample) { Native.GetWindowThreadProcessId(current, out uint foregroundPid); physicalSamples.Add(new { ms = physicalWatch.Elapsed.TotalMilliseconds, foreground = (long)current, foregroundPid, x = position.X, y = position.Y, inputTick = tick, desktop = inputDesktop }); previous = sample; }
                Thread.Sleep(25);
            }
        });
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        int Capture(string name, int expected = 0, int expectedWidth = 0, int expectedHeight = 0)
        {
            string path = Path.Combine(output, name + ".png"); var watch = Stopwatch.StartNew(); JsonElement info = default; int counter = 0, attempts = 0; bool cells = false; Color marker = default;
            do
            {
                info = bridge!.Capture(path, true); attempts++; counter = 0; cells = true;
                using (var image = new Bitmap(path))
                {
                    for (int bit = 0; bit < 16; bit++) { int x = (14 + bit * 10) * image.Width / 320, y = 164 * image.Height / 180; var cell = image.GetPixel(x, y); bool high = cell.R > 215 && cell.G > 215 && cell.B > 215; bool low = cell.R < 35 && cell.G < 35 && cell.B < 35; cells &= high || low; if (high) counter |= 1 << bit; }
                    marker = image.GetPixel(image.Width / 2, image.Height / 2);
                }
                bool size = expectedWidth == 0 || (info.GetProperty("width").GetInt32() == expectedWidth && info.GetProperty("height").GetInt32() == expectedHeight);
                if ((expected == 0 || counter == expected) && size) break; Thread.Sleep(40);
            } while (watch.ElapsedMilliseconds < 3000);
            double ms = watch.Elapsed.TotalMilliseconds; bool color = counter % 2 == 0 ? marker.R > 180 && marker.G > 80 && marker.G < 200 && marker.B < 120 : marker.G > 120 && marker.R < 110 && marker.B < 150;
            bool hidden = !layer.GetWindows().First(window => window.Handle == root).Visible;
            Check(name, cells && color && counter is >= 1 and <= 40 && (expected == 0 || counter == expected) && (expectedWidth == 0 || (info.GetProperty("width").GetInt32() == expectedWidth && info.GetProperty("height").GetInt32() == expectedHeight)) && hidden && layer.ApplicationProcessId == pid, new { counter, expected, hidden, ms, info, marker = new { marker.R, marker.G, marker.B } });
            events.Add(new { name, counter, expected, hidden, attempts, ms, info, bridge = bridge.State }); return counter;
        }
        try
        {
            string exe = Path.Combine(project, ".deps/mpv-7c4f662c0/mpv.exe"); string log = Path.Combine(output, "mpv.log");
            string[] arguments = Arguments(output, video, "VIAVW disposable mpv", format);
            pid = layer.LaunchApplication(exe, arguments, output); root = CompatibilityVerification.WaitRoot(layer, "VIAVW disposable mpv").Handle;
            Thread.Sleep(400); layer.HideLayer(root); bridge = new(layer, root, ResearchGpuTarget.Mpv);
            Check("capture-only adapter installed in pinned ordinary mpv", bridge.State.GetProperty("initialized").GetInt32() == 1 && bridge.State.GetProperty("keyAdapterInstalled").GetInt32() == 0);
            Capture("paused-first-frame", 1);
            string rendererLog = SnapshotFiles.ReadText(log);
            Check("actual hardware renderer and GPU format observed", rendererLog.Contains("Using Direct3D 11 feature level") && rendererLog.Contains("Device Name:") && !rendererLog.Contains("Device Name: Microsoft Basic Render Driver") && bridge.State.GetProperty("format").GetInt32() == (format == "rgba8" ? 28 : 87));
            int shift = offset ? 1 : 0; if (offset) { layer.SendKeyboardInput(root, "."); Capture("independent-offset-frame", 2); Thread.Sleep(220); }
            layer.SendKeyboardInput(root, "."); Capture("native-frame-step", 2 + shift); Thread.Sleep(220);
            layer.TakeHumanControl(); bool rejected = false; try { layer.SendKeyboardInput(root, "."); } catch (InvalidOperationException) { rejected = true; }
            layer.SendKeyboardInput(root, ".", ControlOwner.Human); Capture("human-frame-step", 3 + shift); Thread.Sleep(220); Check("native input obeys ownership", rejected);
            layer.RestartWorker(); Capture("worker-recovered-paused-frame", 3 + shift); Check("worker recovery preserves ordinary source and owner", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human);
            bridge.RestartHelper(); Capture("reader-recovered-paused-frame", 3 + shift); layer.ReturnToAutomation(); layer.SendKeyboardInput(root, "."); Capture("automation-resumed-frame-step", 4 + shift); Thread.Sleep(220);
            var activeCpu = Cpu(); var activeWatch = Stopwatch.StartNew(); var activeGpu = Task.Run(() => GpuCounters.Sample(layer.GetProcessIds()));
            layer.SendKey(root, 0x20); var counters = new List<int>(); for (int i = 0; i < 8; i++) { Thread.Sleep(210); counters.Add(Capture("playing-" + i)); }
            double activeMs = activeWatch.Elapsed.TotalMilliseconds; activity.Add(new { phase = "playing-and-inspecting", ms = activeMs, cpu = Cost(activeCpu, activeMs), gpu = activeGpu.GetAwaiter().GetResult(), observedFrames = counters });
            Check("native Space resumes ordinary video playback", counters.Distinct().Count() >= 4);
            layer.SendKey(root, 0x20); Thread.Sleep(250); int paused = Capture("paused-again"); Thread.Sleep(400); Capture("paused-retained", paused);
            int idleCopies = bridge.State.GetProperty("copied").GetInt32(); var idleCpu = Cpu(); var idleWatch = Stopwatch.StartNew(); var idleGpu = Task.Run(() => GpuCounters.Sample(layer.GetProcessIds())); Thread.Sleep(1700);
            activity.Add(new { phase = "paused-no-inspection", ms = idleWatch.Elapsed.TotalMilliseconds, cpu = Cost(idleCpu, idleWatch.Elapsed.TotalMilliseconds), gpu = idleGpu.GetAwaiter().GetResult() });
            Check("no GPU copies without inspection requests", idleCopies == bridge.State.GetProperty("copied").GetInt32());
            bridge.ResizeClient(480, 270); Capture("resized-small-paused", paused, 480, 270);
            bridge.ResizeClient(960, 540); Capture("resized-large-paused", paused, 960, 540);
            bridge.ResizeClient(640, 360); Capture("resized-restored-paused", paused, 640, 360);
            bridge.StopAdapter(); Check("adapter stop preserves ordinary application", CompatibilityVerification.Alive(pid) && bridge.State.GetProperty("initialized").GetInt32() == 0);
            File.WriteAllText(Path.Combine(output, "launch-arguments.json"), JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { bridge?.Dispose(); if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(300); physicalStop.Cancel(); physicalTask.GetAwaiter().GetResult(); }
        Native.GetCursorPos(out var afterPointer); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && desktopEquality, new { cleanup, foregroundEquality, pointerEquality, desktopEquality, graceful });
        File.WriteAllText(Path.Combine(output, "mpv-gpu.json"), JsonSerializer.Serialize(new { passed, failure, checks, events, activity, format, offset, desktopId = layer.Id, pid, root, owned, graceful, cleanup, foregroundEquality, pointerEquality, desktopEquality, physicalSamples, physicalInitial = new { foreground = (long)foreground, pointer.X, pointer.Y }, physicalFinal = new { foreground = (long)Native.GetForegroundWindow(), afterPointer.X, afterPointer.Y }, videoBytes = new FileInfo(video).Length, note = "Pinned first-party portable mpv build with ordinary D3D11 CLI selection; binary unchanged, process instrumented for capture only. No app plugin, JSON input control, physical input, driver, installation or registration. Video is a disposable Y4M file with image-encoded frame indices. Human owner is simulated; physical acceptance pending. Read-only physical samples are polled at 25 ms and changes retained; they are not a global input hook." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
    internal static int Concurrent(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); var watch = Stopwatch.StartNew();
        int[] results = Task.WhenAll(Task.Run(() => Run(Path.Combine(output, "bgra8"))), Task.Run(() => Run(Path.Combine(output, "rgba8"), "rgba8", true))).GetAwaiter().GetResult();
        using var leftDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "bgra8/mpv-gpu.json"))); using var rightDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "rgba8/mpv-gpu.json")));
        var left = leftDocument.RootElement; var right = rightDocument.RootElement;
        bool independent = left.GetProperty("pid").GetInt32() != right.GetProperty("pid").GetInt32() && left.GetProperty("desktopId").GetString() != right.GetProperty("desktopId").GetString();
        bool differentPausedFrames = left.GetProperty("events").EnumerateArray().First(frame => frame.GetProperty("name").GetString() == "human-frame-step").GetProperty("counter").GetInt32() + 1 == right.GetProperty("events").EnumerateArray().First(frame => frame.GetProperty("name").GetString() == "human-frame-step").GetProperty("counter").GetInt32();
        bool passed = results.All(code => code == 0) && independent && differentPausedFrames;
        File.WriteAllText(Path.Combine(output, "concurrent-mpv.json"), JsonSerializer.Serialize(new { passed, results, independent, differentPausedFrames, ms = watch.Elapsed.TotalMilliseconds, leftPid = left.GetProperty("pid").GetInt32(), rightPid = right.GetProperty("pid").GetInt32(), frames = left.GetProperty("events").GetArrayLength() + right.GetProperty("events").GetArrayLength(), cleanup = left.GetProperty("cleanup").GetBoolean() && right.GetProperty("cleanup").GetBoolean(), note = "Two complete ordinary mpv runs overlapped on different private desktops and jobs, with independent ownership, reader/worker recovery, resize and different paused frames. BGRA and RGBA variants use the same unchanged executable. Physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine((passed ? "PASS " : "FAIL ") + "two ordinary private GPU layers with independent retained frames"); return passed ? 0 : 1;
    }
}


using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace LayerHost;

internal static class QivResearch
{
    internal const string Sha256 = "852F10EFFE6A99C96B73EFD1720C56749C8AAA659C7057DA72F9D833AA0E5CD4";
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetClipCursor(out Native.RECT rectangle);
    internal static string Project => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal static string Executable => Path.Combine(Project, ".deps/qiv-3.2.0.452/QuickImageViewer.exe");
    internal static Native.RECT CursorClip() { Native.Check(GetClipCursor(out var value), "Read physical cursor confinement"); return value; }
    internal static string Prepare(string output, string id)
    {
        string folder = Path.Combine(output, "images"); Directory.CreateDirectory(folder);
        for (int image = 0; image < 2; image++)
        {
            Color[] colors = image == 0 ? [Color.FromArgb(220, 40, 50), Color.FromArgb(30, 200, 80), Color.FromArgb(40, 90, 230), Color.FromArgb(240, 190, 30)] : [Color.FromArgb(200, 40, 200), Color.FromArgb(20, 180, 210), Color.FromArgb(240, 120, 30), Color.FromArgb(180, 180, 180)];
            using var bitmap = new Bitmap(1200, 900); using var drawing = Graphics.FromImage(bitmap);
            for (int q = 0; q < 4; q++) { using var brush = new SolidBrush(colors[q]); drawing.FillRectangle(brush, q % 2 * 600, q / 2 * 450, 600, 450); }
            using var ink = new Pen(Color.White, 4); for (int x = 100; x < 1200; x += 100) drawing.DrawLine(ink, x, 0, x, 900); for (int y = 100; y < 900; y += 100) drawing.DrawLine(ink, 0, y, 1200, y);
            using var marker = new SolidBrush(Color.Black); drawing.FillRectangle(marker, 420, 310, 65, 65);
            bitmap.Save(Path.Combine(folder, image == 0 ? "01-quadrants.png" : "02-alternate.png"));
        }
        string ini = Path.Combine(output, "qiv.ini");
        File.WriteAllText(ini, $"[Instance]\r\nName=VIAVW_{id[6..]}\r\nDedicated=1\r\nMutex=Local\\VIAVW_QIV_{id[6..]}\r\n[Settings]\r\nqivDedFullscreen=0\r\nqivDedSlideshow=0\r\nqivDedLoop=0\r\nqivDedHideMouse=0\r\nqivRunOnStartup=0\r\nqivKeepInBackground=0\r\nqivKeepDisplayAwake=0\r\nqivStartFullscreen=0\r\nqivBaseWidth=640\r\nqivBaseHeight=360\r\nqivOverlayVisible=0\r\nqivOpenDirWndOnStart=0\r\nqivSwapMouseButtons=1\r\nqivVramCacheCount=2\r\nqivDirThumbCacheMB=1\r\nqivPreloadLookaside=1\r\nqivHistoryEnabled=0\r\nqivCtrlCEnabled=0\r\nqivContextMenu=0\r\n", Encoding.Unicode);
        return ini;
    }
    internal static string[] Arguments(string output, string ini) => ["-config", ini, "-windowedView", "-startFolder", Path.Combine(output, "images"), Path.Combine(output, "images/01-quadrants.png")];
    internal static void Wheel(ApplicationLayer layer, long root, int delta, int buttons = 0, ControlOwner owner = ControlOwner.Automation)
        => layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, 320, 180), new(MouseMessage.Wheel, 320, 180, buttons, delta), new(MouseMessage.Move, 320, 180)], false, owner);
    internal static string RegistryState()
    {
        object? Read(string path)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, false); if (key == null) return null;
            return new { values = key.GetValueNames().Order().Select(name => new { name, value = key.GetValue(name)?.ToString() }).ToArray(), children = key.GetSubKeyNames().Order().ToArray() };
        }
        using var run = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false);
        return JsonSerializer.Serialize(new { settings = Read("Software\\QuickImageViewer"), association = Read("Software\\Classes\\Applications\\QuickImageViewer.exe"), startup = run?.GetValue("QuickImageViewer")?.ToString() });
    }
    internal static JsonElement Pixels(string path)
    {
        using var image = SnapshotFiles.ReadBitmap(path); int colorArea = 0, red = 0, magenta = 0, dark = 0; double darkX = 0, darkY = 0;
        for (int y = 24; y < image.Height - 24; y += 3) for (int x = 6; x < image.Width - 6; x += 3)
        {
            var c = image.GetPixel(x, y); if (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 90) colorArea++;
            if (c.R > 170 && c.G < 90 && c.B < 100) red++;
            if (c.R > 140 && c.G < 100 && c.B > 130) magenta++;
            if (c.R < 10 && c.G < 10 && c.B < 10) { dark++; darkX += x; darkY += y; }
        }
        return JsonSerializer.SerializeToElement(new { image.Width, image.Height, colorArea, red, magenta, dark, darkX = dark == 0 ? 0 : darkX / dark, darkY = dark == 0 ? 0 : darkY / dark });
    }
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        string ini = Prepare(output, layer.Id), registryBefore = RegistryState();
        Native.GetCursorPos(out var pointer); nint foreground = Native.GetForegroundWindow(); uint? inputBefore = Native.LastInputTick(); string desktop = Native.InputDesktopName(); Native.Check(GetClipCursor(out var clip), "Read physical cursor confinement");
        var checks = new List<object>(); var frames = new List<object>(); var costs = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = []; int pid = 0; long root = 0; GpuBridgeResearchSession? gpu = null;
        var physicalChanges = new List<object>(); using var physicalStop = new CancellationTokenSource(); var physicalClock = Stopwatch.StartNew();
        var sampler = Task.Run(() =>
        {
            (long, int, int, int, int, int, int, uint?)? previous = null;
            while (!physicalStop.IsCancellationRequested)
            {
                Native.GetCursorPos(out var p); GetClipCursor(out var c); uint? tick = Native.LastInputTick(); var value = ((long)Native.GetForegroundWindow(), p.X, p.Y, c.Left, c.Top, c.Right, c.Bottom, tick);
                if (previous != value) { Native.GetWindowThreadProcessId((nint)value.Item1, out uint foregroundPid); physicalChanges.Add(new { ms = physicalClock.Elapsed.TotalMilliseconds, foreground = value.Item1, foregroundPid, p.X, p.Y, inputTick = tick, clip = new { c.Left, c.Top, c.Right, c.Bottom } }); previous = value; }
                Thread.Sleep(25);
            }
        });
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Wait(Func<bool> condition, string reason)
        {
            var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(40); } while (clock.ElapsedMilliseconds < 6000); throw new TimeoutException(reason);
        }
        JsonElement Capture(string name, Func<JsonElement, bool>? expected = null)
        {
            string path = Path.Combine(output, name + ".png"); JsonElement metrics = default, pixels = default; var clock = Stopwatch.StartNew(); int attempts = 0;
            do { metrics = gpu!.Capture(path, true); attempts++; pixels = Pixels(path); if (expected == null || expected(pixels)) break; Thread.Sleep(60); } while (clock.ElapsedMilliseconds < 3500);
            bool hidden = !layer.GetWindows().First(window => window.Handle == root).Visible;
            Check(name, hidden && (expected == null || expected(pixels)), new { pixels, hidden, attempts });
            frames.Add(new { name, metrics, pixels, attempts, ms = clock.Elapsed.TotalMilliseconds, guard = gpu!.CursorGuardState }); return pixels;
        }
        bool First(JsonElement pixels) => pixels.GetProperty("red").GetInt32() > 1000 && pixels.GetProperty("magenta").GetInt32() < 100;
        bool Second(JsonElement pixels) => pixels.GetProperty("magenta").GetInt32() > 1000 && pixels.GetProperty("red").GetInt32() < 100;
        void Wheel(int delta, int buttons = 0, ControlOwner owner = ControlOwner.Automation) => layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, 320, 180), new(MouseMessage.Wheel, 320, 180, buttons, delta), new(MouseMessage.Move, 320, 180)], false, owner);
        try
        {
            Check("pinned ordinary executable unchanged", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Executable))) == Sha256);
            pid = layer.LaunchApplication(Executable, Arguments(output, ini), output);
            string sourceClass = "QuickImageViewer_VIAVW_" + layer.Id[6..];
            Wait(() => { root = layer.GetWindows().FirstOrDefault(window => window.Parent == 0 && window.ProcessId == pid && window.ClassName == sourceClass)?.Handle ?? 0; return root != 0; }, "qIV owned configured source window missing");
            Thread.Sleep(500); File.WriteAllBytes(Path.Combine(output, "native-visible.png"), layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent).Png);
            gpu = new(layer, root, ResearchGpuTarget.Qiv); Check("cursor guard installed before mouse input", gpu.CursorGuardState.GetProperty("installed").GetInt32() == 1 && gpu.State.GetProperty("keyAdapterInstalled").GetInt32() == 0);
            layer.HideLayer(root); gpu.ResizeClient(640, 360); var first = Capture("hidden-first", First);
            var firstMetrics = JsonSerializer.SerializeToElement(frames[0]).GetProperty("metrics");
            Check("actual hardware adapter matched for GPU readback", !firstMetrics.GetProperty("softwareAdapter").GetBoolean() && !string.IsNullOrWhiteSpace(firstMetrics.GetProperty("adapterName").GetString()), firstMetrics.GetProperty("adapterName").GetString());
            var nativeControls = new List<object>();
            foreach (bool visible in new[] { false, true })
            {
                try
                {
                    if (visible) { layer.ShowLayer(root); Thread.Sleep(100); }
                    foreach (var backend in new[] { CaptureBackend.PrintWindow, CaptureBackend.PrintWindowFullContent })
                    {
                        string path = Path.Combine(output, $"native-control-{visible}-{backend}.png"); var control = layer.CaptureVisualOutput(root, backend); File.WriteAllBytes(path, control.Png);
                        nativeControls.Add(new { visible, control.Backend, control.CaptureMilliseconds, pixels = Pixels(path), file = Path.GetFileName(path) });
                    }
                }
                finally { layer.HideLayer(root); }
            }
            File.WriteAllText(Path.Combine(output, "native-capture-controls.json"), JsonSerializer.Serialize(nativeControls, new JsonSerializerOptions { WriteIndented = true }));
            Capture("after-native-controls", First);
            var activityCpu = layer.GetProcessIds().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; }); var activityClock = Stopwatch.StartNew();
            Wheel(-120); Capture("wheel-next", Second); Wheel(120); Capture("wheel-previous", First);
            Wheel(120, 8); Wheel(120, 8); Wheel(120, 8); var zoomed = Capture("control-wheel-zoom", First);
            Check("native Ctrl wheel enlarges actual GPU image", zoomed.GetProperty("colorArea").GetInt32() > first.GetProperty("colorArea").GetInt32() + 1000, new { first, zoomed });
            var dragged = layer.ResearchTransaction(ControlOwner.Automation, () =>
            {
                try
                {
                    layer.SendMouseEvent(root, MouseMessage.LeftDown, 320, 180, 1); layer.SendMouseEvent(root, MouseMessage.Move, 410, 230, 1);
                    return Capture("drag-held", First);
                }
                finally { layer.SendMouseEvent(root, MouseMessage.LeftUp, 410, 230); }
            });
            Capture("drag-released", First); Check("native pan moves visible fiducial", Math.Abs(dragged.GetProperty("darkX").GetDouble() - zoomed.GetProperty("darkX").GetDouble()) > 15 || Math.Abs(dragged.GetProperty("darkY").GetDouble() - zoomed.GetProperty("darkY").GetDouble()) > 15, new { zoomed, dragged });
            layer.TakeHumanControl(); bool rejected = false; try { Wheel(-120); } catch (InvalidOperationException) { rejected = true; } Check("non-owner mouse input rejected", rejected);
            Wheel(-120, owner: ControlOwner.Human); Capture("human-next", Second);
            double activityMs = activityClock.Elapsed.TotalMilliseconds;
            costs.Add(new { phase = "native-navigation-zoom-and-held-pan", ms = activityMs, processes = activityCpu.Select(pair =>
            {
                using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value;
                return new { pid = pair.Key, name = process.ProcessName, cpuMs = cpu, machinePercent = cpu / activityMs * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 };
            }).ToArray(), note = "Stable source, worker and reader processes before intentional recovery. Includes native input, PNG capture and source image decode; verifier CPU is excluded." });
            gpu.RestartHelper(); Capture("reader-recovered", Second); layer.RestartWorker(); Capture("worker-recovered", Second); Check("source and human owner survive recovery", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human);
            layer.ReturnToAutomation(); Wheel(120); Capture("automation-resumed", First);
            gpu.ResizeClient(480, 270); Capture("small-hidden", p => First(p) && p.GetProperty("Width").GetInt32() == 480 && p.GetProperty("Height").GetInt32() == 270);
            gpu.ResizeClient(960, 540); Capture("large-hidden", p => First(p) && p.GetProperty("Width").GetInt32() == 960 && p.GetProperty("Height").GetInt32() == 540);
            gpu.ResizeClient(640, 360); Capture("restored-hidden", p => First(p) && p.GetProperty("Width").GetInt32() == 640 && p.GetProperty("Height").GetInt32() == 360);
            // Refit with the ordinary numpad '*' shortcut, then a held click requests
            // temporary zoom and actual native confinement, which the guard denies.
            layer.SendKey(root, 0x6A); Thread.Sleep(150); var reset = Capture("reset-probe", First);
            var heldZoom = layer.ResearchTransaction(ControlOwner.Automation, () =>
            {
                try { layer.SendMouseEvent(root, MouseMessage.LeftDown, 320, 180, 1); return Capture("click-zoom-held", First); }
                finally { layer.SendMouseEvent(root, MouseMessage.LeftUp, 320, 180); }
            });
            var releasedZoom = Capture("click-zoom-released", First);
            Check("native held-click zoom and release restore pixels", heldZoom.GetProperty("colorArea").GetInt32() > reset.GetProperty("colorArea").GetInt32() + 1000 && Math.Abs(releasedZoom.GetProperty("darkX").GetDouble() - reset.GetProperty("darkX").GetDouble()) < 3 && Math.Abs(releasedZoom.GetProperty("darkY").GetDouble() - reset.GetProperty("darkY").GetDouble()) < 3);
            Check("source confinement requests blocked locally", gpu.CursorGuardState.GetProperty("clipRequests").GetInt32() > 0, gpu.CursorGuardState);
            var pacedCpu = layer.GetProcessIds().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; }); var pacedClock = Stopwatch.StartNew();
            var activeGpu = Task.Run(() => GpuCounters.Sample(pacedCpu.Keys.ToArray()));
            for (int i = 0; i < 16; i++) { bool second = i % 2 == 0; Wheel(second ? -120 : 120); Capture("paced-" + i.ToString("D2"), second ? Second : First); Thread.Sleep(100); }
            double pacedMs = pacedClock.Elapsed.TotalMilliseconds;
            costs.Add(new { phase = "paced-native-wheel-and-inspection", ms = pacedMs, processes = pacedCpu.Select(pair => { using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value; return new { pid = pair.Key, name = process.ProcessName, cpuMs = cpu, machinePercent = cpu / pacedMs * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray(), gpu = activeGpu.GetAwaiter().GetResult(), note = "16 real native wheel/capture cycles with 100 ms pacing. Not maximum throughput; supervising verifier CPU excluded. GPU engine sample has its own interval." });
            var idleState = gpu.State; int copies = idleState.GetProperty("copied").GetInt32(); var cpuBefore = layer.GetProcessIds().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
            var idleGpu = Task.Run(() => GpuCounters.Sample(cpuBefore.Keys.ToArray())); var idleClock = Stopwatch.StartNew(); Thread.Sleep(1700); double ms = idleClock.Elapsed.TotalMilliseconds;
            costs.Add(new { phase = "hidden-no-inspection", ms, processes = cpuBefore.Select(pair => { using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value; return new { pid = pair.Key, name = process.ProcessName, cpuMs = cpu, machinePercent = cpu / ms * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray(), gpu = idleGpu.GetAwaiter().GetResult(), before = idleState, after = gpu.State });
            Check("no copies between inspections", copies == gpu.State.GetProperty("copied").GetInt32());
            gpu.StopAdapter(); Check("capture stop preserves source and cursor guard", gpu.State.GetProperty("initialized").GetInt32() == 0 && gpu.CursorGuardState.GetProperty("installed").GetInt32() == 1 && CompatibilityVerification.Alive(pid));
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            gpu?.Dispose(); if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(250); physicalStop.Cancel(); sampler.GetAwaiter().GetResult();
        }
        string registryAfter = RegistryState(); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var after); GetClipCursor(out var afterClip);
        Check("registry targets unchanged", registryBefore == registryAfter, new { before = registryBefore, after = registryAfter });
        uint? inputAfter = Native.LastInputTick(); bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == after.X && pointer.Y == after.Y, inputDesktopEquality = desktop == Native.InputDesktopName(), clipEquality = clip.Equals(afterClip);
        var samples = JsonSerializer.SerializeToElement(physicalChanges).EnumerateArray().ToArray();
        bool confinementStable = samples.All(sample => { var c = sample.GetProperty("clip"); return c.GetProperty("Left").GetInt32() == clip.Left && c.GetProperty("Top").GetInt32() == clip.Top && c.GetProperty("Right").GetInt32() == clip.Right && c.GetProperty("Bottom").GetInt32() == clip.Bottom; });
        bool layerNeverForeground = samples.All(sample => !owned.Contains(sample.GetProperty("foregroundPid").GetInt32()));
        bool quietPointerPreserved = inputBefore != null && inputBefore == inputAfter ? pointerEquality : true;
        Check("owned cleanup, confinement and input desktop preserved", cleanup && inputDesktopEquality && clipEquality && confinementStable && layerNeverForeground && quietPointerPreserved && samples.Length > 0, new { cleanup, graceful, foregroundEquality, pointerEquality, inputBefore, inputAfter, inputDesktopEquality, clipEquality, confinementStable, layerNeverForeground, physicalSampleChanges = physicalChanges.Count, note = "Whole-run pointer/focus equality is reported separately when physical input activity occurs. Samples do not identify the origin of external input." });
        File.WriteAllText(Path.Combine(output, "qiv-research.json"), JsonSerializer.Serialize(new { passed, failure, checks, frames, costs, sourcePid = pid, root, owned, cleanup, graceful, physicalChanges, registryBefore, registryAfter, note = "Ordinary pinned qIV executable, dedicated file-backed built-in configuration, no app patch or plugin. Private native GPU capture plus executable-local guard denying ClipCursor/SetCursorPos; guard remains resident after capture stop until source exits. Native mouse/keyboard messages only. No qIV remote protocol, UIA mutation, clipboard, physical input, driver or persistent system-wide setting change. Human sender is simulated; physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}


using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class QivWindowViewerVerification
{
    static int ContentInk(string file)
    {
        using var image = new Bitmap(file); int count = 0;
        for (int y = 155; y < image.Height - 55; y += 2) for (int x = 28; x < image.Width - 45; x += 2) { var c = image.GetPixel(x, y); if (Math.Max(c.R, Math.Max(c.G, c.B)) > 130) count++; }
        return count;
    }
    internal static int Run(string output, bool nativeStartup = false, Barrier? pairing = null)
    {
        GpuBridgeVerification.PrepareOutput(output); using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var checks = new List<object>(); var frames = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = []; int sourcePid = 0; long viewer = 0, canvas = 0, chooser = 0;
        string registry = QivResearch.RegistryState(); var costs = new List<object>(); var pings = new List<double>();
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Wait(Func<bool> condition, string reason) { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(35); } while (clock.ElapsedMilliseconds < 10000); throw new TimeoutException(reason); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        string Kind() => Preview().GetProperty("surfaceKind").GetString()!;
        bool Status(string text) => qa.GetWindows().Any(w => w.Text.Contains(text, StringComparison.Ordinal));
        void Button(string text) { var button = qa.GetWindows().Single(w => w.Text == text); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(90); }
        JsonElement Inspect(string name, string kind, bool? second = null)
        {
            var clock = Stopwatch.StartNew(); JsonElement info = default; int attempts = 0; bool verified;
            do
            {
                long version = Preview().GetProperty("previewVersion").GetInt64(); Button("Inspect hidden app"); Wait(() => Preview().GetProperty("previewVersion").GetInt64() > version, "Inspection did not complete."); info = Preview(); attempts++;
                verified = info.GetProperty("surfaceKind").GetString() == kind;
                if (second != null) verified &= QivResearch.Pixels(Path.Combine(output, "viewer-preview.png")).GetProperty(second.Value ? "magenta" : "red").GetInt32() > 1000;
                if (!verified) Thread.Sleep(40);
            } while (!verified && clock.ElapsedMilliseconds < 3500);
            Check(name, verified && info.GetProperty("applicationPid").GetInt32() == sourcePid, new { info, attempts });
            File.WriteAllBytes(Path.Combine(output, name + ".png"), SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png"))); frames.Add(new { name, info, attempts }); return info;
        }
        void Select(string className)
        {
            var choices = Preview().GetProperty("availableSurfaces").EnumerateArray().ToArray(); int index = Array.FindIndex(choices, w => className == "main" ? w.GetProperty("Handle").GetInt64() == Preview().GetProperty("mainRoot").GetInt64() : w.GetProperty("ClassName").GetString() == className);
            if (index < 0) throw new InvalidOperationException("Viewer choice missing: " + className);
            qa.SendKey(chooser, 0x24); for (int i = 0; i < index; i++) qa.SendKey(chooser, 0x28);
            long expected = choices[index].GetProperty("Handle").GetInt64(); Wait(() => Preview().GetProperty("root").GetInt64() == expected, "Surface choice did not change.");
        }
        Point ViewPoint(int x, int y)
        { var p = Preview().GetProperty("presentation"); return new((int)Math.Ceiling((double)x * p.GetProperty("DrawWidth").GetInt32() / p.GetProperty("SourceWidth").GetInt32()), (int)Math.Ceiling((double)y * p.GetProperty("DrawHeight").GetInt32() / p.GetProperty("SourceHeight").GetInt32())); }
        void Click(int x, int y)
        { var p = ViewPoint(x, y); qa.SendQueueMouseSequence(canvas, [new(MouseMessage.Move, p.X, p.Y), new(MouseMessage.LeftDown, p.X, p.Y, 1), new(MouseMessage.LeftUp, p.X, p.Y)], true); Thread.Sleep(150); }
        string Highlight() => string.Join("|", Preview().GetProperty("menuSnapshot").GetProperty("items").EnumerateArray().Where(i => i.GetProperty("highlighted").GetBoolean()).Select(i => i.GetProperty("text").GetString()));
        try
        {
            string ready = Path.Combine(output, "session.json"); var args = new List<string> { "--demo", "--ready-file", ready, "--qa-report-directory", output, "--experimental-gpu-bridge", "qiv", "--root-input", "--queue-pointer" };
            if (nativeStartup) args.AddRange(["--qiv-native-startup", "--inspection-settle", "30"]); args.Add(QivResearch.Executable);
            qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
            Wait(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "Viewer startup failed.");
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle; canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            chooser = windows.Single(w => w.ClassName.Contains("COMBOBOX", StringComparison.OrdinalIgnoreCase) && w.Width is >= 210 and <= 230).Handle;
            sourcePid = Preview().GetProperty("applicationPid").GetInt32(); long main = Preview().GetProperty("mainRoot").GetInt64(); string reader = Preview().GetProperty("helperPid").ToString(), mapping = Preview().GetProperty("gpuMapping").ToString();
            var measuredIds = qa.GetProcessIds().Append(Environment.ProcessId).Distinct().ToArray(); var identities = ProcessCensus.Read(measuredIds);
            var cpuBefore = measuredIds.ToDictionary(id => id, id => { using var p = Process.GetProcessById(id); return p.TotalProcessorTime.TotalMilliseconds; }); var active = Stopwatch.StartNew();
            Inspect("initial", "main", false); Button("Automation: toggle help"); Button("Automation: toggle directory"); Button("Refresh");
            Wait(() => Preview().GetProperty("availableSurfaces").GetArrayLength() >= 3, "Modeless choices missing.");
            Check("three ordinary windows share one layer", Preview().GetProperty("availableSurfaces").EnumerateArray().All(w => w.GetProperty("ProcessId").GetInt32() == sourcePid));
            Select("QIV_HelpWindow"); var help = Inspect("help", "modeless"); int initialInk = ContentInk(Path.Combine(output, "help.png"));
            Check("oversized help fits canvas", help.GetProperty("presentation").GetProperty("DrawHeight").GetInt32() <= help.GetProperty("presentation").GetProperty("CanvasHeight").GetInt32() && help.GetProperty("presentation").GetProperty("DrawHeight").GetInt32() < help.GetProperty("Height").GetInt32());
            Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership missing."); Click(90, 107); qa.SendKeyboardInput(canvas, "VIAVW_UNLIKELY"); Thread.Sleep(180); Inspect("help-filtered", "modeless"); int filteredInk = ContentInk(Path.Combine(output, "help-filtered.png"));
            Check("scaled pointer and text filter ordinary help content", initialInk > 1000 && filteredInk < initialInk / 3, new { initialInk, filteredInk });
            Select("QIV_DirWindow"); var directory = Inspect("directory", "modeless");
            Check("wide directory is scaled to canvas", directory.GetProperty("presentation").GetProperty("DrawWidth").GetInt32() < directory.GetProperty("Width").GetInt32());
            Click(directory.GetProperty("Width").GetInt32() / 2 + 75, 60); Select("main"); Inspect("directory-selected-second", "main", true);
            Check("main capture resources survive modeless interaction", Preview().GetProperty("mainRoot").GetInt64() == main && Preview().GetProperty("helperPid").ToString() == reader && Preview().GetProperty("gpuMapping").ToString() == mapping && Preview().GetProperty("owner").GetString() == "Human");
            Button("Return to automation"); Wait(() => Status("Automation owns input"), "Automation ownership missing.");
            Button("Automation: open file dialog"); Wait(() => Kind() == "modal", "Picker not selected above modeless windows."); Inspect("picker-with-panels", "modal");
            Check("modal temporarily disables window chooser", !qa.GetWindows().Single(w => w.Handle == chooser).Enabled);
            Button("Automation: window menu"); Wait(() => Kind() == "menu", "Native menu not selected."); Inspect("menu", "menu");
            if (pairing != null) Check("two independent native menus overlap", pairing.SignalAndWait(TimeSpan.FromSeconds(12)));
            Button("Human control"); Wait(() => Status("Human owns input"), "Menu human ownership missing."); string before = Highlight(); qa.PostKey(canvas, 0x28); Thread.Sleep(120); Inspect("menu-down", "menu");
            Check("viewer arrow changes native menu selection", Highlight() != before, new { before, after = Highlight() });
            string recoveredHighlight = Highlight(); Button("Recover input worker"); Inspect("menu-worker-recovered", "menu"); Check("input-worker recovery preserves native menu and owner", Highlight() == recoveredHighlight && Preview().GetProperty("owner").GetString() == "Human");
            Button("Return to automation"); Wait(() => Status("Automation owns input"), "Menu automation ownership missing."); string held = Highlight(); qa.PostKey(canvas, 0x28); Thread.Sleep(150); Inspect("menu-nonowner", "menu"); Check("non-owner menu input ignored", Highlight() == held);
            Button("Human control"); qa.PostKey(canvas, 0x1B); Wait(() => Kind() == "modal", "Menu dismissal did not retain picker."); Inspect("menu-dismissed", "modal");
            qa.ResearchResizeWindow(viewer, 780, 620); Inspect("scaled-picker", "modal");
            var picker = Preview(); Check("picker shrinks to smaller viewer", picker.GetProperty("presentation").GetProperty("DrawWidth").GetInt32() < picker.GetProperty("Width").GetInt32());
            Click(300, picker.GetProperty("Height").GetInt32() - 60); qa.SendKeyboardInput(canvas, Path.Combine(output, "images", "01-quadrants.png")); Thread.Sleep(150); Inspect("scaled-picker-typed", "modal"); qa.PostKey(canvas, 0x0D); Wait(() => Kind() == "main", "Scaled filename input did not open image."); Inspect("scaled-first-opened", "main", false);
            Button("Return to automation"); Select("QIV_HelpWindow"); Button("Automation: toggle help"); Wait(() => Kind() == "main", "Hidden preferred panel did not restore main."); Inspect("hidden-panel-main", "main", false);
            Button("Automation: toggle directory"); Button("Refresh"); Check("hidden modeless windows leave chooser", Preview().GetProperty("availableSurfaces").GetArrayLength() == 1);
            Button("Human control");
            for (int i = 0; i < 3; i++) { var center = ViewPoint(320, 180); qa.SendQueueMouseSequence(canvas, [new(MouseMessage.Move, center.X, center.Y), new(MouseMessage.Wheel, center.X, center.Y, 8, 120), new(MouseMessage.Move, center.X, center.Y)], true); Thread.Sleep(90); }
            Inspect("before-gesture-resize", "main", false); var beforeResize = QivResearch.Pixels(Path.Combine(output, "before-gesture-resize.png"));
            var p = ViewPoint(320, 180); qa.SendMouseEvent(canvas, MouseMessage.LeftDown, p.X, p.Y, 1); Thread.Sleep(60);
            bool armed = qa.ObserveWindowThread(canvas).GetProperty("capture").GetInt64() == canvas; string oldHash = Convert.ToHexString(SHA256.HashData(SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png"))));
            qa.ResearchResizeWindow(viewer, 1050, 850); qa.SendMouseEvent(canvas, MouseMessage.Move, p.X + 60, p.Y + 35, 1); qa.SendMouseEvent(canvas, MouseMessage.LeftUp, p.X + 60, p.Y + 35); Inspect("resize-cancelled-gesture", "main", false);
            var afterResize = QivResearch.Pixels(Path.Combine(output, "resize-cancelled-gesture.png"));
            Check("viewer resize cancels armed gesture without source pan", armed && qa.ObserveWindowThread(canvas).GetProperty("capture").GetInt64() != canvas && Math.Abs(beforeResize.GetProperty("darkX").GetDouble() - afterResize.GetProperty("darkX").GetDouble()) < 3 && Math.Abs(beforeResize.GetProperty("darkY").GetDouble() - afterResize.GetProperty("darkY").GetDouble()) < 3, new { armed, oldHash, beforeResize, afterResize });
            Check("same running source survives full workflow", Preview().GetProperty("applicationPid").GetInt32() == sourcePid && Preview().GetProperty("mainRoot").GetInt64() == main);
            File.WriteAllBytes(Path.Combine(output, "actual-viewer.png"), qa.CaptureVisualOutput(viewer).Png);
            for (int i = 0; i < 8; i++) { var ping = Stopwatch.StartNew(); qa.ResearchProbeWindowResponse(viewer); pings.Add(ping.Elapsed.TotalMilliseconds); }
            double ms = active.Elapsed.TotalMilliseconds; var survivors = ProcessCensus.Read(measuredIds).Where(now => identities.Any(beforeIdentity => beforeIdentity.Pid == now.Pid && beforeIdentity.CreationFileTime == now.CreationFileTime)).ToArray();
            costs.Add(new { ms, processes = survivors.Select(entry => { using var process = Process.GetProcessById(entry.Pid); double cpuMs = process.TotalProcessorTime.TotalMilliseconds - cpuBefore[entry.Pid]; return new { pid = entry.Pid, name = process.ProcessName, supervisor = entry.Pid == Environment.ProcessId, cpuMs, machinePercent = cpuMs / ms * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray(), note = "Active end-to-end workflow including supervisor inspection/evidence CPU and pacing. Replaced worker omitted by creation identity; no claim of total GPU utilization." });
            owned = qa.GetProcessIds(); graceful = qa.CloseGracefully(TimeSpan.FromSeconds(5)); Check("graceful viewer and source cleanup", graceful && owned.All(id => !CompatibilityVerification.Alive(id)));
        }
        catch (Exception e) { passed = false; failure = e.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); if (!qa.Closed) { try { File.WriteAllText(Path.Combine(output, "failed-windows.json"), JsonSerializer.Serialize(qa.GetWindows(), new JsonSerializerOptions { WriteIndented = true })); } catch { } } }
        finally { if (!qa.Closed) { owned = qa.GetProcessIds(); qa.Dispose(); } Thread.Sleep(200); }
        var physical = monitor.Complete(owned); Check("physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical); Check("registry and owned cleanup", registry == QivResearch.RegistryState() && owned.All(id => !CompatibilityVerification.Alive(id)));
        File.WriteAllText(Path.Combine(output, "window-viewer.json"), JsonSerializer.Serialize(new { passed, failure, nativeStartup, checks, frames, costs, pings, sourcePid, owned, graceful, note = "Actual viewer with pinned ordinary qIV on private QA desktops. Human senders simulated. Preview scaling is tested without changing Windows display DPI. Native menu arrows and cancellation only; menu pointer invocation is not claimed." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
    internal static int Concurrent(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); using var pairing = new Barrier(2);
        int[] results = Task.WhenAll(Task.Run(() => Run(Path.Combine(output, "gpu"), false, pairing)), Task.Run(() => Run(Path.Combine(output, "native"), true, pairing))).GetAwaiter().GetResult();
        using var a = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "gpu", "window-viewer.json"))); using var b = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native", "window-viewer.json")));
        string Desktop(JsonElement r) => r.GetProperty("frames")[0].GetProperty("info").GetProperty("desktopId").GetString()!;
        bool independent = a.RootElement.GetProperty("sourcePid").GetInt32() != b.RootElement.GetProperty("sourcePid").GetInt32() && Desktop(a.RootElement) != Desktop(b.RootElement);
        bool passed = independent && results.All(r => r == 0);
        File.WriteAllText(Path.Combine(output, "concurrent-windows.json"), JsonSerializer.Serialize(new { passed, results, independent, aPid = a.RootElement.GetProperty("sourcePid"), bPid = b.RootElement.GetProperty("sourcePid"), aDesktop = Desktop(a.RootElement), bDesktop = Desktop(b.RootElement) }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

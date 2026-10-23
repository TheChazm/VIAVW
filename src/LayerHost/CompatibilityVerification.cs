using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class CompatibilityVerification
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static JsonElement Read(string directory)
    {
        for (int i = 0; ; i++)
        {
            try { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(directory, "observed.json"))); return doc.RootElement.Clone(); }
            catch (IOException) when (i < 20) { Thread.Sleep(25); }
        }
    }
    static string Text(JsonElement value) => value.TryGetProperty("text", out var v) ? v.GetString()! : value.GetProperty("textValue").GetString()!;
    static string String(JsonElement value, string name) => value.TryGetProperty(name, out var v) ? v.ToString() : "<unobserved>";
    static double Number(JsonElement value, string name) => value.TryGetProperty(name, out var v) && v.TryGetDouble(out double n) ? n : 0;
    internal static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
    internal static WindowInfo WaitRoot(ApplicationLayer layer, string title)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10000)
        {
            var root = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text == title);
            if (root != null) return root;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("No owned window: " + title);
    }
    internal static object PixelEvidence(CaptureFrame frame, string path, int x, int y, bool orange)
    {
        File.WriteAllBytes(path, frame.Png);
        using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream);
        var pixel = image.GetPixel(Math.Min(x, image.Width - 1), Math.Min(y, image.Height - 1));
        var expected = orange ? Color.FromArgb(242, 140, 40) : Color.FromArgb(40, 180, 100);
        bool correct = Math.Abs(pixel.R - expected.R) < 12 && Math.Abs(pixel.G - expected.G) < 12 && Math.Abs(pixel.B - expected.B) < 12;
        int black = 0, samples = 0;
        for (int yy = 0; yy < image.Height; yy += 20) for (int xx = 0; xx < image.Width; xx += 20) { var p = image.GetPixel(xx, yy); if (p.R < 5 && p.G < 5 && p.B < 5) black++; samples++; }
        return new { frame.Backend, path, frame.Width, frame.Height, frame.CaptureMilliseconds, sampleX = x, sampleY = y, pixel = new { pixel.R, pixel.G, pixel.B }, correctMarker = correct, blackFraction = (double)black / samples };
    }
    internal static int Framework(string name, string executable, string[] args, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>(); var captures = new List<object>();
        using var manager = new LayerManager(); var layer = manager.CreateLayer();
        var foregroundBefore = Native.GetForegroundWindow(); string desktopBefore = Native.InputDesktopName();
        object? finalReport = null;
        try
        {
            var startup = Stopwatch.StartNew(); int pid = layer.LaunchApplication(executable, args, output, name == "QuickSoftware" ? new() { ["QT_QUICK_BACKEND"] = "software" } : null);
            string title = name.StartsWith("Quick") ? "Disposable Qt Quick application" : name == "WPF" ? "Disposable WPF application" : "Disposable Direct3D application";
            var root = WaitRoot(layer, title); var initial = Read(output); startup.Stop();
            bool d3d = name == "D3D";
            int px = d3d ? 310 : 490, py = d3d ? 250 : 160;
            void Capture(string phase, CaptureBackend backend)
            {
                try { var frame = layer.CaptureVisualOutput(root.Handle, backend); captures.Add(new { phase, evidence = PixelEvidence(frame, Path.Combine(output, $"{phase}-{backend}.png"), px, py, Number(Read(output), "clicks") % 2 == 1) }); }
                catch (Exception e) { captures.Add(new { phase, backend = backend.ToString(), error = e.Message, targetPreserved = Alive(pid) }); }
            }
            void Observe(string operation, Action action, Func<JsonElement, bool> expected, string semantics)
            {
                var before = Read(output); var watch = Stopwatch.StartNew(); string? error = null;
                try { action(); } catch (Exception e) { error = e.Message; }
                JsonElement after = Read(output);
                while (error == null && !expected(after) && watch.ElapsedMilliseconds < 900) { Thread.Sleep(50); after = Read(output); }
                bool passed = error == null && expected(after);
                results.Add(new { operation, outcome = passed ? "working" : "not demonstrated", semantics, milliseconds = watch.Elapsed.TotalMilliseconds, before, after, error });
                Console.WriteLine($"{name}: {operation} = {(passed ? "working" : "not demonstrated")}");
            }
            Capture("before", CaptureBackend.PrintWindow);
            Capture("before", CaptureBackend.WindowsGraphicsCapture);
            Capture("before-default-helper", CaptureBackend.WindowsGraphicsCaptureFromDefault);
            Observe("text", () => layer.SendKeyboardInput(root.Handle, "M2_LAYER"), v => Text(v) == "M2_LAYER", "sent WM_CHAR, normal application text path");
            if (!d3d)
            {
                Observe("left navigation", () => layer.SendKey(root.Handle, 0x25), v => Number(v, "caret") == 7, "sent key pair");
                Observe("Tab focus", () => layer.PostKey(root.Handle, 9), v => String(v, name.StartsWith("Quick") ? "focusName" : "focus").Contains(name.StartsWith("Quick") ? "counter" : "Button"), "posted key pair through message-loop preprocessing");
                double count = Number(Read(output), "clicks");
                Observe("Enter button activation", () => layer.PostKey(root.Handle, 13), v => Number(v, "clicks") > count, "posted Enter, application default/focused button handling");
                if (name.StartsWith("Quick")) { double spaceBefore = Number(Read(output), "clicks"); Observe("Space button activation", () => layer.PostKey(root.Handle, 0x20), v => Number(v, "clicks") > spaceBefore, "posted Space, focused Qt Quick button handling"); }
                layer.SendMouseInput(root.Handle, 70, 80);
                Observe("Ctrl+A selection", () => layer.SendKeyChord(root.Handle, [0x11, 0x41]), v => String(v, "selection") == Text(v) && Text(v).Length > 0, "posted chord; no target keyboard-state synthesis");
                Observe("Shift+End selection", () => layer.SendKeyChord(root.Handle, [0x10, 0x23]), v => String(v, "selection").Length > 0 && String(v, "selection") != "<unobserved>", "posted chord; observe actual selection");
            }
            double shortcutCount = Number(Read(output), "shortcuts");
            Observe("Ctrl+K shortcut/state", () => layer.SendKeyChord(root.Handle, [0x11, 0x4B]), v => Number(v, "shortcuts") > shortcutCount, "real shortcut handler / GetKeyState-dependent behavior; no global state changes");
            if (!d3d) { double altCount = Number(Read(output), "altShortcuts"); Observe("Alt+K accelerator", () => layer.SendKeyChord(root.Handle, [0x12, 0x4B]), v => Number(v, "altShortcuts") > altCount, "posted WM_SYSKEYDOWN/UP; no shared keyboard-state changes"); }
            if (!d3d) { layer.CancelOwnedMenu(root.Handle); layer.PostKey(root.Handle, 27); Thread.Sleep(200); }
            double clickBefore = Number(Read(output), "clicks");
            Observe("left click", () => layer.SendMouseInput(root.Handle, 80, 150), v => Number(v, "clicks") > clickBefore, "sent mouse move/down/up");
            if (name == "WPF")
            {
                double queuedBefore = Number(Read(output), "clicks");
                Observe("queued left click", () => { layer.SendMouseEvent(root.Handle, MouseMessage.Move, 80, 150, posted: true); layer.SendMouseEvent(root.Handle, MouseMessage.LeftDown, 80, 150, 1, posted: true); layer.SendMouseEvent(root.Handle, MouseMessage.LeftUp, 80, 150, posted: true); }, v => Number(v, "clicks") > queuedBefore, "posted native mouse path");
                double invokedBefore = Number(Read(output), "clicks");
                Observe("accessible button action", () => layer.InvokeAccessibleAction(root.Handle, "Increment counter"), v => Number(v, "clicks") > invokedBefore, "explicit UI Automation Invoke adapter; not native mouse input");
            }
            if (Number(Read(output), "clicks") % 2 == 0) { layer.SendMouseInput(root.Handle, 80, 150); Thread.Sleep(250); }
            Capture("after", CaptureBackend.PrintWindow);
            Capture("after", CaptureBackend.WindowsGraphicsCapture);
            if (args.Contains("--enable-grab-adapter")) Capture("after", CaptureBackend.CooperativeFixture);
            if (name.StartsWith("Quick"))
            {
                string grabbed = Path.Combine(output, "quick-grab.png"); if (File.Exists(grabbed)) File.Delete(grabbed); File.WriteAllText(Path.Combine(output, "grab.request"), "capture");
                var wait = Stopwatch.StartNew(); while (!File.Exists(grabbed) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(50);
                if (File.Exists(grabbed)) { using var image = new Bitmap(grabbed); captures.Add(new { phase = "cooperative", evidence = PixelEvidence(new(File.ReadAllBytes(grabbed), image.Width, image.Height, wait.Elapsed.TotalMilliseconds, "QtQuick.grabWindow cooperative fixture adapter"), Path.Combine(output, "after-QtQuickGrab.png"), px, py, true) }); }
                else captures.Add(new { phase = "cooperative", error = "grabWindow produced no file" });
            }
            if (!d3d)
            {
                Observe("mouse hover", () => layer.SendMouseEvent(root.Handle, MouseMessage.Move, 80, 150), v => String(v, "hovered") == "True", "WM_MOUSEMOVE; no physical pointer relocation");
                double offset = Number(Read(output), "scrollOffset");
                Observe("wheel scrolling", () => layer.SendMouseEvent(root.Handle, MouseMessage.Wheel, 100, 280, wheelDelta: -120), v => Number(v, "scrollOffset") > offset, "WM_MOUSEWHEEL with screen coordinates; verify actual scroll offset");
                Observe("drag selection", () => { layer.SendMouseEvent(root.Handle, MouseMessage.LeftDown, 35, 85, 1); layer.SendMouseEvent(root.Handle, MouseMessage.Move, 180, 85, 1); layer.SendMouseEvent(root.Handle, MouseMessage.LeftUp, 180, 85); }, v => String(v, "selection").Length > 0 && String(v, "selection") != "<unobserved>", "native mouse messages including held-button wParam");
                Observe("double click selection", () => { layer.SendMouseInput(root.Handle, 65, 85); layer.SendMouseEvent(root.Handle, MouseMessage.LeftDoubleClick, 65, 85, 1); layer.SendMouseEvent(root.Handle, MouseMessage.LeftUp, 65, 85); }, v => String(v, "selection").Length > 0 && String(v, "selection") != "<unobserved>", "WM_LBUTTONDBLCLK; verify selected text");
                Observe("right-click context menu", () => { layer.SendMouseEvent(root.Handle, MouseMessage.RightDown, 65, 85, 2); layer.SendMouseEvent(root.Handle, MouseMessage.RightUp, 65, 85); }, v => String(v, "contextOpen") == "True", "right-button pair; normal context-menu path");
                layer.PostKey(root.Handle, 27); Thread.Sleep(250);
                Observe("modal dialog", () => { if (name == "WPF") layer.InvokeAccessibleAction(root.Handle, "Open dialog"); else layer.SendMouseInput(root.Handle, 80, 200); }, v => String(v, name.StartsWith("Quick") ? "dialogState" : "dialog") == "open", name == "WPF" ? "UI Automation Invoke adapter opens ordinary modal dialog" : "native mouse messages open ordinary modal dialog");
                var modalRoot = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Enabled && w.Text.Contains("modal review"));
                if (String(Read(output), name.StartsWith("Quick") ? "dialogState" : "dialog") == "open") Observe("Escape modal close", () => layer.PostKey(modalRoot?.Handle ?? root.Handle, 27), v => String(v, name.StartsWith("Quick") ? "dialogState" : "dialog") == "closed", "posted Escape through application dialog handling");
                else results.Add(new { operation = "Escape modal close", outcome = "unverified", reason = "Dialog never opened; no successful Escape assertion is inferred." });
                if (name == "WPF")
                {
                    for (int tab = 0; tab < 5 && String(Read(output), "focusName") != "editor"; tab++) { layer.PostKey(root.Handle, 9); Thread.Sleep(250); }
                }
                else layer.SendMouseInput(root.Handle, 70, 80);
                layer.SendKey(root.Handle, 0x23);
            }
            layer.RequestAttention("Compatibility review"); layer.TakeHumanControl(); bool blocked = false;
            try { layer.SendKeyboardInput(root.Handle, "BAD"); } catch (InvalidOperationException) { blocked = true; }
            layer.SendKeyboardInput(root.Handle, "H", ControlOwner.Human); layer.ReturnToAutomation(); layer.SendKeyboardInput(root.Handle, "R"); Thread.Sleep(300);
            var handoff = Read(output);
            results.Add(new { operation = "handoff", outcome = blocked && Text(handoff).Contains("HR") && Number(handoff, "pid") == pid ? "working" : "not demonstrated", wrongOwnerRejected = blocked, after = handoff });
            layer.HideLayer(root.Handle); double ticks = Number(Read(output), "ticks"); Thread.Sleep(1100); var hidden = Read(output);
            results.Add(new { operation = "hidden workload", outcome = Number(hidden, "ticks") > ticks ? "working" : "not demonstrated", after = hidden });
            Capture("hidden", CaptureBackend.PrintWindow); layer.ShowLayer(root.Handle);
            var processStats = layer.GetProcessIds().Where(Alive).Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, wrapper = layer.GetInfrastructureProcessIds().Contains(id), workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
            var final = Read(output); var cleanup = Stopwatch.StartNew(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(2));
            int[] remaining = layer.Closed ? [] : layer.GetProcessIds(); if (!graceful) layer.Dispose();
            finalReport = new { name, executable, args, startupMilliseconds = startup.Elapsed.TotalMilliseconds, initial, interactions = results, captures, final, processStats, gracefulClose = graceful, shutdownMilliseconds = cleanup.Elapsed.TotalMilliseconds, cleanupPids = remaining, inputDesktopUnchanged = Native.InputDesktopName() == desktopBefore, overallForegroundEquality = Native.GetForegroundWindow() == foregroundBefore, note = "Equality can change when the user legitimately switches Layer 0 windows. Unsupported interactions are recorded observations, not substituted global input." };
        }
        catch (Exception e) { finalReport = new { name, error = e.ToString(), interactions = results, captures }; }
        finally { layer.Dispose(); File.WriteAllText(Path.Combine(output, "compatibility.json"), JsonSerializer.Serialize(finalReport, Json)); }
        Console.WriteLine("Evidence: " + output); return finalReport!.GetType().GetProperty("error") == null ? 0 : 1;
    }
}

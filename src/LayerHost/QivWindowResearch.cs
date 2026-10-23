using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class QivWindowResearch
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        QivCursorGuardSession? guard = null; int[] owned = []; var checks = new List<object>(); var observations = new List<object>(); bool passed = true, graceful = false; string? failure = null;
        string registry = QivResearch.RegistryState();
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        WindowInfo Wait(Func<WindowInfo, bool> predicate)
        { var clock = Stopwatch.StartNew(); do { var found = layer.GetWindows().FirstOrDefault(predicate); if (found != null) return found; Thread.Sleep(40); } while (clock.ElapsedMilliseconds < 5000); throw new TimeoutException("Expected ordinary window missing."); }
        void Capture(string name, WindowInfo window)
        {
            foreach (var backend in new[] { CaptureBackend.PrintWindow, CaptureBackend.PrintWindowFullContent })
            {
                try { var f = layer.CaptureVisualOutput(window.Handle, backend); string file = name + "-" + backend + ".png"; File.WriteAllBytes(Path.Combine(output, file), f.Png); observations.Add(new { name, window, file, f.Width, f.Height, f.CaptureMilliseconds, thread = layer.ObserveWindowThread(window.Handle) }); }
                catch (Exception e) { observations.Add(new { name, window, backend, error = e.Message }); }
            }
        }
        try
        {
            string ini = QivResearch.Prepare(output, layer.Id); int pid = layer.LaunchApplication(QivResearch.Executable, QivResearch.Arguments(output, ini), output);
            long root = QivGuardResearch.WaitRoot(layer, pid); guard = new(layer, root); layer.HideLayer(root);
            layer.PostKey(root, 0x70); var help = Wait(w => w.Parent == 0 && w.Owner == root && w.Visible && w.ClassName.Contains("Help", StringComparison.OrdinalIgnoreCase)); Capture("help", help);
            layer.PostKey(root, 0x75); var directory = Wait(w => w.Parent == 0 && w.Owner == root && w.Visible && w.ClassName == "QIV_DirWindow"); Capture("directory", directory);
            Check("two ordinary modeless panels coexist with enabled main", layer.GetWindows().Any(w => w.Handle == help.Handle && w.Visible) && layer.GetWindows().Single(w => w.Handle == root).Enabled, new { help, directory });
            File.WriteAllText(Path.Combine(output, "panels-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), new JsonSerializerOptions { WriteIndented = true }));
            layer.PostKey(root, 0x70); layer.PostKey(root, 0x75);
            layer.PostKey(root, 0x71); var picker = Wait(w => w.Parent == 0 && w.Owner == root && w.Visible && w.ClassName == "#32770");
            long focused = layer.GetFocusedWindow(picker.Handle); layer.SendQueueStateChord(focused, [0x12, 0x4F], posted: true); Thread.Sleep(200);
            var state = layer.ObserveWindowThread(picker.Handle); var windows = layer.GetWindows();
            observations.Add(new { name = "organize-alt-o", state, windows });
            if (!windows.Any(w => w.Visible && w.ClassName == "#32768"))
            {
                var hit = layer.HitTest(picker.Handle, 35, 70);
                layer.SendMouseEvent(hit.Window, MouseMessage.Move, hit.X, hit.Y, posted: true); layer.SendMouseEvent(hit.Window, MouseMessage.LeftDown, hit.X, hit.Y, 1, posted: true); layer.SendMouseEvent(hit.Window, MouseMessage.LeftUp, hit.X, hit.Y, posted: true); Thread.Sleep(200);
                state = layer.ObserveWindowThread(picker.Handle); windows = layer.GetWindows(); observations.Add(new { name = "organize-posted-click", hit, state, windows });
            }
            var popup = windows.FirstOrDefault(w => w.ClassName == "#32768" && w.Visible);
            foreach (string method in new[] { "posted-alt-space", "system-menu-command", "semantic-organize-invoke" })
            {
                if (popup != null) break;
                try
                {
                    if (method == "posted-alt-space") layer.SendQueueStateChord(picker.Handle, [0x12, 0x20], posted: true);
                    else if (method == "system-menu-command") layer.ResearchOpenSystemMenu(picker.Handle);
                    else layer.InvokeAccessibleAction(picker.Handle, "Organize");
                    Thread.Sleep(250); state = layer.ObserveWindowThread(picker.Handle); windows = layer.GetWindows(); popup = windows.FirstOrDefault(w => w.Visible && w.ClassName == "#32768");
                    observations.Add(new { name = method, state, windows });
                }
                catch (Exception e) { observations.Add(new { name = method, error = e.Message }); }
            }
            Check("ordinary picker menu discovered", popup != null, state);
            if (popup != null)
            {
                Capture("system-menu", popup);
                JsonElement Menu(string name) { var menu = layer.ObserveMenu(popup.Handle); observations.Add(new { name, menu }); return menu; }
                string Highlight(JsonElement menu) => string.Join("|", menu.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("highlighted").GetBoolean()).Select(i => i.GetProperty("text").GetString()));
                var initial = Menu("initial-menu-state"); layer.PostKey(picker.Handle, 0x28); Thread.Sleep(80); var down = Menu("down-menu-state");
                Check("posted Down changes actual native menu highlight", Highlight(initial) != Highlight(down) && Highlight(down).Length > 0, new { initial = Highlight(initial), down = Highlight(down) });
                layer.TakeHumanControl(); bool rejected = false; try { layer.PostKey(picker.Handle, 0x28); } catch (InvalidOperationException) { rejected = true; }
                Check("native menu preserves input ownership", rejected && Highlight(Menu("rejected-menu-state")) == Highlight(down));
                layer.PostKey(picker.Handle, 0x28, ControlOwner.Human); Thread.Sleep(80); var human = Menu("human-menu-state");
                Check("human sender navigates same native menu", Highlight(human) != Highlight(down), new { down = Highlight(down), human = Highlight(human) });
                layer.PostKey(picker.Handle, 0x26, ControlOwner.Human); Thread.Sleep(80); var up = Menu("up-menu-state"); Check("native Up restores previous selection", Highlight(up) == Highlight(down));
                File.WriteAllBytes(Path.Combine(output, "highlighted-menu.png"), layer.CaptureVisualOutput(popup.Handle, CaptureBackend.PrintWindowFullContent).Png);
                layer.PostKey(picker.Handle, 0x1B, ControlOwner.Human); Thread.Sleep(100);
                var afterEscape = layer.GetWindows(); var escapeState = layer.ObserveWindowThread(picker.Handle);
                bool popupGone = !afterEscape.Any(w => w.Handle == popup.Handle && w.Visible), pickerAlive = afterEscape.Any(w => w.Handle == picker.Handle && w.Visible);
                Check("Escape dismisses only native popup", popupGone && pickerAlive, new { popupGone, pickerAlive, escapeState });
                layer.CancelOwnedMenu(picker.Handle, ControlOwner.Human); Thread.Sleep(80); var cancelledState = layer.ObserveWindowThread(picker.Handle);
                Check("explicit menu cancellation exits tracking", (cancelledState.GetProperty("flags").GetUInt32() & 4) == 0, cancelledState);
                layer.ReturnToAutomation();
            }
            File.WriteAllBytes(Path.Combine(output, "picker-with-menu.png"), layer.CaptureVisualOutput(picker.Handle, CaptureBackend.PrintWindowFullContent).Png);
            layer.CancelOwnedMenu(picker.Handle); Thread.Sleep(100); layer.PostKey(picker.Handle, 0x1B);
            Thread.Sleep(120); var afterPickerEscape = layer.GetWindows();
            observations.Add(new { name = "picker-escape-after-menu", stillPresent = afterPickerEscape.Any(w => w.Handle == picker.Handle), thread = layer.ObserveWindowThread(root) });
            if (afterPickerEscape.Any(w => w.Handle == picker.Handle))
            {
                var cancel = afterPickerEscape.Single(w => w.ClassName == "Button" && w.Text == "Cancel" && LayerWindowSelection.InSubtree(afterPickerEscape, w.Handle, picker.Handle));
                layer.SendMouseInput(cancel.Handle, 10, 10);
            }
            var closure = Stopwatch.StartNew(); while(layer.GetWindows().Any(w => w.Handle == picker.Handle) && closure.ElapsedMilliseconds < 3000) Thread.Sleep(30);
            Check("ordinary Cancel closes picker before application shutdown", layer.GetWindows().All(w => w.Handle != picker.Handle));
        }
        catch (Exception e) { passed = false; failure = e.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); guard?.Dispose(); Thread.Sleep(200); }
        var physical = monitor.Complete(owned); Check("physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical); Check("owned process and registry cleanup", owned.All(id => !CompatibilityVerification.Alive(id)) && registry == QivResearch.RegistryState(), new { graceful, owned });
        File.WriteAllText(Path.Combine(output, "windows.json"), JsonSerializer.Serialize(new { passed, failure, checks, observations, physical, owned, graceful }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

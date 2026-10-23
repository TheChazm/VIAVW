using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class VisualStudioMouseWorkflowResearch
{
    internal static int Run(string output, string seedFile, string mode)
    {
        if (mode is not ("hold" or "warmup" or "invoke" or "shift-drag")) throw new ArgumentException("Choose hold, warmup, shift-drag or explicit Invoke research.");
        foreach (var process in Process.GetProcessesByName("devenv")) using (process) throw new InvalidOperationException("An existing IDE must not be reused.");
        var setup = VisualStudioPrivateVerification.Prepare(output, seedFile);
        var beforeSettings = VisualStudioResearch.PersonalSettings();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var identities = new Dictionary<int, ProcessIdentity>(); var actions = new List<object>(); var checks = new Dictionary<string, bool>();
        long main = 0; int pid = 0; string stage = "startup"; string? failure = null;
        var json = new JsonSerializerOptions { WriteIndented = true };
        void Census() { foreach (var identity in ProcessCensus.Read(layer.GetProcessIds())) identities[identity.Pid] = identity; }
        void Wait(Func<bool> condition, string reason, int ms = 12000)
        { var clock = Stopwatch.StartNew(); do { Census(); if (condition()) return; Thread.Sleep(100); } while (clock.ElapsedMilliseconds < ms); throw new TimeoutException(reason); }
        JsonElement Observe(string name, long root)
        { var observation = layer.ObserveAccessible(root); File.WriteAllText(Path.Combine(output, name + ".json"), observation.GetRawText()); return observation; }
        JsonElement Find(JsonElement observation, Func<JsonElement, bool> condition) => observation.GetProperty("nodes").EnumerateArray().First(condition).Clone();
        bool EditorFocused(JsonElement observation) => observation.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("id").GetString() == "WpfTextView" && n.GetProperty("text").GetString() == File.ReadAllText(setup.First) && n.GetProperty("focused").GetBoolean());
        void Click(string name, long root, int x, int y)
        {
            var hit = layer.HitTest(root, x, y);
            var result = layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y)], true, layer.Owner, warmupCapture: mode is "warmup" or "shift-drag");
            actions.Add(new { name, root, x, y, hit = new { hit.Window, hit.X, hit.Y }, result }); Thread.Sleep(350);
        }
        void ClickNode(string name, long root, JsonElement node)
        { var origin = layer.ClientOrigin(root); Click(name, root, (int)(node.GetProperty("x").GetDouble() + node.GetProperty("width").GetDouble() / 2) - origin.X, (int)(node.GetProperty("y").GetDouble() + node.GetProperty("height").GetDouble() / 2) - origin.Y); }
        WindowInfo[] Snapshot(string name)
        {
            var windows = layer.GetWindows(); File.WriteAllText(Path.Combine(output, name + "-windows.json"), JsonSerializer.Serialize(windows, json));
            foreach (var root in windows.Where(w => w.Parent == 0 && w.Visible && w.Width > 2 && w.Height > 2))
            { File.WriteAllBytes(Path.Combine(output, name + "-" + root.Handle + ".png"), layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent).Png); Observe(name + "-" + root.Handle, root.Handle); }
            return windows;
        }
        try
        {
            pid = layer.LaunchApplication(setup.Executable, [..setup.Arguments.Where(a => a != "/SafeMode"), setup.First], output);
            Wait(() => { main = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text.Contains("VIAVWResearch -", StringComparison.Ordinal))?.Handle ?? 0; return main != 0; }, "IDE did not open.", 90000);
            Thread.Sleep(2500); var initial = Observe("01-startup", main); checks["startupEditorFocused"] = EditorFocused(initial); Snapshot("01-startup");
            layer.TakeHumanControl();
            stage = "click Output";
            var outputNode = Find(initial, n => n.GetProperty("name").GetString() == "Output" && n.GetProperty("type").GetString() == "ControlType.Pane" && n.GetProperty("width").GetDouble() > 100);
            ClickNode("Output", main, outputNode); var afterOutput = Observe("02-output", main); checks["outputTookFocus"] = !EditorFocused(afterOutput);
            stage = "click code after Output";
            var editor = Find(initial, n => n.GetProperty("id").GetString() == "WpfTextView" && n.GetProperty("text").GetString() == File.ReadAllText(setup.First)); var origin = layer.ClientOrigin(main);
            Click("code", main, (int)editor.GetProperty("x").GetDouble() + 120 - origin.X, (int)editor.GetProperty("y").GetDouble() + 8 - origin.Y);
            var afterCode = Observe("03-code", main); checks["editorRegainedFocus"] = EditorFocused(afterCode); Snapshot("03-code");
            if (mode == "shift-drag")
            {
                int startX = (int)editor.GetProperty("x").GetDouble() + 2 - origin.X, endX = startX + 110, y = (int)editor.GetProperty("y").GetDouble() + 8 - origin.Y;
                var hit = layer.HitTestPointer(main, startX, y);
                layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y), new(MouseMessage.Move, hit.X + 110, hit.Y, 4), new(MouseMessage.LeftDown, hit.X + 110, hit.Y, 5), new(MouseMessage.LeftUp, hit.X + 110, hit.Y, 4), new(MouseMessage.Move, hit.X + 110, hit.Y)], true, ControlOwner.Human, warmupCapture: true);
                Thread.Sleep(300); var drag = Observe("03-shift-drag", main);
                checks["shiftClickSelection"] = drag.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("id").GetString() == "WpfTextView" && n.GetProperty("selection").EnumerateArray().Any(s => (s.GetString() ?? "").Length > 0)); Snapshot("03-shift-drag");
            }
            stage = "Tools menu";
            var tools = Find(initial, n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "Tools");
            if (mode == "invoke") { layer.InvokeAccessibleAction(main, "Tools", ControlOwner.Human); Thread.Sleep(500); }
            else ClickNode("Tools", main, tools);
            var menuWindows = Snapshot("04-tools"); checks["menuSurfaceVisible"] = menuWindows.Any(w => w.Parent == 0 && w.Visible && w.Handle != main && w.Width > 40 && w.Height > 40);
            checks["optionsDialogOpened"] = false;
            foreach (var root in menuWindows.Where(w => w.Parent == 0 && w.Visible && w.Width > 40 && w.Height > 40))
            {
                var observation = Observe("04-options-search-" + root.Handle, root.Handle);
                var options = observation.GetProperty("nodes").EnumerateArray().FirstOrDefault(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && (n.GetProperty("name").GetString() ?? "").Replace("&", "").StartsWith("Options", StringComparison.Ordinal));
                if (options.ValueKind == JsonValueKind.Undefined) continue;
                stage = "Options command"; ClickNode("Options", root.Handle, options); Thread.Sleep(500);
                var dialog = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text == "Options"); checks["optionsDialogOpened"] = dialog != null;
                if (dialog != null)
                {
                    Snapshot("05-options"); var cancel = layer.GetWindows().FirstOrDefault(w => w.ClassName == "Button" && w.Text.Replace("&", "") == "Cancel" && LayerWindowSelection.InSubtree(layer.GetWindows(), w.Handle, dialog.Handle));
                    if (cancel != null) layer.SendMouseInput(cancel.Handle, 10, 10, ControlOwner.Human);
                    else layer.PostKey(layer.GetFocusedWindow(dialog.Handle), 0x1B, ControlOwner.Human);
                    Wait(() => !layer.GetWindows().Any(w => w.Handle == dialog.Handle && w.Visible), "Options dialog did not close.");
                }
                break;
            }
            stage = "complete";
        }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine(failure); if (main != 0 && !layer.Closed) try { Snapshot("failure"); } catch (Exception) { } }
        finally { if (!layer.Closed) { Census(); try { layer.CloseGracefully(TimeSpan.FromSeconds(5)); } catch (Exception) { } } layer.Dispose(); Thread.Sleep(300); }
        var survivors = ProcessCensus.Read(identities.Keys.ToArray()).Where(now => identities.TryGetValue(now.Pid, out var old) && old.CreationFileTime == now.CreationFileTime).ToArray();
        var physical = monitor.Complete(identities.Keys.ToArray()); var afterSettings = VisualStudioResearch.PersonalSettings(); bool settingsUnchanged = beforeSettings.Count == afterSettings.Count && beforeSettings.All(k => afterSettings.TryGetValue(k.Key, out string? value) && value == k.Value);
        bool completed = failure == null && survivors.Length == 0 && physical.GetProperty("passed").GetBoolean() && settingsUnchanged;
        File.WriteAllText(Path.Combine(output, "mouse-workflow.json"), JsonSerializer.Serialize(new { completed, mode, stage, failure, pid, checks, actions, survivors, physical, settingsUnchanged, physicalHumanAcceptance = "pending", semanticInput = mode == "invoke" ? "Explicit UIA Invoke of Tools; other input is native messages" : "none" }, json));
        Console.WriteLine(JsonSerializer.Serialize(new { completed, checks, output })); return completed ? 0 : 1;
    }
}

using System.Text.Json;

namespace LayerHost;

internal static class MouseStateVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var before = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); var results = new List<object>(); bool cleanup = true, rejected = true, wpfDemonstrated = false;
        using var manager = new LayerManager();
        foreach (string name in new[] { "Charmap", "WPF", "Quick", "D3D" })
        {
            string folder = Path.Combine(output, name); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer();
            string exe = name == "Charmap" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe") : name == "WPF" ? Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe") : name == "Quick" ? "C:/Python314/python.exe" : Path.Combine(project, "artifacts/native-build/D3DProbe.exe");
            layer.LaunchApplication(exe, name == "Quick" ? [Path.Combine(project, "experiments/quick_probe.py")] : [], folder);
            var root = CompatibilityVerification.WaitRoot(layer, name == "Charmap" ? "Character Map" : name == "WPF" ? "Disposable WPF application" : name == "Quick" ? "Disposable Qt Quick application" : "Disposable Direct3D application");
            long target = name == "Charmap" ? layer.GetWindows().First(w => w.ClassName == "RICHEDIT50W").Handle : root.Handle;
            layer.SendKeyboardInput(target, "M4_POINTER_SELECTION"); Thread.Sleep(200);
            JsonElement State()
            {
                if (name != "Charmap") return CompatibilityVerification.Read(folder);
                var selection = layer.GetControlSelection(target); return JsonSerializer.SerializeToElement(new { text = layer.GetWindows().First(w => w.Handle == target).Text, selection = new { selection.Start, selection.End } });
            }
            var cases = new List<object>();
            foreach (bool prime in new[] { false, true })
            {
                int clickX = name == "Charmap" ? 15 : name == "D3D" ? 310 : 80, clickY = name == "Charmap" ? 12 : name == "D3D" ? 250 : 150;
                foreach (string action in new[] { "click", "selection drag", "wheel" })
                {
                    var beforeState = State(); JsonElement? reply = null; string? error = null;
                    int x = name == "Charmap" ? 5 : 35, y = name == "Charmap" ? 12 : 80;
                    MouseStep[] steps = action == "click" ? [new(MouseMessage.Move, clickX, clickY), new(MouseMessage.LeftDown, clickX, clickY, 1), new(MouseMessage.LeftUp, clickX, clickY)] : action == "wheel" ? [new(MouseMessage.Move, 100, 280), new(MouseMessage.Wheel, 100, 280, Delta: -360)] : [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.Move, x + 40, y, 1), new(MouseMessage.Move, x + 100, y, 1), new(MouseMessage.LeftUp, x + 100, y)];
                    try { reply = layer.SendQueueMouseSequence(target, steps, prime); } catch (Exception e) { error = e.Message; }
                    Thread.Sleep(250); var after = State();
                    if (name == "WPF" && prime && action == "wheel") wpfDemonstrated = after.GetProperty("clicks").GetInt32() == 1 && after.GetProperty("selection").GetString()!.Length > 0 && after.GetProperty("scrollOffset").GetDouble() > 0;
                    cases.Add(new { action, prime, before = beforeState, after, reply, error });
                }
            }
            layer.TakeHumanControl(); bool denied = false; try { layer.SendQueueMouseSequence(target, [new(MouseMessage.Move, 1, 1)]); } catch (InvalidOperationException) { denied = true; } rejected &= denied;
            layer.SendQueueMouseSequence(target, [new(MouseMessage.Move, 1, 1)], sender: ControlOwner.Human); layer.ReturnToAutomation();
            if (name != "WPF") File.WriteAllBytes(Path.Combine(folder, "after.png"), layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent).Png);
            var ids = layer.GetProcessIds(); layer.Dispose(); Thread.Sleep(150); bool cleaned = ids.All(pid => !CompatibilityVerification.Alive(pid)); cleanup &= cleaned; results.Add(new { name, cases, ownershipRejected = denied, cleanup = cleaned });
        }
        Native.GetCursorPos(out var afterPointer);
        var report = new { results, cleanup, rejected, wpfDemonstrated, foregroundEquality = Native.GetForegroundWindow() == before, pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, note = "Atomic mouse sequences share only private GUI queue button/modifier state. Optional focus/capture priming is private-desktop scoped. No SetCursorPos, SendInput, global hook or asynchronous-device state changes. Results reflect actual widget state; receiving messages alone is insufficient." };
        File.WriteAllText(Path.Combine(output, "mouse-state.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, cleanup, rejected, wpfDemonstrated, report.foregroundEquality, report.pointerEquality })); return cleanup && rejected && wpfDemonstrated ? 0 : 1;
    }
}

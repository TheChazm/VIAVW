using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class HiddenFrameworkResearch
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick();
        var results = new List<object>(); bool completed = true; using var manager = new LayerManager();
        foreach (string framework in new[] { "Charmap", "WinForms", "WPF-layered", "QtQuick-GPU" }) foreach (bool notifications in new[] { false, true })
        {
            string folder = Path.Combine(output, framework, notifications ? "notification" : "direct"); Directory.CreateDirectory(folder);
            using var layer = manager.CreateLayer(); int[] ids = []; int pid = 0; long root = 0, target = 0; string? failure = null; bool baseline = false, selected = false, replaced = false, rejected = false, hidden = false, graceful = false; JsonElement? before = null, after = null, inspected = null;
            double inputMs = 0;
            try
            {
                string exe = framework switch { "Charmap" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe"), "WinForms" => Program.FindProbe(), "WPF-layered" => Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), _ => "C:/Python314/python.exe" };
                string[] args = framework switch { "WinForms" => ["--report"], "WPF-layered" => ["--layered"], "QtQuick-GPU" => [Path.Combine(project, "experiments/quick_probe.py")], _ => [] };
                string title = framework switch { "Charmap" => "Character Map", "WinForms" => "Disposable application", "WPF-layered" => "Disposable WPF application", _ => "Disposable Qt Quick application" };
                pid = layer.LaunchApplication(exe, args, folder); root = CompatibilityVerification.WaitRoot(layer, title).Handle;
                target = framework == "Charmap" ? layer.GetWindows().First(window => window.ClassName == "RICHEDIT50W").Handle : framework == "WinForms" ? layer.GetWindows().First(window => window.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase)).Handle : root;
                JsonElement State()
                {
                    if (framework is "Charmap" or "WinForms") { string text = layer.GetWindows().First(window => window.Handle == target).Text; var selection = layer.GetControlSelection(target); return JsonSerializer.SerializeToElement(new { text, selection = text.Substring(Math.Min(selection.Start, text.Length), Math.Max(0, Math.Min(selection.End, text.Length) - Math.Min(selection.Start, text.Length))), caret = selection.End }); }
                    var observed = CompatibilityVerification.Read(folder); return JsonSerializer.SerializeToElement(new { text = observed.GetProperty(framework == "WPF-layered" ? "text" : "textValue").GetString(), selection = observed.GetProperty("selection").GetString(), observed });
                }
                bool Wait(Func<JsonElement, bool> condition)
                {
                    var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < 1800) { if (condition(State())) return true; Thread.Sleep(25); } return false;
                }
                void SelectionKeys()
                {
                    layer.SendKey(target, 0x24); for (int i = 0; i < 3; i++) layer.SendKey(target, 0x27); for (int i = 0; i < 3; i++) layer.SendQueueStateChord(target, [0x10, 0x27]);
                }
                layer.SendKeyboardInput(target, "ABCDEFGHIJK"); bool seeded = Wait(state => state.GetProperty("text").GetString() == "ABCDEFGHIJK"); SelectionKeys(); baseline = seeded && Wait(state => state.GetProperty("selection").GetString() == "DEF");
                if (!baseline) throw new InvalidOperationException("Visible keyboard baseline not demonstrated.");
                layer.SendKey(target, 0x23); layer.HideLayer(root); Thread.Sleep(300); before = State();
                var watch = Stopwatch.StartNew();
                if (notifications)
                {
                    var keys = new List<PresentedInput> { new(PresentedInputKind.KeyChord, Window: target, Keys: [0x24]) };
                    for (int i = 0; i < 3; i++) keys.Add(new(PresentedInputKind.KeyChord, Window: target, Keys: [0x27]));
                    for (int i = 0; i < 3; i++) keys.Add(new(PresentedInputKind.KeyChord, Window: target, Keys: [0x10, 0x27]));
                    layer.ResearchHiddenInteraction(root, keys.ToArray(), settleMilliseconds: 0);
                }
                else SelectionKeys();
                inputMs = watch.Elapsed.TotalMilliseconds; selected = Wait(state => state.GetProperty("selection").GetString() == "DEF");
                if (notifications) layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.Text, Window: target, Text: "XYZ")], settleMilliseconds: 0); else layer.SendKeyboardInput(target, "XYZ");
                replaced = Wait(state => state.GetProperty("text").GetString() == "ABCXYZGHIJK"); after = State(); hidden = !layer.GetWindows().First(window => window.Handle == root).Visible;
                layer.TakeHumanControl(); try { if (notifications) layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.Text, Window: target, Text: "REJECT")]); else layer.SendKeyboardInput(target, "REJECT"); } catch (InvalidOperationException) { rejected = true; }
                var image = layer.InspectHiddenWindow(root, true); File.WriteAllBytes(Path.Combine(folder, "inspected.png"), image.Png); inspected = State();
                completed &= baseline && rejected && hidden && inspected.Value.GetProperty("text").GetString() == after.Value.GetProperty("text").GetString();
            }
            catch (Exception exception) { failure = exception.ToString(); completed = false; }
            finally { if (!layer.Closed) { ids = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(2)); } catch { } } layer.Dispose(); Thread.Sleep(150); }
            bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); completed &= cleanup;
            var result = new { framework, notifications, baseline, selected, replaced, ownerRejected = rejected, hidden, sourcePid = pid, root, target, inputMs, before, after, inspected, failure, ids, graceful, cleanup }; results.Add(result);
            Console.WriteLine(JsonSerializer.Serialize(new { framework, notifications, baseline, selected, replaced, hidden, failure, cleanup }));
        }
        Native.GetCursorPos(out var afterPointer); bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName(); completed &= foregroundEquality && pointerEquality && desktopEquality;
        File.WriteAllText(Path.Combine(output, "hidden-frameworks.json"), JsonSerializer.Serialize(new { completed, results, foregroundEquality, pointerEquality, desktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Ordinary installed Character Map plus passive fixtures using standard native EDIT, WPF layered GPU and Qt Quick default GPU controls. Each variant gets a fresh owned process. Hidden selection/replacement failures are retained compatibility outcomes; visible baseline, containment, ownership and cleanup are mandatory. No control-selection/model writes, UIA writes, source adapters, physical input or global activation. Native on-demand inspection is separate from hidden input." }, new JsonSerializerOptions { WriteIndented = true }));
        return completed ? 0 : 1;
    }
}

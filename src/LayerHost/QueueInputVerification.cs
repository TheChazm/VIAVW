using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class QueueInputVerification
{
    internal static int Run(string output, bool synchronousRegression = false)
    {
        Directory.CreateDirectory(output);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var results = new List<object>();
        bool allDemonstrated = true, allSynchronousDemonstrated = true, allCleaned = true;
        using var manager = new LayerManager();
        var physical = Native.GetForegroundWindow(); string inputDesktop = Native.InputDesktopName();
        foreach (string name in new[] { "Charmap", "WPF", "Quick", "D3D" })
        {
            string folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
            using var layer = manager.CreateLayer();
            string exe = name switch { "Charmap" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe"), "WPF" => Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), "Quick" => "C:/Python314/python.exe", _ => Path.Combine(project, "artifacts/native-build/D3DProbe.exe") };
            string[] args = name == "Quick" ? [Path.Combine(project, "experiments/quick_probe.py")] : [];
            int pid = layer.LaunchApplication(exe, args, folder, name == "Quick" ? new() { ["QT_QUICK_BACKEND"] = "software" } : null);
            string title = name switch { "Charmap" => "Character Map", "WPF" => "Disposable WPF application", "Quick" => "Disposable Qt Quick application", _ => "Disposable Direct3D application" };
            var root = CompatibilityVerification.WaitRoot(layer, title);
            long target = name == "Charmap" ? layer.GetWindows().First(w => w.ClassName == "RICHEDIT50W").Handle : root.Handle;
            layer.SendKeyboardInput(target, "M3_SELECTION"); Thread.Sleep(300);
            JsonElement State()
            {
                if (name != "Charmap") return CompatibilityVerification.Read(folder);
                var selected = layer.GetControlSelection(target);
                return JsonSerializer.SerializeToElement(new { text = layer.GetWindows().First(w => w.Handle == target).Text, selection = new { selected.Start, selected.End } });
            }
            // Root creation and delivery do not acknowledge the fixture's telemetry
            // publication. Establish the actual seed before comparing a selection.
            var seedWatch = Stopwatch.StartNew(); int seedObservations = 0; JsonElement seeded;
            do
            {
                seeded = State(); seedObservations++;
                string value = seeded.TryGetProperty("text", out var text) ? text.GetString()! : seeded.GetProperty("textValue").GetString()!;
                if (value == "M3_SELECTION") break;
                if (seedWatch.ElapsedMilliseconds >= 4000) throw new TimeoutException(name + " did not publish the keyboard seed: " + seeded);
                Thread.Sleep(25);
            } while (true);
            var cases = new List<object>();
            foreach (bool posted in new[] { false, true })
            {
                foreach (int[] keys in new[] { new[] { 0x11, 0x41 }, new[] { 0x11, 0x4B }, new[] { 0x10, 0x23 }, new[] { 0x12, 0x4B } })
                {
                    if (keys[0] == 0x10) { layer.SendKey(target, 0x24); Thread.Sleep(100); }
                    var before = State(); var clock = Stopwatch.StartNew(); string? error = null;
                    try { layer.SendQueueStateChord(target, keys, posted); } catch (Exception e) { error = e.Message; }
                    double operationMs = clock.Elapsed.TotalMilliseconds; Thread.Sleep(300);
                    var after = State();
                    bool? Demonstrated(JsonElement observed)
                    {
                        bool? result = null;
                        if (name is "WPF" or "Quick")
                        {
                        string textName = name == "WPF" ? "text" : "textValue";
                        result = keys[0] == 0x10 || keys[1] == 0x41 ? observed.GetProperty("selection").GetString() == before.GetProperty(textName).GetString() : observed.GetProperty(keys[0] == 0x12 ? "altShortcuts" : "shortcuts").GetInt32() > before.GetProperty(keys[0] == 0x12 ? "altShortcuts" : "shortcuts").GetInt32();
                        }
                        if (name == "Charmap" && (keys[1] == 0x41 || keys[0] == 0x10)) result = observed.GetProperty("selection").GetProperty("Start").GetInt32() == 0 && observed.GetProperty("selection").GetProperty("End").GetInt32() >= before.GetProperty("text").GetString()!.Length;
                        if (name == "D3D" && keys[0] == 0x11 && keys[1] == 0x4B) result = observed.GetProperty("shortcuts").GetInt32() > before.GetProperty("shortcuts").GetInt32() && observed.GetProperty("ctrlState").GetBoolean() && !observed.GetProperty("asyncCtrlState").GetBoolean();
                        return result;
                    }
                    bool? demonstrated = Demonstrated(after); var observation = Stopwatch.StartNew(); int observations = 1;
                    while (error == null && demonstrated == false && observation.ElapsedMilliseconds < 2000) { Thread.Sleep(50); after = State(); observations++; demonstrated = Demonstrated(after); }
                    if (demonstrated == false || error != null) allDemonstrated = false;
                    if (!posted && (demonstrated == false || error != null)) allSynchronousDemonstrated = false;
                    cases.Add(new { keys, posted, operationMs, demonstrated, observations, before, after, error });
                    if (keys[0] == 0x12) { layer.CancelOwnedMenu(root.Handle); layer.PostKey(target, 27); }
                }
            }
            layer.TakeHumanControl(); bool blocked = false;
            try { layer.SendQueueStateChord(target, [0x11, 0x41]); } catch (InvalidOperationException) { blocked = true; }
            layer.SendQueueStateChord(target, [0x11, 0x41], sender: ControlOwner.Human);
            layer.ReturnToAutomation(); layer.SendKeyboardInput(target, "M3_AFTER_HANDOFF"); Thread.Sleep(300);
            layer.SendKey(target, 0x25); Thread.Sleep(250); // Observe that Ctrl is released after the transaction.
            if (name is "Charmap" or "Quick") File.WriteAllBytes(Path.Combine(folder, "final.png"), layer.CaptureVisualOutput(root.Handle).Png);
            var final = State(); var ids = layer.GetProcessIds(); bool sameProcess = CompatibilityVerification.Alive(pid);
            layer.Dispose(); Thread.Sleep(150); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); allCleaned &= cleanup; allDemonstrated &= blocked && sameProcess;
            allSynchronousDemonstrated &= blocked && sameProcess;
            results.Add(new { name, pid, seedObservations, seeded, cases, handoffNonOwnerRejected = blocked, afterHandoff = final, sameProcess, cleanup });
            Console.WriteLine(name + ": queue-state experiments completed");
        }
        var report = new { allDemonstrated, allSynchronousDemonstrated, allCleaned, requiredMode = synchronousRegression ? "synchronous" : "all including posted research", results, foregroundEquality = Native.GetForegroundWindow() == physical, inputDesktopUnchanged = Native.InputDesktopName() == inputDesktop, note = "No SendInput or cursor movement. AttachThreadInput is guarded to owned non-input private desktops. Posted variant uses bounded delays, not a processing acknowledgement, and can lose modifiers under scheduling delay. Viewer uses synchronous chords. Poll resulting state for up to two seconds without resending. Null demonstrated means the fixture has no matching feature; it is not a compatibility success." };
        File.WriteAllText(Path.Combine(output, "queue-input.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return (synchronousRegression ? allSynchronousDemonstrated : allDemonstrated) && allCleaned ? 0 : 1;
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class QuickViewerVerification
{
    internal static int Run(string output, bool software, bool shortcuts = false, bool fullContent = false)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var manager = new LayerManager(); var outer = manager.CreateLayer();
        var args = new List<string> { "--demo", fullContent ? "--full-content" : software ? "--qt-software" : "--cooperative-fixture", @"C:\Python314\python.exe", Path.Combine(project, "experiments/quick_probe.py"), "--report" };
        if (!software && !fullContent) args.Add("--enable-grab-adapter");
        int viewerPid = outer.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
        var watch = Stopwatch.StartNew(); WindowInfo[] windows = [];
        while (watch.ElapsedMilliseconds < 12000)
        { windows = outer.GetWindows(); if (windows.Any(w => w.Text.StartsWith("Automation | PID"))) break; Thread.Sleep(100); }
        var root = windows.First(w => w.Text == "VIAVW — private application layer"); var canvas = windows.First(w => w.Parent == root.Handle && w.Width > 900 && w.Height > 200);
        void Click(string name) { var button = outer.GetWindows().First(w => w.Text == name); outer.SendMouseInput(button.Handle, 15, 12); }
        JsonElement Wait(Func<JsonElement, bool> condition, string operation)
        {
            var clock = Stopwatch.StartNew(); JsonElement state;
            do { state = CompatibilityVerification.Read(output); if (condition(state)) return state; Thread.Sleep(40); } while (clock.ElapsedMilliseconds < 8000);
            throw new TimeoutException($"Qt viewer {operation} did not reach the observed application state: {state}");
        }
        Click("Automation: send text"); Thread.Sleep(350); var automated = CompatibilityVerification.Read(output);
        Click("Human control"); Thread.Sleep(150);
        outer.SendMouseInput(canvas.Handle, 80, 80); outer.PostKeyboardInput(canvas.Handle, "HUMAN"); Thread.Sleep(450);
        outer.SendMouseInput(canvas.Handle, 80, 150); Thread.Sleep(350);
        var human = CompatibilityVerification.Read(output); outer.SendMouseInput(canvas.Handle, 80, 80); Thread.Sleep(150);
        JsonElement? shortcutState = null;
        if (shortcuts)
        {
            outer.SendQueueStateChord(canvas.Handle, [0x11, 0x41]);
            Wait(state => state.GetProperty("selection").GetString() == state.GetProperty("textValue").GetString(), "Ctrl+A selection");
            outer.PostKeyboardInput(canvas.Handle, "M3_HUMAN_REPLACEMENT");
            Wait(state => state.GetProperty("textValue").GetString() == "M3_HUMAN_REPLACEMENT", "replacement");
            outer.SendQueueStateChord(canvas.Handle, [0x11, 0x4B]);
            shortcutState = Wait(state => state.GetProperty("shortcuts").GetInt32() == 1, "Ctrl+K shortcut");
        }
        Click("Return to automation"); Thread.Sleep(100); string beforeRejected = CompatibilityVerification.Read(output).GetProperty("textValue").GetString()!;
        outer.PostKeyboardInput(canvas.Handle, "REJECT_ME"); Thread.Sleep(200); bool rejected = CompatibilityVerification.Read(output).GetProperty("textValue").GetString() == beforeRejected;
        Click("Automation: send text"); Thread.Sleep(400); var resumed = CompatibilityVerification.Read(output);
        var frame = outer.CaptureVisualOutput(root.Handle); File.WriteAllBytes(Path.Combine(output, "quick-viewer.png"), frame.Png);
        bool passed = automated.GetProperty("textValue").GetString() == "Hello from virtual input" && human.GetProperty("textValue").GetString()!.Contains("HUMAN") && human.GetProperty("clicks").GetInt32() == 1 && rejected && human.GetProperty("pid").GetInt32() == resumed.GetProperty("pid").GetInt32() && resumed.GetProperty("textValue").GetString()!.Length > human.GetProperty("textValue").GetString()!.Length;
        var pids = outer.GetProcessIds(); outer.Dispose(); watch.Restart(); while (pids.Any(CompatibilityVerification.Alive) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        bool cleanup = pids.All(p => !CompatibilityVerification.Alive(p));
        bool shortcutPassed = !shortcuts || (shortcutState!.Value.GetProperty("textValue").GetString() == "M3_HUMAN_REPLACEMENT" && shortcutState.Value.GetProperty("shortcuts").GetInt32() == 1);
        passed &= shortcutPassed;
        var report = new { passed, cleanup, software, fullContent, viewerPid, automated, human, resumed, shortcutState, shortcutPassed, wrongOwnerHumanInputRejected = rejected, note = "Actual wrapper UI exercised on private QA desktop. Full-content mode uses native GPU window capture with no grab adapter; other modes retain the M2/M3 choices." };
        File.WriteAllText(Path.Combine(output, "quick-viewer.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, passed, cleanup, software })); return passed && cleanup ? 0 : 1;
    }
}

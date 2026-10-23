using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class AdapterVerification
{
    static (long Foreground, int X, int Y, string Desktop) Sample()
    { Native.GetCursorPos(out var p); return ((long)Native.GetForegroundWindow(), p.X, p.Y, Native.InputDesktopName()); }
    static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
    internal static int Viewer(string probe, string output)
    {
        Directory.CreateDirectory(output);
        using var manager = new LayerManager(); var outer = manager.CreateLayer();
        // Exercise the real preview/control UI on another private desktop so QA does not
        // activate a window or send physical input on the user's desktop.
        outer.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), ["--demo", Path.GetFullPath(probe), "--report"], output);
        var watch = Stopwatch.StartNew(); WindowInfo[] windows = [];
        while (watch.ElapsedMilliseconds < 12000)
        {
            windows = outer.GetWindows();
            if (windows.Any(w => w.Text.StartsWith("Automation | PID"))) break;
            Thread.Sleep(100);
        }
        var root = windows.First(w => w.Text == "VIAVW — private application layer");
        var canvas = windows.First(w => w.Parent == root.Handle && w.Width >= 900 && w.Height >= 200);
        void Click(string name)
        {
            var button = outer.GetWindows().First(w => w.Text == name);
            outer.SendMouseInput(button.Handle, 15, 12);
        }
        JsonElement Read() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "probe-observed.json"))); return doc.RootElement.Clone(); }
        File.WriteAllBytes(Path.Combine(output, "viewer-before.png"), outer.CaptureVisualOutput(root.Handle).Png);
        Click("Automation: send text"); Thread.Sleep(350); var automated = Read();
        Click("Human control");
        outer.SendMouseInput(canvas.Handle, 55, 85);
        outer.PostKeyboardInput(canvas.Handle, "HUMAN");
        Thread.Sleep(200); // Queued UI input must be processed before choosing the next control.
        outer.SendMouseInput(canvas.Handle, 58, 236);
        outer.SendMouseInput(canvas.Handle, 55, 85); // Restore the intended EDIT target before automation resumes.
        Thread.Sleep(350); var human = Read();
        Click("Return to automation"); Click("Automation: send text"); Thread.Sleep(350);
        var returned = Read();
        File.WriteAllBytes(Path.Combine(output, "viewer-after.png"), outer.CaptureVisualOutput(root.Handle).Png);
        bool passed = automated.GetProperty("text").GetString() == "Hello from virtual input"
            && human.GetProperty("text").GetString()!.Contains("HUMAN") && human.GetProperty("clicks").GetInt32() == 1
            && returned.GetProperty("pid").GetInt32() == automated.GetProperty("pid").GetInt32()
            && returned.GetProperty("text").GetString()!.Length > human.GetProperty("text").GetString()!.Length;
        var pids = outer.GetProcessIds(); outer.Dispose(); watch.Restart();
        while (pids.Any(Alive) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        bool cleanup = pids.All(p => !Alive(p));
        File.WriteAllText(Path.Combine(output, "viewer-verification.json"), JsonSerializer.Serialize(new { passed, cleanup, automated, human, returned, note = "Actual UI controls driven by window messages on a private QA desktop; human-origin hardware typing is still a manual test." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, passed, cleanup, automated, human, returned }));
        return passed && cleanup ? 0 : 1;
    }
    internal static int Browser(string executable, string page, string output)
    {
        Directory.CreateDirectory(output);
        int[] pids = []; object? report = null; int result = 1;
        var before = Sample();
        using (var browser = new ChromiumLayer(executable, Path.Combine(output, "profile")))
        {
            var inputSamples = new List<object>();
            void Observe(string operation, Action action)
            {
                var start = Sample(); action(); var end = Sample();
                Native.GetWindowThreadProcessId((nint)end.Foreground, out uint foregroundPid);
                inputSamples.Add(new { operation, before = new { start.Foreground, start.X, start.Y, start.Desktop }, after = new { end.Foreground, end.X, end.Y, end.Desktop }, unchanged = start == end, foregroundOutsideLayer = !browser.GetProcessIds().Contains((int)foregroundPid) });
            }
            browser.Navigate(new Uri(Path.GetFullPath(page)).AbsoluteUri);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 8000)
            {
                try { if (browser.Inspect("typeof observations !== 'undefined'").GetBoolean()) break; } catch (LayerOperationException) { }
                Thread.Sleep(100);
            }
            File.WriteAllBytes(Path.Combine(output, "browser-before.png"), browser.CaptureVisualOutput().Png);
            Observe("click editor", () => browser.SendMouseInput(130, 165));
            var keyboardTime = Stopwatch.StartNew(); Observe("type", () => browser.SendKeyboardInput("BROWSER_LAYER")); keyboardTime.Stop();
            Observe("click counter", () => browser.SendMouseInput(130, 235));
            var observed = browser.Inspect("({text:editor.value, ...observations})");
            // Ownership handoff uses the same browser process, target, and page.
            string target = browser.TargetId; int pid = browser.ApplicationProcessId;
            browser.RequestAttention("Inspect this browser configuration."); browser.TakeHumanControl();
            bool blocked = false;
            try { browser.SendKeyboardInput("BAD"); } catch (InvalidOperationException) { blocked = true; }
            browser.SendMouseInput(130, 165, ControlOwner.Human);
            browser.SendKeyboardInput("H", ControlOwner.Human);
            browser.ReturnToAutomation(); browser.SendKeyboardInput("R");
            var final = browser.Inspect("({text:editor.value, ...observations})");
            var frame = browser.CaptureVisualOutput(); File.WriteAllBytes(Path.Combine(output, "browser-after.png"), frame.Png);
            int ticksBefore = final.GetProperty("ticks").GetInt32(); Thread.Sleep(1100);
            int ticksAfter = browser.Inspect("observations.ticks").GetInt32();
            var after = Sample();
            bool inputPassed = observed.GetProperty("text").GetString() == "BROWSER_LAYER" && observed.GetProperty("clicks").GetInt32() == 1;
            bool realEvents = observed.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("type").GetString() == "input" && e.GetProperty("trusted").GetBoolean());
            bool handoff = blocked && pid == browser.ApplicationProcessId && target == browser.TargetId && final.GetProperty("text").GetString()!.Contains("HR");
            browser.SendKey("a", "KeyA", 0x41, 2);
            var selected = browser.Inspect("({start:editor.selectionStart,end:editor.selectionEnd,length:editor.value.length})");
            bool ctrlA = selected.GetProperty("start").GetInt32() == 0 && selected.GetProperty("end").GetInt32() == selected.GetProperty("length").GetInt32();
            browser.SendKeyboardInput("CDP_REPLACEMENT");
            bool replaced = browser.Inspect("editor.value").GetString() == "CDP_REPLACEMENT";
            pids = browser.GetProcessIds();
            var processStats = pids.Where(Alive).Select(id => { using var p = Process.GetProcessById(id); return new { pid = id, name = p.ProcessName, workingSetMiB = p.WorkingSet64 / 1048576.0, privateMiB = p.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
            var samples = JsonSerializer.SerializeToElement(inputSamples).EnumerateArray().ToArray();
            bool layerNeverForeground = samples.All(s => s.GetProperty("foregroundOutsideLayer").GetBoolean());
            report = new { executable, version = FileVersionInfo.GetVersionInfo(executable).FileVersion, mechanism = "Chromium CDP Input.dispatchKeyEvent / Input.dispatchMouseEvent / Page.captureScreenshot; not native Windows device input", observed, final, inputPassed, realEvents, handoff, ctrlA, selected, replaced, sameBrowserPid = pid, sameTarget = target, hiddenTimerContinues = ticksAfter > ticksBefore, overallFocusEquality = before.Foreground == after.Foreground, overallPointerEquality = before.X == after.X && before.Y == after.Y, inputDesktopUnchanged = before.Desktop == after.Desktop, inputSamples, layerNeverForeground, keyboardBatchMs = keyboardTime.Elapsed.TotalMilliseconds, screenshotRoundTripMs = frame.CaptureMilliseconds, processStats, note = "User may legitimately switch Layer 0 windows or move their pointer over the whole experiment; before/after equality is reported, not attributed to the adapter." };
            result = inputPassed && realEvents && handoff && ctrlA && replaced && ticksAfter > ticksBefore && before.Desktop == after.Desktop && layerNeverForeground ? 0 : 1;
        }
        var cleanupWatch = Stopwatch.StartNew();
        while (pids.Any(Alive) && cleanupWatch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        bool cleanup = pids.All(p => !Alive(p));
        File.WriteAllText(Path.Combine(output, "browser-verification.json"), JsonSerializer.Serialize(new { result, cleanup, report }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, result, cleanup }));
        return cleanup ? result : 1;
    }

    internal static int Charmap(string output)
    {
        Directory.CreateDirectory(output);
        var before = Sample();
        using var manager = new LayerManager(); var layer = manager.CreateLayer();
        int pid = layer.LaunchApplication(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe"));
        WindowInfo[] windows = []; var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        { windows = layer.GetWindows(); if (windows.Any(w => w.Text == "&Select")) break; Thread.Sleep(100); }
        var root = windows.First(w => w.ClassName == "#32770" && w.Parent == 0);
        var editor = windows.First(w => w.ClassName == "RICHEDIT50W");
        var select = windows.First(w => w.Text == "&Select");
        File.WriteAllBytes(Path.Combine(output, "charmap-before.png"), layer.CaptureVisualOutput(root.Handle).Png);
        var inputSamples = new List<object>();
        void Observe(string operation, Action action)
        {
            var start = Sample(); action(); var end = Sample();
            Native.GetWindowThreadProcessId((nint)end.Foreground, out uint foregroundPid);
            inputSamples.Add(new { operation, before = new { start.Foreground, start.X, start.Y, start.Desktop }, after = new { end.Foreground, end.X, end.Y, end.Desktop }, unchanged = start == end, foregroundOutsideLayer = !layer.GetProcessIds().Contains((int)foregroundPid) });
        }
        Observe("type", () => layer.SendKeyboardInput(editor.Handle, "WIN32_LAYER"));
        var typed = layer.GetWindows().First(w => w.Handle == editor.Handle).Text;
        Observe("select glyph", () => layer.SendMouseInput(select.Handle, 30, 10)); // Does not touch the clipboard.
        var selected = layer.GetWindows().First(w => w.Handle == editor.Handle).Text;
        File.WriteAllBytes(Path.Combine(output, "charmap-after.png"), layer.CaptureVisualOutput(root.Handle).Png);
        var after = Sample();
        var samples = JsonSerializer.SerializeToElement(inputSamples).EnumerateArray().ToArray();
        bool passed = typed == "WIN32_LAYER" && selected.Length > typed.Length && before.Desktop == after.Desktop && samples.All(s => s.GetProperty("foregroundOutsideLayer").GetBoolean());
        var pids = layer.GetProcessIds(); layer.Dispose();
        watch.Restart(); while (pids.Any(Alive) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        var report = new { application = "Unmodified Windows Character Map", pid, typed, selected, characterSelectionClicked = selected.Length > typed.Length, focusUnchanged = before.Foreground == after.Foreground, pointerUnchanged = before.X == after.X && before.Y == after.Y, inputDesktopUnchanged = before.Desktop == after.Desktop, inputSamples, cleanup = pids.All(p => !Alive(p)), passed, note = "Whole-run pointer/focus equality can change while the user works on Layer 0. Input samples are preserved separately; only layer-owned foreground activation is treated as a routing failure." };
        File.WriteAllText(Path.Combine(output, "charmap-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report)); return passed ? 0 : 1;
    }

    internal static int Qt(string python, string fixture, string output)
    {
        Directory.CreateDirectory(output);
        var before = Sample();
        using var manager = new LayerManager(); var layer = manager.CreateLayer();
        int pid = layer.LaunchApplication(python, [Path.GetFullPath(fixture)], output);
        WindowInfo[] windows = []; var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10000)
        { windows = layer.GetWindows(); if (windows.Any(w => w.Text == "Disposable ordinary Qt Widgets app")) break; Thread.Sleep(100); }
        var root = windows.First(w => w.Text == "Disposable ordinary Qt Widgets app");
        string telemetry = Path.Combine(output, "qt-observed.json");
        JsonElement Read() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(telemetry)); return doc.RootElement.Clone(); }
        // Qt's ordinary children have no HWNDs: use root client coordinates and its focused widget.
        File.WriteAllBytes(Path.Combine(output, "qt-before.png"), layer.CaptureVisualOutput(root.Handle).Png);
        layer.SendMouseInput(root.Handle, 130, 120);
        layer.SendKeyboardInput(root.Handle, "QT_LAYER");
        layer.SendKey(root.Handle, 0x25);
        layer.SendMouseInput(root.Handle, 130, 205);
        Thread.Sleep(500);
        var observed = Read();
        File.WriteAllBytes(Path.Combine(output, "qt-after.png"), layer.CaptureVisualOutput(root.Handle).Png);
        layer.RequestAttention("Inspect Qt layer."); layer.TakeHumanControl();
        bool blocked = false;
        try { layer.SendKeyboardInput(root.Handle, "BAD"); } catch (InvalidOperationException) { blocked = true; }
        layer.SendMouseInput(root.Handle, 130, 120, ControlOwner.Human);
        layer.SendKeyboardInput(root.Handle, "H", ControlOwner.Human);
        layer.ReturnToAutomation(); layer.SendKeyboardInput(root.Handle, "R");
        Thread.Sleep(500); var final = Read();
        layer.HideLayer(root.Handle); int ticksBefore = final.GetProperty("ticks").GetInt32();
        Thread.Sleep(1100); var hidden = Read(); layer.ShowLayer(root.Handle);
        var after = Sample();
        bool keyboard = observed.GetProperty("text").GetString() == "QT_LAYER";
        bool mouse = observed.GetProperty("clicks").GetInt32() == 1;
        var pids = layer.GetProcessIds(); layer.Dispose();
        watch.Restart(); while (pids.Any(Alive) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        bool cleanup = pids.All(p => !Alive(p));
        var report = new { pid, observed, final, hidden, keyboard, mouse, handoffInputBlocked = blocked, handoffSameProcess = pid == final.GetProperty("pid").GetInt32(), hiddenTimerContinues = hidden.GetProperty("ticks").GetInt32() > ticksBefore, overallFocusEquality = before.Foreground == after.Foreground, overallPointerEquality = before.X == after.X && before.Y == after.Y, inputDesktopUnchanged = before.Desktop == after.Desktop, windows, cleanup, note = "Raw Win32 window-message experiment against ordinary Qt widget handling. No Qt automation or native-child forcing." };
        File.WriteAllText(Path.Combine(output, "qt-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, keyboard, mouse, cleanup, observed }));
        // A recorded incompatibility is a completed experiment, not a passing input test.
        return keyboard && mouse && cleanup ? 0 : 2;
    }
}

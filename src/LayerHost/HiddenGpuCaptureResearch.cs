using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class HiddenGpuCaptureResearch
{
    internal static int Run(string output, string browser = "chrome")
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var profile = new ResearchBrowserProfile(project, browser == "edge" ? "edge" : "main");
        using var server = new PassivePageServer(BrowserActivationVerification.Page(project));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var results = new List<object>(); var windows = new List<WindowInfo>(); string? failure = null; bool baseline = false, graceful = false, restored = true; int pid = 0; int[] owned = []; long root = 0;
        JsonElement State() => server.Observation ?? throw new InvalidOperationException("No passive page observation.");
        JsonElement Wait(Func<JsonElement, bool> condition, int limit = 6000)
        {
            var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < limit) { if (server.Observation is { } state && condition(state)) return state; Thread.Sleep(25); }
            throw new TimeoutException("Hidden GPU observation: " + server.Observation);
        }
        try
        {
            string exe = browser == "edge" ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : "C:/Program Files/Google/Chrome/Application/chrome.exe";
            pid = layer.LaunchApplication(exe, ["--user-data-dir=" + profile.Path, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,700"], output);
            Wait(state => state.GetProperty("webgl").GetBoolean(), 20000); windows.AddRange(layer.GetWindows());
            root = windows.Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
            var initial = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent); var rect = State().GetProperty("editor");
            int x = (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() / 2), y = (int)(rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2) + initial.Height - State().GetProperty("innerHeight").GetInt32();
            layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
            layer.SendKeyboardInput(root, "BASELINE"); Wait(state => state.GetProperty("text").GetString() == "BASELINE");
            baseline = State().GetProperty("renderer").GetString()!.Contains("NVIDIA") && PrivateBrowserVisibilityVerification.Pixels(initial, State(), Path.Combine(output, "initial.png")).GetProperty("colorCorrect").GetBoolean();
            layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden");
            var candidates = new List<(string Strategy, string Mode, long Target, string Label)>();
            foreach (var strategy in new[] { "framework", "style", "framework-style" }) foreach (var mode in strategy == "framework" ? new[] { "pw0", "pw1", "pw2", "pw3", "print-client", "print-window", "window-dc", "redraw-pw3" } : new[] { "pw3" }) candidates.Add((strategy, mode, 0, strategy));
            foreach (var child in windows.Where(window => window.Parent == root && window.ClassName is "Intermediate D3D Window" or "Chrome_RenderWidgetHostHWND")) candidates.Add(("framework", "pw3", child.Handle, child.ClassName == "Intermediate D3D Window" ? "d3d-child" : "legacy-child"));
            foreach (var candidate in candidates)
            {
                string strategy = candidate.Strategy, mode = candidate.Mode, label = candidate.Label;
                long captureTarget = candidate.Target;
                if (captureTarget != 0)
                {
                    string wantedClass = label == "d3d-child" ? "Intermediate D3D Window" : "Chrome_RenderWidgetHostHWND";
                    captureTarget = layer.GetWindows().FirstOrDefault(window => window.Parent == root && window.ClassName == wantedClass)?.Handle ?? 0;
                    if (captureTarget == 0) { results.Add(new { label, available = false, reason = "Child absent while source is hidden; no stale HWND used." }); Console.WriteLine(label + ": child absent while hidden"); continue; }
                }
                int changes = State().GetProperty("changes").GetInt32();
                layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: strategy + "_" + mode), new(PresentedInputKind.KeyChord, Keys: [0x9]), new(PresentedInputKind.KeyChord, Keys: [0x20]), new(PresentedInputKind.KeyChord, Keys: [0x10, 0x9])]);
                Wait(state => state.GetProperty("changes").GetInt32() == changes + 1 && state.GetProperty("visibility").GetString() == "hidden");
                var before = State(); var watch = Stopwatch.StartNew(); var trial = layer.ResearchHiddenCapture(root, strategy, mode, targetWindow: captureTarget); double rpcMs = watch.Elapsed.TotalMilliseconds;
                var after = Wait(state => state.GetProperty("visibility").GetString() == "hidden"); JsonElement? pixels = null; string? pixelError = null;
                if (trial.Frame != null) try { pixels = PrivateBrowserVisibilityVerification.Pixels(trial.Frame, after, Path.Combine(output, label + "-" + mode + ".png")); } catch (Exception exception) { pixelError = exception.Message; }
                bool color = pixels?.GetProperty("colorCorrect").GetBoolean() == true, fresh = color && pixels is { } p && p.GetProperty("encodedFrame").GetInt32() > before.GetProperty("frames").GetInt32() && p.GetProperty("encodedFrame").GetInt32() <= after.GetProperty("frames").GetInt32();
                var recovered = layer.InspectHiddenWindow(root, true); var recoveryState = Wait(state => state.GetProperty("visibility").GetString() == "hidden");
                bool recoveryPixels = PrivateBrowserVisibilityVerification.Pixels(recovered, recoveryState, Path.Combine(output, label + "-" + mode + "-recovery.png")).GetProperty("colorCorrect").GetBoolean();
                restored &= trial.NativeHiddenAfter && trial.StyleBefore == trial.StyleAfter && recoveryPixels && layer.ApplicationProcessId == pid;
                results.Add(new { strategy, label, target = captureTarget, available = true, mode, color, fresh, pixels, pixelError, rpcMs, trial.CaptureMode, trial.Error, trial.NativeVisibleBeforeCapture, trial.NativeVisibleAfterCapture, trial.NativeHiddenAfter, trial.StyleBefore, trial.StyleAfter, trial.Milliseconds, nativeMs = trial.Frame?.NativeRenderingMilliseconds, before, after, recoveryPixels });
                Console.WriteLine(JsonSerializer.Serialize(new { label, mode, color, fresh, trial.NativeVisibleBeforeCapture, recoveryPixels }));
                if (!restored) throw new InvalidOperationException("Hidden GPU experiment did not restore accepted state.");
            }
        }
        catch (Exception exception) { failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(300); }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        bool completed = baseline && restored && failure == null && cleanup && foregroundEquality && pointerEquality && desktopEquality;
        File.WriteAllText(Path.Combine(output, "hidden-gpu-capture.json"), JsonSerializer.Serialize(new { completed, browser, baseline, restored, failure, sourcePid = pid, profile = profile.Path, windows, results, graceful, owned, cleanup, foregroundEquality, pointerEquality, desktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Owned private source only. Framework-only keeps native visibility hidden; style variants deliberately set WS_VISIBLE during capture and restore that bit afterward, so they are temporary native visibility alternatives. Normal viewer paths remain unchanged. Passive page observations and native GPU binary pixels, no browser protocol/input hooks/global desktop capture. Individual negative mechanisms are retained outcomes. Whole-window captures include frame offsets and are diagnostic; verify saved images." }, new JsonSerializerOptions { WriteIndented = true }));
        return completed ? 0 : 1;
    }
}

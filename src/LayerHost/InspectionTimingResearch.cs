using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class InspectionTimingResearch
{
    internal static int Run(string output, string browser = "chrome")
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var profile = new ResearchBrowserProfile(project, browser == "edge" ? "edge" : "main"); using var server = new PassivePageServer(BrowserActivationVerification.Page(project));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); var samples = new List<JsonElement>(); var activity = new List<JsonElement>(); bool activityFresh = false; int[] owned = []; int pid = 0; long root = 0; bool baseline = false, restored = true, graceful = false; string? failure = null;
        JsonElement State() => server.Observation ?? throw new InvalidOperationException("No passive page observation.");
        JsonElement Wait(Func<JsonElement, bool> condition, int deadline = 6000)
        {
            var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < deadline) { if (server.Observation is { } state && condition(state)) return state; Thread.Sleep(20); } throw new TimeoutException("Inspection observation: " + server.Observation);
        }
        try
        {
            string exe = browser == "edge" ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : "C:/Program Files/Google/Chrome/Application/chrome.exe";
            pid = layer.LaunchApplication(exe, ["--user-data-dir=" + profile.Path, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,700"], output);
            Wait(state => state.GetProperty("webgl").GetBoolean(), 20000); root = layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
            var initial = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent); var rect = State().GetProperty("editor"); int x = (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() / 2), y = (int)(rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2) + initial.Height - State().GetProperty("innerHeight").GetInt32();
            layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
            layer.SendKeyboardInput(root, "INITIAL"); Wait(state => state.GetProperty("text").GetString() == "INITIAL"); baseline = State().GetProperty("renderer").GetString()!.Contains("NVIDIA"); layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden");
            var conditions = (from strategy in new[] { "show", "show-activation", "style-framework" } from settle in new[] { 0, 10, 30, 150 } select (strategy, settle)).ToArray();
            for (int round = 0; round < 6; round++) for (int index = 0; index < conditions.Length; index++)
            {
                var condition = conditions[(index + round * 5) % conditions.Length]; int changes = State().GetProperty("changes").GetInt32(); string text = "ROUND_" + round + "_" + index;
                layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: text), new(PresentedInputKind.KeyChord, Keys: [0x9]), new(PresentedInputKind.KeyChord, Keys: [0x20]), new(PresentedInputKind.KeyChord, Keys: [0x10, 0x9])]);
                var before = Wait(state => state.GetProperty("changes").GetInt32() == changes + 1 && state.GetProperty("text").GetString() == text && state.GetProperty("visibility").GetString() == "hidden");
                CaptureFrame frame; HiddenCaptureTrial? trial = null; var watch = Stopwatch.StartNew();
                if (condition.strategy == "show") frame = layer.InspectHiddenWindow(root, true, condition.settle);
                else if (condition.strategy == "show-activation") frame = layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "")], fullContent: true, settleMilliseconds: condition.settle, notifyActivation: true).Frame!;
                else { trial = layer.ResearchHiddenCapture(root, "framework-style", "pw3", condition.settle); frame = trial.Frame ?? throw new InvalidOperationException(trial.Error); }
                double rpcMs = watch.Elapsed.TotalMilliseconds; var after = Wait(state => state.GetProperty("visibility").GetString() == "hidden");
                string? path = round == 0 ? Path.Combine(output, condition.strategy + "-" + condition.settle + ".png") : null; var pixels = PrivateBrowserVisibilityVerification.Pixels(frame, after, path); int encoded = pixels.GetProperty("encodedFrame").GetInt32(); bool color = pixels.GetProperty("colorCorrect").GetBoolean(), fresh = color && encoded > before.GetProperty("frames").GetInt32() && encoded <= after.GetProperty("frames").GetInt32();
                bool hidden = !layer.GetWindows().First(window => window.Handle == root).Visible; restored &= hidden && layer.ApplicationProcessId == pid && (trial == null || trial.StyleBefore == trial.StyleAfter && trial.NativeHiddenAfter);
                samples.Add(JsonSerializer.SerializeToElement(new { round, condition.strategy, settle = condition.settle, rpcMs, frame.NativeRenderingMilliseconds, frame.EncodingMilliseconds, frame.Backend, color, fresh, hidden, encoded, framesBefore = before.GetProperty("frames"), framesAfter = after.GetProperty("frames"), sourceText = after.GetProperty("text"), nativeVisibleDuringStyleCapture = trial?.NativeVisibleBeforeCapture }));
                if (!restored) throw new InvalidOperationException("Inspection timing changed owned lifecycle or failed visibility restoration.");
            }
            var resources = InspectionResourceResearch.Run(layer, root, State, condition => Wait(condition)); activityFresh = resources.AllFresh; activity = resources.Phases;
        }
        catch (Exception exception) { failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(300); }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        var groups = samples.GroupBy(sample => (sample.GetProperty("strategy").GetString(), sample.GetProperty("settle").GetInt32())).Select(group => new { strategy = group.Key.Item1, settle = group.Key.Item2, count = group.Count(), correctColors = group.Count(sample => sample.GetProperty("color").GetBoolean()), freshFrames = group.Count(sample => sample.GetProperty("fresh").GetBoolean()), medianMs = group.Select(sample => sample.GetProperty("rpcMs").GetDouble()).Order().ElementAt(group.Count() / 2) }).ToArray();
        bool completed = baseline && restored && failure == null && cleanup && foregroundEquality && pointerEquality && desktopEquality;
        File.WriteAllText(Path.Combine(output, "inspection-timing.json"), JsonSerializer.Serialize(new { completed, browser, baseline, restored, failure, groups, samples, activityFresh, activity, sourcePid = pid, owned, graceful, cleanup, foregroundEquality, pointerEquality, desktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Balanced rotating conditions, six samples each, warm reusable project profile. Actual native input toggles GPU color before every capture; no debugging input. Fresh requires correct GPU color and a binary counter above the prior observation and no greater than the subsequent observation. Failed conditions are retained outcomes. Style-framework temporarily changes native visibility; all modes restore hidden state. Synchronous window returns are not application acknowledgements." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, completed, failure, groups, activityFresh, cleanup })); return completed ? 0 : 1;
    }
}

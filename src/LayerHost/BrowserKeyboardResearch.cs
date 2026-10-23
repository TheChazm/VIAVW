using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class BrowserKeyboardResearch
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        // One designated disposable research profile, retained and reused between runs.
        using var profile = new ResearchBrowserProfile(project);
        return RunOwned(output, profile.Path, project);
    }
    static int RunOwned(string output, string profile, string project)
    {
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputBefore = Native.LastInputTick(); string desktop = Native.InputDesktopName();
        var stages = new List<JsonElement>(); var checks = new List<object>(); bool baseline = false, graceful = false; string? failure = null; int[] owned = [];
        string html = File.ReadAllText(Path.Combine(project, "experiments/gpu-visibility.html"));
        html = html.Replace("text:editor.value,selectionStart", "activeElement:document.activeElement&&document.activeElement.id,text:editor.value,selectionStart");
        using var server = new PassivePageServer(html); using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        void Record(string name, object value) { stages.Add(JsonSerializer.SerializeToElement(new { name, utc = DateTime.UtcNow, value })); }
        JsonElement Latest() => server.Observation ?? throw new InvalidOperationException("No browser observation.");
        JsonElement Wait(Func<JsonElement, bool> condition, int deadline = 6000)
        {
            var watch = Stopwatch.StartNew(); JsonElement? state = null;
            while (watch.ElapsedMilliseconds < deadline) { state = server.Observation; if (state != null && condition(state.Value)) return state.Value; Thread.Sleep(25); }
            throw new TimeoutException("Browser observation deadline: " + state);
        }
        long root = 0; int height = 0, pid = 0;
        void Click(string target)
        {
            var rectangle = Latest().GetProperty(target); int x = (int)(rectangle.GetProperty("x").GetDouble() + rectangle.GetProperty("width").GetDouble() / 2), y = (int)(rectangle.GetProperty("y").GetDouble() + rectangle.GetProperty("height").GetDouble() / 2) + height - Latest().GetProperty("innerHeight").GetInt32();
            layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
        }
        void Seed(string text)
        {
            layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible"); Click("editor");
            layer.SendQueueStateChord(root, [0x11, 0x41]); layer.SendKeyboardInput(root, text); Wait(state => state.GetProperty("text").GetString() == text);
        }
        try
        {
            pid = layer.LaunchApplication("C:/Program Files/Google/Chrome/Application/chrome.exe", ["--user-data-dir=" + profile, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,660"], output);
            owned = layer.GetProcessIds(); Wait(state => state.GetProperty("webgl").GetBoolean(), 20000);
            root = layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
            height = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent).Height; Seed("BASELINE"); baseline = Latest().GetProperty("renderer").GetString()!.Contains("NVIDIA");
            Record("baseline", new { baseline, pid, state = Latest(), gui = layer.ResearchActivation(root, "observe") });
            foreach (string mode in new[] { "show-hide-control", "position-hide", "position-hide-show", "active-focus", "active-pulse", "activate-notification", "activate-only", "clickactivate-only", "deactivate-only", "hidden-activation" })
            {
                Seed("SEED_" + mode); var before = Latest(); var beforeGui = layer.ResearchActivation(root, "observe");
                bool positionHide = mode.StartsWith("position-"); if (positionHide) layer.ResearchActivation(root, "hide-noactivate"); else layer.HideLayer(root);
                Wait(state => state.GetProperty("visibility").GetString() == "hidden"); var hiddenGui = layer.ResearchActivation(root, "observe");
                var watch = Stopwatch.StartNew();
                if (mode != "hidden-activation")
                {
                    if (mode == "position-hide-show") layer.ResearchActivation(root, "show-noactivate"); else layer.ShowLayer(root);
                    Wait(state => state.GetProperty("visibility").GetString() == "visible"); Thread.Sleep(150);
                }
                JsonElement? action = mode == "hidden-activation" ? layer.ResearchActivation(root, "activate-notification") : mode is "active-focus" or "active-pulse" or "activate-notification" or "activate-only" or "clickactivate-only" or "deactivate-only" ? layer.ResearchActivation(root, mode) : null;
                string expected = "SEED_" + mode + "_RESUMED"; layer.SendKeyboardInput(root, "_RESUMED"); bool resumed = false;
                try { Wait(state => state.GetProperty("text").GetString() == expected, 1500); resumed = true; } catch (TimeoutException) { }
                var frame = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent); File.WriteAllBytes(Path.Combine(output, mode + ".png"), frame.Png);
                var after = Latest(); var afterGui = layer.ResearchActivation(root, "observe");
                Record(mode, new { resumed, ms = watch.Elapsed.TotalMilliseconds, before, beforeGui, hiddenGui, action, after, afterGui, sourceVisible = layer.GetWindows().First(window => window.Handle == root).Visible, samePid = layer.ApplicationProcessId == pid });
                checks.Add(new { mode, resumed }); Console.WriteLine(JsonSerializer.Serialize(new { mode, resumed, gui = afterGui }));
            }
        }
        catch (Exception exception) { failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            if (!layer.Closed) { owned = owned.Concat(layer.GetProcessIds()).Distinct().ToArray(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch (Exception exception) { Record("close-failure", exception.ToString()); } }
            layer.Dispose(); Thread.Sleep(300);
        }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, inputDesktopEquality = desktop == Native.InputDesktopName();
        bool completed = baseline && failure == null && cleanup && foregroundEquality && pointerEquality && inputDesktopEquality;
        File.WriteAllText(Path.Combine(output, "browser-keyboard-research.json"), JsonSerializer.Serialize(new { completed, failure, baseline, checks, stages, profile, sourcePid = pid, owned, graceful, cleanup, foregroundEquality, pointerEquality, inputDesktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Unchanged installed Chrome on a non-input private desktop. Native messages, private queue activation and alternative private visibility APIs only. No pointer writes, global input, process injection, browser control protocol or physical-desktop activation. Disposable local page observations only. Designated disposable profile reused to limit storage; warm state is an experimental confounder. Failed modes are research outcomes, not required functionality." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, completed, failure, graceful, cleanup, foregroundEquality, pointerEquality })); return completed ? 0 : 1;
    }
}

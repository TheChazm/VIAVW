using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Bounded notification comparisons. Never selected by the normal viewer.
internal static class HiddenShortcutResearch
{
    internal static int Run(string output, string browser = "chrome")
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var profile = new ResearchBrowserProfile(project, browser == "edge" ? "edge" : "main");
        var reports = new List<JsonElement>(); bool completed = true;
        foreach (string mode in new[] { "activation", "posted-activation", "show-notification", "size-pulse", "size-pulse-activation", "position-show-notification", "position-show-activation" })
        {
            string folder = Path.Combine(output, mode); Directory.CreateDirectory(folder);
            int code = RunOne(folder, project, profile.Path, browser, mode); completed &= code == 0;
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "hidden-shortcuts.json"))); reports.Add(document.RootElement.Clone());
            if (!document.RootElement.GetProperty("cleanup").GetBoolean()) break;
        }
        File.WriteAllText(Path.Combine(output, "hidden-shortcuts.json"), JsonSerializer.Serialize(new { completed, browser, reports }, new JsonSerializerOptions { WriteIndented = true }));
        return completed ? 0 : 1;
    }
    static int RunOne(string output, string project, string profile, string browser, string experiment)
    {
        using var server = new PassivePageServer(BrowserActivationVerification.Page(project));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var results = new List<object>(); bool baseline = false, graceful = false; string? failure = null; int[] owned = []; long root = 0; int pid = 0;
        JsonElement State() => server.Observation ?? throw new InvalidOperationException("No passive state.");
        JsonElement Wait(Func<JsonElement, bool> condition, int limit = 4000)
        {
            var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < limit) { if (server.Observation is { } state && condition(state)) return state; Thread.Sleep(25); }
            throw new TimeoutException("Hidden shortcut observation: " + server.Observation);
        }
        bool Hidden() => !layer.GetWindows().First(window => window.Handle == root).Visible;
        try
        {
            string exe = browser == "edge" ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : "C:/Program Files/Google/Chrome/Application/chrome.exe";
            pid = layer.LaunchApplication(exe, ["--user-data-dir=" + profile, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,660"], output);
            Wait(state => state.GetProperty("webgl").GetBoolean(), 20000);
            root = layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
            var frame = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent); var rect = State().GetProperty("editor");
            int x = (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() / 2), y = (int)(rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2) + frame.Height - State().GetProperty("innerHeight").GetInt32();
            layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
            layer.SendKeyboardInput(root, "INITIAL"); Wait(state => state.GetProperty("text").GetString() == "INITIAL");
            baseline = State().GetProperty("renderer").GetString()!.Contains("NVIDIA"); layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden");
            foreach (string mode in new[] { experiment })
            {
                // Restore genuine presentation/state before each independent comparison.
                layer.PresentForInput(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: "ABCDEFGHIJK")], captureAfter: false, notifyActivation: true);
                Wait(state => state.GetProperty("text").GetString() == "ABCDEFGHIJK" && state.GetProperty("visibility").GetString() == "hidden");
                var before = State(); var clock = Stopwatch.StartNew();
                if (mode.StartsWith("size-pulse")) layer.ResearchActivation(root, "size-pulse");
                if (mode.StartsWith("position-show")) layer.ResearchActivation(root, "position-show-notification");
                if (mode == "show-notification") layer.ResearchActivation(root, "show-notification");
                if (mode.Contains("activation")) layer.ResearchActivation(root, "activate-notification");
                Thread.Sleep(200); var notified = State();
                layer.SendQueueStateChord(root, [0x11, 0x41], mode == "posted-activation");
                bool selected = false; try { Wait(state => state.GetProperty("selectionStart").GetInt32() == 0 && state.GetProperty("selectionEnd").GetInt32() == 11, 1200); selected = true; } catch (TimeoutException) { }
                var selection = State(); layer.SendKeyboardInput(root, "XYZ");
                bool replaced = false; try { Wait(state => state.GetProperty("text").GetString() == "XYZ", 1200); replaced = true; } catch (TimeoutException) { }
                var after = State(); bool sourceHidden = Hidden();
                // Restore framework notifications before resuming the accepted path.
                if (mode.StartsWith("size-pulse")) layer.ResearchActivation(root, "size-minimize");
                if (mode.StartsWith("position-show")) layer.ResearchActivation(root, "position-hide-notification");
                if (mode == "show-notification") layer.ResearchActivation(root, "hide-notification");
                layer.PresentForInput(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: "RECOVERED")], captureAfter: false, notifyActivation: true);
                bool recovered = false; try { Wait(state => state.GetProperty("text").GetString() == "RECOVERED" && state.GetProperty("visibility").GetString() == "hidden"); recovered = true; } catch (TimeoutException) { }
                results.Add(new { mode, selected, replaced, sourceHidden, recovered, ms = clock.Elapsed.TotalMilliseconds, before, notified, selection, after, restored = State() });
                Console.WriteLine(JsonSerializer.Serialize(new { mode, selected, replaced, sourceHidden, recovered }));
                if (!sourceHidden) throw new InvalidOperationException("Comparison changed native source visibility.");
            }
            var image = layer.InspectHiddenWindow(root, true); File.WriteAllBytes(Path.Combine(output, "recovered.png"), image.Png);
        }
        catch (Exception exception) { failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } }
            layer.Dispose(); Thread.Sleep(300);
        }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        bool completed = baseline && failure == null && cleanup && foregroundEquality && pointerEquality && desktopEquality;
        File.WriteAllText(Path.Combine(output, "hidden-shortcuts.json"), JsonSerializer.Serialize(new { completed, failure, browser, baseline, sourcePid = pid, profile, results, graceful, owned, cleanup, foregroundEquality, pointerEquality, desktopEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Research notifications can create framework/native state disagreement; no normal viewer/backend uses them. Each comparison runs in a fresh owned process, is bounded, records native and page visibility, and observes recovery. No source injection, debugging input, physical input or desktop switch. Individual failed mechanisms and recovery attempts are retained outcomes, not harness failures. Reports include waiting deadlines, not just input latency." }, new JsonSerializerOptions { WriteIndented = true }));
        return completed ? 0 : 1;
    }
}

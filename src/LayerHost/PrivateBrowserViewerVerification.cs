using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class PrivateBrowserViewerVerification
{
    internal static int Run(string output, bool activation = false, bool notifications = false, int inspectionSettle = 150)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); var before = Native.GetForegroundWindow();
        using var profileLease = activation ? new ResearchBrowserProfile(project, "viewer") : null;
        using var manager = new LayerManager(); var outer = manager.CreateLayer(); string profile = profileLease?.Path ?? Path.Combine(output, "profile");
        var args = new List<string> { "--demo", "--full-content", "--queue-pointer", "--root-input", "C:/Program Files/Google/Chrome/Application/chrome.exe", "--user-data-dir=" + profile, "--app=" + new Uri(Path.Combine(project, "experiments/gpu-browser.html")).AbsoluteUri, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,600", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--report" };
        if (notifications) args.Insert(1, "--experimental-hidden-input");
        args.InsertRange(1, ["--inspection-settle", inspectionSettle.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        outer.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
        var watch = Stopwatch.StartNew(); WindowInfo[] windows = [];
        while (watch.ElapsedMilliseconds < 15000) { windows = outer.GetWindows(); if (windows.Any(w => w.Text.StartsWith("Automation | PID"))) break; Thread.Sleep(100); }
        var root = windows.First(w => w.Text == "VIAVW — private application layer"); var canvas = windows.First(w => w.Parent == root.Handle && w.Width > 900 && w.Height > 200);
        var lines = File.ReadAllLines(Path.Combine(profile, "DevToolsActivePort")); using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using var list = JsonDocument.Parse(http.GetStringAsync("http://127.0.0.1:" + lines[0] + "/json/list").GetAwaiter().GetResult());
        var page = list.RootElement.EnumerateArray().First(t => t.GetProperty("type").GetString() == "page"); using var inspect = new LocalCdp(page.GetProperty("webSocketDebuggerUrl").GetString()!);
        JsonElement Read(string expression) => inspect.Call("Runtime.evaluate", new { expression, returnByValue = true }).GetProperty("result").GetProperty("value").Clone();
        JsonElement State() => Read("({text:editor.value,selectionStart:editor.selectionStart,selectionEnd:editor.selectionEnd,...observations})");
        JsonElement Wait(Func<JsonElement, bool> condition)
        {
            var clock = Stopwatch.StartNew(); JsonElement state = State();
            while (clock.ElapsedMilliseconds < 6000) { state = State(); if (condition(state)) return state; Thread.Sleep(50); }
            throw new TimeoutException("Native viewer browser state did not satisfy the observation: " + state);
        }
        void Button(string name) { var button = outer.GetWindows().First(w => w.Text == name); outer.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(200); }
        void PictureClick(string name)
        {
            var point = Read($"(()=>{{let r={name}.getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2,height:innerHeight}}}})()");
            using var preview = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); int offset = preview.RootElement.GetProperty("Height").GetInt32() - point.GetProperty("height").GetInt32();
            outer.SendMouseInput(canvas.Handle, (int)point.GetProperty("x").GetDouble(), (int)point.GetProperty("y").GetDouble() + offset); Thread.Sleep(200);
        }
        Button("Human control"); PictureClick("editor"); outer.PostKeyboardInput(canvas.Handle, "HUMAN_NATIVE_GPU"); Thread.Sleep(400); var typed = State();
        PictureClick("toggle"); Thread.Sleep(300); var clicked = State();
        PictureClick("editor"); outer.SendQueueStateChord(canvas.Handle, [0x11, 0x41]); Thread.Sleep(200); outer.PostKeyboardInput(canvas.Handle, "NATIVE_REPLACEMENT"); Thread.Sleep(350); var replaced = State();
        Button("Return to automation"); outer.PostKeyboardInput(canvas.Handle, "REJECT"); Thread.Sleep(250); bool rejected = State().GetProperty("text").GetString() == replaced.GetProperty("text").GetString();
        Button("Automation: send text"); Thread.Sleep(350); var resumed = State(); Button("Refresh");
        var toolbarText = outer.GetWindows().First(w => w.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase) && w.Width == 240);
        outer.SendQueueMouseSequence(toolbarText.Handle, [new(MouseMessage.Move, 20, 10), new(MouseMessage.LeftDown, 20, 10, 1), new(MouseMessage.LeftUp, 20, 10)], true);
        // Configure the wrapper's own native EDIT; its WinForms Ctrl+A preprocessing
        // is not a source-browser compatibility requirement.
        outer.SelectControlText(toolbarText.Handle, 0, -1); outer.SendKeyboardInput(toolbarText.Handle, "PRIVATE_HIDDEN_BATCH");
        bool toolbarInputCorrect = outer.GetWindows().First(window => window.Handle == toolbarText.Handle).Text == "PRIVATE_HIDDEN_BATCH";
        Button("Hide app window"); Button("Click and send privately"); var hiddenBatch = Wait(state => state.GetProperty("text").GetString()!.Contains("PRIVATE_HIDDEN_BATCH"));
        var previewClock = Stopwatch.StartNew(); JsonElement hiddenPreview = default;
        while (previewClock.ElapsedMilliseconds < 6000)
        {
            using var document = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); hiddenPreview = document.RootElement.Clone();
            if (hiddenPreview.GetProperty("Backend").GetString()!.StartsWith("Private presentation / native input") && !hiddenPreview.GetProperty("sourceWindowVisible").GetBoolean()) break;
            Thread.Sleep(50);
        }
        bool hiddenRestored = hiddenPreview.GetProperty("Backend").GetString()!.StartsWith("Private presentation / native input") && !hiddenPreview.GetProperty("sourceWindowVisible").GetBoolean();
        Button("Human control"); Button("Click and send privately"); Thread.Sleep(250); bool hiddenNonOwnerRejected = State().GetProperty("text").GetString() == hiddenBatch.GetProperty("text").GetString();
        Button("Return to automation"); Button("Show app window");
        object? activationEvidence = null; bool activationCorrect = !activation;
        if (activation)
        {
            Button("Human control"); PictureClick("editor"); outer.SendQueueStateChord(canvas.Handle, [0x11, 0x41]); outer.PostKeyboardInput(canvas.Handle, "ABCDEFGHIJK"); Wait(state => state.GetProperty("text").GetString() == "ABCDEFGHIJK");
            outer.SendQueueStateChord(canvas.Handle, [0x24]); for (int i = 0; i < 3; i++) outer.SendQueueStateChord(canvas.Handle, [0x27]); for (int i = 0; i < 3; i++) outer.SendQueueStateChord(canvas.Handle, [0x10, 0x27]);
            var selected = Wait(state => state.GetProperty("selectionStart").GetInt32() == 3 && state.GetProperty("selectionEnd").GetInt32() == 6);
            Button("Return to automation"); Button("Hide app window");
            outer.SelectControlText(toolbarText.Handle, 0, -1); outer.SendKeyboardInput(toolbarText.Handle, "XYZ"); Button("Send without clicking");
            var selectionReplaced = Wait(state => state.GetProperty("text").GetString() == "ABCXYZGHIJK");
            var activationPreviewWatch = Stopwatch.StartNew(); JsonElement activationPreview = default;
            while (activationPreviewWatch.ElapsedMilliseconds < 6000)
            {
                using var document = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); activationPreview = document.RootElement.Clone();
                if (activationPreview.GetProperty("Backend").GetString()!.Contains("activation pulse") && !activationPreview.GetProperty("sourceWindowVisible").GetBoolean()) break;
                Thread.Sleep(50);
            }
            Button("Human control"); Button("Send without clicking"); Thread.Sleep(250); bool activationNonOwnerRejected = State().GetProperty("text").GetString() == "ABCXYZGHIJK";
            Button("Return to automation"); Button("Hide preview"); outer.SelectControlText(toolbarText.Handle, 0, -1); outer.SendKeyboardInput(toolbarText.Handle, "_CAPTURELESS_"); Button("Send without clicking");
            var captureless = Wait(state => state.GetProperty("text").GetString() == "ABCXYZ_CAPTURELESS_GHIJK");
            if (notifications)
            {
                outer.SelectControlText(toolbarText.Handle, 0, -1); outer.SendKeyboardInput(toolbarText.Handle, "HIDDEN_SHORTCUT_REPLACED");
                Button("Human control"); Button("Replace hidden field"); Thread.Sleep(250);
                bool nonOwnerRejected = State().GetProperty("text").GetString() == "ABCXYZ_CAPTURELESS_GHIJK";
                Button("Return to automation"); Button("Replace hidden field");
                var hiddenReplaced = Wait(state => state.GetProperty("text").GetString() == "HIDDEN_SHORTCUT_REPLACED");
                if (!nonOwnerRejected || hiddenReplaced.GetProperty("selectionStart").GetInt32() != "HIDDEN_SHORTCUT_REPLACED".Length) throw new InvalidOperationException("Hidden shortcut viewer transaction failed.");
                File.WriteAllText(Path.Combine(output, "viewer-notification-input.json"), JsonSerializer.Serialize(new { nonOwnerRejected, hiddenReplaced, note = "Preview hidden during input; separate inspection follows. Physical human acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Button("Inspect hidden app");
            activationCorrect = activationNonOwnerRejected && !activationPreview.GetProperty("sourceWindowVisible").GetBoolean() && activationPreview.GetProperty("Backend").GetString()!.Contains("activation pulse") && selectionReplaced.GetProperty("selectionStart").GetInt32() == 6;
            activationEvidence = new { selected, selectionReplaced, activationPreview, activationNonOwnerRejected, captureless };
        }
        var frame = outer.CaptureVisualOutput(root.Handle); File.WriteAllBytes(Path.Combine(output, "private-browser-viewer.png"), frame.Png);
        var pids = outer.GetProcessIds(); outer.Dispose(); Thread.Sleep(300); bool cleanup = pids.All(id => !CompatibilityVerification.Alive(id));
        bool passed = typed.GetProperty("text").GetString() == "HUMAN_NATIVE_GPU" && clicked.GetProperty("changes").GetInt32() == 1 && replaced.GetProperty("text").GetString() == "NATIVE_REPLACEMENT" && rejected && resumed.GetProperty("text").GetString()!.Contains("Hello from virtual input") && resumed.GetProperty("renderer").GetString()!.Contains("NVIDIA") && hiddenRestored && hiddenNonOwnerRejected && toolbarInputCorrect && activationCorrect;
        var report = new { passed, cleanup, typed, clicked, replaced, resumed, rejected, hiddenBatch, hiddenPreview, hiddenRestored, hiddenNonOwnerRejected, toolbarInputCorrect, activation, activationCorrect, activationEvidence, profile, foregroundEquality = Native.GetForegroundWindow() == before, note = "Unmodified Chrome app window launched normally into a private desktop. All source input through native scoped Windows messages/queue state and optional targeted activation notifications; all preview pixels through PrintWindow full-content. CDP used exclusively for read-only verification, never input, navigation, rendering or capture. Wrapper toolbar setup uses its native EDIT selection adapter. Actual controls exercised on a private QA desktop; human hardware acceptance pending. Activation QA reuses a designated disposable profile." };
        File.WriteAllText(Path.Combine(output, "private-browser-viewer.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, passed, cleanup, typed = typed.GetProperty("text").GetString(), replaced = replaced.GetProperty("text").GetString(), rejected, report.foregroundEquality }));
        return passed && cleanup ? 0 : 1;
    }
}

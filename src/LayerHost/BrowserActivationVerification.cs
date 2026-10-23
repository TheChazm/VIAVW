using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class BrowserActivationVerification
{
    internal static string Page(string project)
    {
        string html = File.ReadAllText(Path.Combine(project, "experiments/gpu-visibility.html"));
        return html.Replace("<canvas id=", "<input id=\"otherEditor\" placeholder=\"Second independent field\"><canvas id=")
            .Replace("contextLost=0;", "contextLost=0,mouseDowns=0,clicks=0;")
            .Replace("return {text:editor.value", "return {activeElement:document.activeElement&&document.activeElement.id,otherText:otherEditor.value,mouseDowns,clicks,text:editor.value")
            .Replace("toggle:rect(toggle)", "otherEditor:rect(otherEditor),toggle:rect(toggle)")
            .Replace("if(type!=='pointermove'", "if(type==='pointerdown')mouseDowns++;if(type==='click')clicks++;if(type!=='pointermove'");
    }
    internal static int Run(string output, string browser = "chrome", bool hiddenInput = false, bool notificationInput = false, int notificationSettle = 0)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string executable = browser == "edge" ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : "C:/Program Files/Google/Chrome/Application/chrome.exe";
        using var profile = new ResearchBrowserProfile(project, browser == "edge" ? "edge" : "main"); using var server = new PassivePageServer(Page(project));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputBefore = Native.LastInputTick(); string desktop = Native.InputDesktopName();
        var events = new List<JsonElement>(); var checks = new List<object>(); var burstTimes = new List<double>(); var inputTimes = new List<double>(); var captureTimes = new List<double>();
        var hiddenTimes = new List<double>();
        int incompleteUtf16Observations = 0;
        bool passed = true, graceful = false; int[] owned = []; long root = 0; int height = 0, pid = 0; string? failure = null;
        using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        void Record(string name, object value) { events.Add(JsonSerializer.SerializeToElement(new { name, utc = DateTime.UtcNow, value })); }
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        JsonElement State() => server.Observation ?? throw new InvalidOperationException("No passive browser observation.");
        JsonElement Wait(Func<JsonElement, bool> condition, int deadline = 6000)
        {
            var watch = Stopwatch.StartNew(); JsonElement? state = null;
            while (watch.ElapsedMilliseconds < deadline)
            {
                state = server.Observation;
                try { if (state != null && condition(state.Value)) return state.Value; }
                catch (InvalidOperationException exception) when (exception.Message.Contains("UTF-16", StringComparison.Ordinal))
                {
                    // WM_CHAR delivers a surrogate pair as two code units. A passive
                    // page snapshot can briefly contain only its high surrogate.
                    incompleteUtf16Observations++;
                    File.WriteAllText(Path.Combine(output, "intermediate-utf16-observation.json"), state!.Value.GetRawText());
                }
                Thread.Sleep(25);
            }
            throw new TimeoutException("Activation observation deadline; last: " + state);
        }
        bool Hidden() => !layer.GetWindows().First(window => window.Handle == root).Visible;
        void Click(string target)
        {
            var rectangle = State().GetProperty(target); int x = (int)(rectangle.GetProperty("x").GetDouble() + rectangle.GetProperty("width").GetDouble() / 2), y = (int)(rectangle.GetProperty("y").GetDouble() + rectangle.GetProperty("height").GetDouble() / 2) + height - State().GetProperty("innerHeight").GetInt32();
            layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
        }
        void Hide() { layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden"); }
        PresentedInteraction Burst(string name, PresentedInput[] input, Func<JsonElement, bool> condition, ControlOwner sender = ControlOwner.Automation, bool capture = true, int settle = 150, int expectedMouseDowns = 0)
        {
            var before = State(); int frames = before.GetProperty("frames").GetInt32(), mouse = before.GetProperty("mouseDowns").GetInt32();
            Native.GetCursorPos(out var physicalBefore); var focusBefore = Native.GetForegroundWindow(); uint? tickBefore = Native.LastInputTick();
            bool staysHidden = hiddenInput && input.All(command => command.Kind == PresentedInputKind.Text);
            var watch = Stopwatch.StartNew(); var result = notificationInput ? layer.ResearchHiddenInteraction(root, input, captureAfter: false, settleMilliseconds: notificationSettle, sender: sender) : staysHidden ? layer.SendActivatedInput(root, input, sender) : layer.PresentForInput(root, input, captureAfter: capture, fullContent: true, settleMilliseconds: settle, sender: sender, notifyActivation: true); double inputMs = watch.Elapsed.TotalMilliseconds;
            Native.GetCursorPos(out var physicalAfter); bool inputSamplesPreserved = physicalBefore.X == physicalAfter.X && physicalBefore.Y == physicalAfter.Y && focusBefore == Native.GetForegroundWindow();
            var inputState = Wait(state => condition(state) && state.GetProperty("visibility").GetString() == "hidden");
            if (notificationInput) Check(name + " native hidden transaction", Hidden() && result.HiddenAfter && inputSamplesPreserved);
            // Direct PW3 while natively hidden produced blank pixels in the first
            // notification experiment. Inspect separately through accepted presentation.
            if (notificationInput && capture) result = result with { Frame = layer.InspectHiddenWindow(root, true) };
            if (staysHidden)
            {
                hiddenTimes.Add(inputMs); Check(name + " input stayed hidden", result.Frame == null && Hidden() && result.HiddenAfter && inputSamplesPreserved);
                if (capture) result = result with { Frame = layer.InspectHiddenWindow(root, true) };
            }
            double elapsed = watch.Elapsed.TotalMilliseconds;
            var after = Wait(state => condition(state) && state.GetProperty("visibility").GetString() == "hidden");
            JsonElement? pixels = result.Frame == null ? null : PrivateBrowserVisibilityVerification.Pixels(result.Frame, after, Path.Combine(output, name + ".png"));
            bool correct = result.InitiallyHidden && result.HiddenAfter && result.ActivationNotified && Hidden() && mouse + expectedMouseDowns == after.GetProperty("mouseDowns").GetInt32() && layer.ApplicationProcessId == pid && (pixels == null || pixels.Value.GetProperty("colorCorrect").GetBoolean() && pixels.Value.GetProperty("encodedFrame").GetInt32() > frames);
            Check(name, correct); Record(name, new { elapsedMs = elapsed, inputMs, inputSamplesPreserved, tickBefore, tickAfter = Native.LastInputTick(), hiddenInput, inputStayedHidden = staysHidden, settle, result.Milliseconds, result.ActivationNotified, result.InitiallyHidden, result.HiddenAfter, before, inputState, after, pixels });
            (capture ? burstTimes : inputTimes).Add(elapsed); if (result.Frame != null) captureTimes.Add(result.Frame.NativeRenderingMilliseconds ?? 0);
            return result;
        }
        try
        {
            pid = layer.LaunchApplication(executable, ["--user-data-dir=" + profile.Path, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,700"], output);
            owned = layer.GetProcessIds(); Wait(state => state.GetProperty("webgl").GetBoolean(), 20000);
            root = layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
            height = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent).Height;
            Check("hardware WebGL", State().GetProperty("renderer").GetString()!.Contains("NVIDIA"));
            Click("editor"); layer.SendKeyboardInput(root, "ABCDEFGHIJK"); Wait(state => state.GetProperty("text").GetString() == "ABCDEFGHIJK");
            layer.SendKey(root, 0x24); for (int i = 0; i < 3; i++) layer.SendKey(root, 0x27); for (int i = 0; i < 3; i++) layer.SendQueueStateChord(root, [0x10, 0x27]);
            var selected = Wait(state => state.GetProperty("selectionStart").GetInt32() == 3 && state.GetProperty("selectionEnd").GetInt32() == 6); Record("selected before hide", selected);
            File.WriteAllBytes(Path.Combine(output, "selection-before-hide.png"), layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent).Png);
            Hide(); Burst("selection-replaced-without-click", [new(PresentedInputKind.Text, Text: "XYZ")], state => state.GetProperty("text").GetString() == "ABCXYZGHIJK" && state.GetProperty("selectionStart").GetInt32() == 6 && state.GetProperty("selectionEnd").GetInt32() == 6);
            Burst("caret-insertion-without-click", [new(PresentedInputKind.Text, Text: "_HERE_")], state => state.GetProperty("text").GetString() == "ABCXYZ_HERE_GHIJK");
            if (hiddenInput)
            {
                bool keysRejected = false; try { layer.SendActivatedInput(root, [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41])]); } catch (ArgumentException) { keysRejected = true; }
                Check("hidden modifier command rejected before input", keysRejected && Hidden() && State().GetProperty("text").GetString() == "ABCXYZ_HERE_GHIJK");
            }
            Burst("modifier-replacement-without-click", [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: "MODIFIER_REPLACED")], state => state.GetProperty("text").GetString() == "MODIFIER_REPLACED");
            layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible"); Click("otherEditor"); layer.SendKeyboardInput(root, "SECOND_FIELD"); Wait(state => state.GetProperty("otherText").GetString() == "SECOND_FIELD"); Hide();
            Burst("second-field-preserved", [new(PresentedInputKind.Text, Text: "_RESUMED")], state => state.GetProperty("otherText").GetString() == "SECOND_FIELD_RESUMED" && state.GetProperty("text").GetString() == "MODIFIER_REPLACED" && state.GetProperty("activeElement").GetString() == "otherEditor");
            Burst("no-capture-input", [new(PresentedInputKind.Text, Text: "_NO_CAPTURE")], state => state.GetProperty("otherText").GetString() == "SECOND_FIELD_RESUMED_NO_CAPTURE", capture: false);
            string expectedOther = "SECOND_FIELD_RESUMED_NO_CAPTURE";
            if (hiddenInput || notificationInput)
            {
                expectedOther += "_Ω_漢字_🙂"; string expectedUnicode = expectedOther;
                Burst("hidden-unicode-text", [new(PresentedInputKind.Text, Text: "_Ω_漢字_🙂")], state => state.GetProperty("otherText").GetString() == expectedUnicode);
            }
            for (int cycle = 0; cycle < 12; cycle++)
            {
                string suffix = "_C" + cycle; expectedOther = hiddenInput ? expectedOther + suffix : "CYCLE_" + cycle; string expectedCycle = expectedOther;
                PresentedInput[] commands = hiddenInput ? [new(PresentedInputKind.Text, Text: suffix)] : [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: expectedCycle)];
                Burst("keyboard-cycle-" + cycle, commands, state => state.GetProperty("otherText").GetString() == expectedCycle && state.GetProperty("text").GetString() == "MODIFIER_REPLACED");
            }
            layer.RequestAttention("Inspect a hidden keyboard session."); layer.TakeHumanControl(); bool rejected = false;
            try { if (notificationInput) layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.Text, Text: "REJECT")]); else layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "REJECT")], notifyActivation: true); } catch (InvalidOperationException) { rejected = true; }
            Check("activation rejected for non-owner", rejected && Hidden());
            string humanText = expectedOther + "_HUMAN"; Burst("human-owned-keyboard", [new(PresentedInputKind.Text, Text: "_HUMAN")], state => state.GetProperty("otherText").GetString() == humanText, ControlOwner.Human);
            var recovery = Stopwatch.StartNew(); layer.RestartWorker(); double recoveryMs = recovery.Elapsed.TotalMilliseconds;
            Check("hidden recovery keeps source and owner", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human && Hidden(), new { recoveryMs });
            string recoveredText = humanText + "_RECOVERED"; Burst("recovered-human-keyboard", [new(PresentedInputKind.Text, Text: "_RECOVERED")], state => state.GetProperty("otherText").GetString() == recoveredText, ControlOwner.Human);
            layer.ReturnToAutomation(); bool humanRejected = false; try { if (notificationInput) layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.Text, Text: "REJECT")], sender: ControlOwner.Human); else layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "REJECT")], sender: ControlOwner.Human, notifyActivation: true); } catch (InvalidOperationException) { humanRejected = true; }
            Check("returned ownership rejects previous sender", humanRejected && layer.AttentionReason == null && Hidden());
            string automationText = recoveredText + "_AUTO"; Burst("resumed-automation-keyboard", [new(PresentedInputKind.Text, Text: "_AUTO")], state => state.GetProperty("otherText").GetString() == automationText);
            // The button gets logical focus by keyboard; no additional picture click.
            Burst("keyboard-focus-next-field", [new(PresentedInputKind.KeyChord, Keys: [0x10, 0x9])], state => state.GetProperty("activeElement").GetString() == "toggle");
            Burst("keyboard-gpu-button", [new(PresentedInputKind.KeyChord, Keys: [0x20])], state => state.GetProperty("changes").GetInt32() == 1);
            Check("no GPU context loss", State().GetProperty("contextLost").GetInt32() == 0);
            if (notificationInput)
            {
                PresentedInput Pointer(string target, double from = .5, double? to = null)
                {
                    var rect = State().GetProperty(target); int x = (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() * from), y = (int)(rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2) + height - State().GetProperty("innerHeight").GetInt32();
                    int end = to is null ? x : (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() * to.Value);
                    return new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.Move, end, y, 1), new(MouseMessage.LeftUp, end, y)]);
                }
                Burst("hidden-native-click-and-text", [Pointer("editor"), new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: "NOTIFICATION_MOUSE")], state => state.GetProperty("text").GetString() == "NOTIFICATION_MOUSE", expectedMouseDowns: 1);
                Burst("hidden-native-button-click", [Pointer("toggle")], state => state.GetProperty("changes").GetInt32() == 2, expectedMouseDowns: 1);
                Burst("hidden-native-slider-drag", [Pointer("sliderRect", .2, .8)], state => state.GetProperty("slider").GetInt32() >= 70, expectedMouseDowns: 1);
                bool invalidRejected = false, unbalancedRejected = false;
                try { layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.Text, Text: new string('x', 4097))]); } catch (ArgumentException) { invalidRejected = true; }
                try { layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.LeftDown, 10, 10, 1)])]); } catch (ArgumentException) { unbalancedRejected = true; }
                Check("notification bounds rejected before source activity", invalidRejected && unbalancedRejected && Hidden());
                layer.ResearchHiddenInteraction(root, [Pointer("otherEditor")], settleMilliseconds: notificationSettle); Wait(state => state.GetProperty("activeElement").GetString() == "otherEditor");
                var activity = NotificationActivityResearch.Run(layer, root, State, condition => Wait(condition), notificationSettle);
                foreach (var measurement in activity.Measurements) Record(measurement.Name, measurement.Value);
                hiddenTimes.AddRange(activity.InputMilliseconds); Check("notification visibility restored and idle animation paused", activity.IdleAnimationPaused && Hidden());
            }
            if (hiddenInput)
            {
                // Input-only activity with no inspection in the interval: observe rendering
                // and CPU/GPU costs without a continuous capture or presentation loop.
                layer.PresentForInput(root, [new(PresentedInputKind.KeyChord, Keys: [0x9])], captureAfter: false, notifyActivation: true); Wait(state => state.GetProperty("activeElement").GetString() == "otherEditor"); Thread.Sleep(1500);
                var initial = State(); int[] ids = layer.GetProcessIds();
                Dictionary<int, double> Cpu() => ids.ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
                var beforeCpu = Cpu(); var gpu = Task.Run(() => GpuCounters.Sample(ids)); var interval = Stopwatch.StartNew(); int commands = 0;
                while (interval.ElapsedMilliseconds < 4000)
                {
                    string value = "_A" + commands++; var packet = Stopwatch.StartNew();
                    var result = layer.SendActivatedInput(root, [new(PresentedInputKind.Text, Text: value)]); hiddenTimes.Add(packet.Elapsed.TotalMilliseconds);
                    Wait(state => state.GetProperty("otherText").GetString()!.EndsWith(value) && state.GetProperty("visibility").GetString() == "hidden");
                    if (!result.HiddenAfter || !Hidden()) throw new InvalidOperationException("Input-only source became visible."); Thread.Sleep(100);
                }
                double elapsed = interval.Elapsed.TotalMilliseconds; var afterCpu = Cpu(); var last = State(); double cpu = beforeCpu.Sum(pair => afterCpu[pair.Key] - pair.Value);
                bool stopped = last.GetProperty("frames").GetInt32() == initial.GetProperty("frames").GetInt32(); Check("input-only animation stays paused", stopped && Hidden());
                Record("hidden-input-activity", new { commands, elapsedMs = elapsed, cpuMs = cpu, wholeMachinePercent = cpu / elapsed / Environment.ProcessorCount * 100, gpu = gpu.GetAwaiter().GetResult(), framesBefore = initial.GetProperty("frames"), framesAfter = last.GetProperty("frames"), initial, last });
                // Test whether already established application focus can be retained
                // between ordinary text commands. No automatic readiness cache is added.
                initial = State(); beforeCpu = Cpu(); gpu = Task.Run(() => GpuCounters.Sample(ids)); interval.Restart(); commands = 0; var retainedTimes = new List<double>();
                while (interval.ElapsedMilliseconds < 4000)
                {
                    string value = "_R" + commands++; var packet = Stopwatch.StartNew(); layer.SendKeyboardInput(root, value); retainedTimes.Add(packet.Elapsed.TotalMilliseconds);
                    Wait(state => state.GetProperty("otherText").GetString()!.EndsWith(value) && state.GetProperty("visibility").GetString() == "hidden");
                    if (!Hidden()) throw new InvalidOperationException("Retained-focus text made the source visible."); Thread.Sleep(100);
                }
                elapsed = interval.Elapsed.TotalMilliseconds; afterCpu = Cpu(); last = State(); cpu = beforeCpu.Sum(pair => afterCpu[pair.Key] - pair.Value);
                Check("established hidden text focus can be reused", last.GetProperty("frames").GetInt32() == initial.GetProperty("frames").GetInt32() && Hidden());
                var ordered = retainedTimes.Order().ToArray();
                Record("retained-focus-activity", new { commands, elapsedMs = elapsed, cpuMs = cpu, wholeMachinePercent = cpu / elapsed / Environment.ProcessorCount * 100, gpu = gpu.GetAwaiter().GetResult(), medianInputMs = ordered[ordered.Length / 2], framesBefore = initial.GetProperty("frames"), framesAfter = last.GetProperty("frames"), initial, last, note = "Focus established by the prior explicit activation call. No presentation/inspection occurs between these commands. Sequential order and warmup confound resource comparisons; no automatic cached-readiness policy is implemented." });
                File.WriteAllBytes(Path.Combine(output, "hidden-input-activity-inspected.png"), layer.InspectHiddenWindow(root, true).Png);
            }
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            if (!layer.Closed) { owned = owned.Concat(layer.GetProcessIds()).Distinct().ToArray(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch (Exception exception) { Record("close-failure", exception.ToString()); } }
            layer.Dispose(); Thread.Sleep(300);
        }
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var afterPointer);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, inputDesktopEquality = desktop == Native.InputDesktopName();
        Check("cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && inputDesktopEquality, new { cleanup, foregroundEquality, pointerEquality, inputDesktopEquality, inputBefore, inputAfter = Native.LastInputTick() });
        object Timing(List<double> samples) { var values = samples.Order().ToArray(); return new { count = values.Length, medianMs = values.Length == 0 ? (double?)null : values[values.Length / 2], maxMs = values.Length == 0 ? (double?)null : values[^1] }; }
        File.WriteAllText(Path.Combine(output, "browser-activation.json"), JsonSerializer.Serialize(new { passed, browser, hiddenInput, notificationInput, notificationSettle, incompleteUtf16Observations, failure, checks, events, profile = profile.Path, sourcePid = pid, owned, graceful, cleanup, burstTiming = Timing(burstTimes), capturelessTiming = Timing(inputTimes), hiddenInputTiming = Timing(hiddenTimes), nativeCaptureTiming = Timing(captureTimes), executableVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion, note = "Unchanged browser executable on private desktop, reusable disposable profile, native targeted activation notifications and keyboard messages only after initial field selection. In hidden-input mode text stays hidden; chords deliberately use private presentation, and captures are separate inspections. In notification mode native visibility stays hidden while a bounded framework visibility notification permits input and direct PW capture; hidden notification is restored in finally. No click replay, debug control protocol, page model writes, source hook, global input or physical activation API. Page supplies passive observations and native GPU counter pixels. Human ownership is simulated; physical acceptance pending. CPU includes passive test content and excludes the supervising harness; GPU counters are individual engine samples." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, browser, passed, failure, graceful, cleanup })); return passed ? 0 : 1;
    }
}

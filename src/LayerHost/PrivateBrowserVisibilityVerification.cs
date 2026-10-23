using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Native browser input/capture only. The disposable page supplies passive observations.
internal static class PrivateBrowserVisibilityVerification
{
    static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    static Dictionary<int, double> Cpu(int[] ids)
    {
        var values = new Dictionary<int, double>();
        foreach (int id in ids) try { using var process = Process.GetProcessById(id); values[id] = process.TotalProcessorTime.TotalMilliseconds; } catch (ArgumentException) { }
        return values;
    }
    internal static int Run(string output, bool nativeOcclusion = false, bool interactionOnly = false)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string profile = Path.Combine(output, "profile"), chrome = "C:/Program Files/Google/Chrome/Application/chrome.exe";
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputTick = Native.LastInputTick(); string inputDesktop = Native.InputDesktopName();
        var evidence = new List<JsonElement>(); var checks = new List<object>(); var inspectionTimes = new List<double>();
        bool passed = true, graceful = false, cleanup = false; string? failure = null; int[] owned = []; long root = 0; int pid = 0;
        using var server = new PassivePageServer(File.ReadAllText(Path.Combine(project, "experiments/gpu-visibility.html")));
        using var power = new DisplayPowerObserver(); using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        void Record(string name, object value) { evidence.Add(JsonSerializer.SerializeToElement(new { name, utc = DateTime.UtcNow, value })); }
        void Check(string name, bool result, object? value = null) { passed &= result; checks.Add(new { name, passed = result, value }); Console.WriteLine((result ? "PASS " : "FAIL ") + name); }
        JsonElement Wait(Func<JsonElement, bool> condition, int deadline = 6000)
        {
            var watch = Stopwatch.StartNew(); JsonElement? value = null;
            while (watch.ElapsedMilliseconds < deadline) { value = server.Observation; if (value != null && condition(value.Value)) return value.Value; Thread.Sleep(50); }
            throw new TimeoutException("Passive page observation deadline; last observation: " + value);
        }
        JsonElement Latest() => server.Observation ?? throw new InvalidOperationException("No page observation.");
        bool Visible() => layer.GetWindows().First(window => window.Handle == root).Visible;
        CaptureFrame Capture(string name, bool inspect = false)
        {
            var watch = Stopwatch.StartNew(); var frame = inspect ? layer.InspectHiddenWindow(root, true) : layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent);
            double rpc = watch.Elapsed.TotalMilliseconds; if (inspect) inspectionTimes.Add(rpc);
            Record(name, new { rpcMs = rpc, frame.CaptureMilliseconds, frame.NativeRenderingMilliseconds, frame.EncodingMilliseconds, frame.Backend, pixels = Pixels(frame, Latest(), Path.Combine(output, name + ".png")), sourceVisible = Visible(), observation = Latest(), power = power.Snapshot() });
            return frame;
        }
        int offset = 0;
        (int X, int Y) Point(string target, double fraction = .5)
        {
            var rectangle = Latest().GetProperty(target);
            return ((int)(rectangle.GetProperty("x").GetDouble() + rectangle.GetProperty("width").GetDouble() * fraction), (int)(rectangle.GetProperty("y").GetDouble() + rectangle.GetProperty("height").GetDouble() / 2) + offset);
        }
        void Click(string target, ControlOwner sender = ControlOwner.Automation)
        {
            var point = Point(target); layer.SendQueueMouseSequence(root, [new(MouseMessage.Move, point.X, point.Y), new(MouseMessage.LeftDown, point.X, point.Y, 1), new(MouseMessage.LeftUp, point.X, point.Y)], true, sender);
        }
        void Replace(string text, ControlOwner sender = ControlOwner.Automation)
        { Click("editor", sender); layer.SendQueueStateChord(root, [0x11, 0x41], sender: sender); layer.SendKeyboardInput(root, text, sender); Wait(state => state.GetProperty("text").GetString() == text); }
        void Phase(string name, string visibility)
        {
            Wait(state => state.GetProperty("visibility").GetString() == visibility); Thread.Sleep(1500);
            var before = Latest(); int[] ids = layer.GetProcessIds(); var cpuBefore = Cpu(ids); var interval = Stopwatch.StartNew(); var gpu = Task.Run(() => GpuCounters.Sample(ids));
            Thread.Sleep(6000); double elapsed = interval.Elapsed.TotalMilliseconds; var cpuAfter = Cpu(ids); var after = Latest(); var cpu = cpuBefore.Where(pair => cpuAfter.ContainsKey(pair.Key)).Select(pair => new { pid = pair.Key, milliseconds = cpuAfter[pair.Key] - pair.Value }).ToArray();
            Record(name, new { elapsedMs = elapsed, cpuMs = cpu.Sum(item => item.milliseconds), machinePercent = cpu.Sum(item => item.milliseconds) / elapsed / Environment.ProcessorCount * 100, cpu, gpu = gpu.GetAwaiter().GetResult(), framesDelta = after.GetProperty("frames").GetInt32() - before.GetProperty("frames").GetInt32(), observationsDelta = after.GetProperty("observations").GetInt32() - before.GetProperty("observations").GetInt32(), before, after, sourceVisible = Visible(), power = power.Snapshot() });
        }
        try
        {
            var args = new List<string> { "--user-data-dir=" + profile, "--app=" + server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--window-size=800,660" };
            if (!nativeOcclusion) args.Add("--disable-backgrounding-occluded-windows");
            pid = layer.LaunchApplication(chrome, args.ToArray(), output);
            owned = layer.GetProcessIds();
            var initial = Wait(state => state.GetProperty("webgl").GetBoolean(), 20000);
            var source = layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First(); root = source.Handle;
            var frame = Capture("initial"); offset = frame.Height - initial.GetProperty("innerHeight").GetInt32();
            Check("hardware WebGL renderer", initial.GetProperty("renderer").GetString()?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true, initial.GetProperty("renderer"));
            Replace("VISIBLE_BASELINE"); Click("toggle"); Wait(state => state.GetProperty("changes").GetInt32() == 1);
            var start = Point("sliderRect", .2); var end = Point("sliderRect", .85);
            var steps = new List<MouseStep> { new(MouseMessage.Move, start.X, start.Y), new(MouseMessage.LeftDown, start.X, start.Y, 1) };
            for (int i = 1; i <= 10; i++) steps.Add(new(MouseMessage.Move, start.X + (end.X - start.X) * i / 10, start.Y, 1));
            steps.Add(new(MouseMessage.LeftUp, end.X, end.Y)); layer.SendQueueMouseSequence(root, steps.ToArray(), true);
            var drag = Wait(state => state.GetProperty("slider").GetInt32() >= 80); Check("native ordinary range drag", true, drag);
            Check("visible native GPU pixels", Pixels(Capture("visible-input"), Latest()).GetProperty("colorCorrect").GetBoolean());
            if (!interactionOnly) Phase("visible-animation", "visible"); layer.HideLayer(root); if (!interactionOnly) Phase("hidden-animation", "hidden");
            int changes = Latest().GetProperty("changes").GetInt32(); Click("toggle");
            bool hiddenClick = false; try { Wait(state => state.GetProperty("changes").GetInt32() == changes + 1, 3000); hiddenClick = true; } catch (TimeoutException) { }
            layer.SendKeyboardInput(root, "_HIDDEN"); Thread.Sleep(1200);
            Record("hidden-input-research", new { hiddenClick, state = Latest(), sourceVisible = Visible() });
            Capture("direct-hidden"); var inspected = Capture("inspect-hidden", true);
            Check("hidden inspection native GPU pixels", Pixels(inspected, Latest()).GetProperty("colorCorrect").GetBoolean()); Check("inspection restores hidden source", !Visible());
            layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible"); Replace("RESUMED_NATIVE"); Click("toggle"); Wait(state => state.GetProperty("changes").GetInt32() == changes + (hiddenClick ? 2 : 1));
            Check("same source resumes input", layer.ApplicationProcessId == pid && Latest().GetProperty("text").GetString() == "RESUMED_NATIVE");
            if (!interactionOnly) Phase("reshown-animation", "visible"); layer.HideLayer(root); if (!interactionOnly) Phase("hidden-again-animation", "hidden");
            for (int cycle = 0; cycle < (interactionOnly ? 1 : 8); cycle++)
            {
                layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible");
                Replace("CYCLE_" + cycle); int prior = Latest().GetProperty("changes").GetInt32(); Click("toggle"); Wait(state => state.GetProperty("changes").GetInt32() == prior + 1);
                layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden");
                int beforeFrame = Latest().GetProperty("frames").GetInt32(); var capture = Capture("cycle-" + cycle, true); var pixels = Pixels(capture, Latest());
                int encoded = pixels.GetProperty("encodedFrame").GetInt32(); Wait(state => state.GetProperty("visibility").GetString() == "hidden" && state.GetProperty("frames").GetInt32() >= encoded);
                Check("checked fresh inspection cycle " + cycle, !Visible() && pixels.GetProperty("colorCorrect").GetBoolean() && encoded > beforeFrame && Latest().GetProperty("text").GetString() == "CYCLE_" + cycle, new { beforeFrame, encoded });
            }
            foreach (int settle in interactionOnly ? new[] { 150 } : new[] { 150, 50, 25, 0, 150 })
            {
                Wait(state => state.GetProperty("visibility").GetString() == "hidden"); int previousChanges = Latest().GetProperty("changes").GetInt32(), previousFrame = Latest().GetProperty("frames").GetInt32();
                var editor = Point("editor"); var toggle = Point("toggle"); string text = "BURST_" + settle + "_" + previousChanges;
                PresentedInput Mouse((int X, int Y) point) => new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.Move, point.X, point.Y), new(MouseMessage.LeftDown, point.X, point.Y, 1), new(MouseMessage.LeftUp, point.X, point.Y)]);
                var watch = Stopwatch.StartNew(); var interaction = layer.PresentForInput(root, [Mouse(editor), new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: text), Mouse(toggle)], fullContent: true, settleMilliseconds: settle); double rpc = watch.Elapsed.TotalMilliseconds;
                bool observed = false; try { Wait(state => state.GetProperty("text").GetString() == text && state.GetProperty("changes").GetInt32() == previousChanges + 1, 2500); observed = true; } catch (TimeoutException) { }
                var expected = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["surface"] = Latest().GetProperty("surface"), ["innerHeight"] = Latest().GetProperty("innerHeight"), ["changes"] = previousChanges + 1 });
                var pixels = Pixels(interaction.Frame!, expected, Path.Combine(output, text + ".png")); bool correct = observed && pixels.GetProperty("colorCorrect").GetBoolean() && pixels.GetProperty("encodedFrame").GetInt32() > previousFrame && interaction.InitiallyHidden && interaction.HiddenAfter && !Visible();
                Record("presented-input-burst", new { settle, rpcMs = rpc, interaction.Milliseconds, interaction.InitiallyHidden, interaction.HiddenAfter, previousFrame, observed, correct, pixels, state = Latest() });
                if (settle == 150) Check("default presented input is fresh and restored", correct);
            }
            layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible"); Click("editor");
            var visibleText = layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "_VISIBLE")], fullContent: true);
            Wait(state => state.GetProperty("text").GetString()!.EndsWith("_VISIBLE")); Check("already visible input remains visible", !visibleText.InitiallyHidden && !visibleText.HiddenAfter && Visible());
            long retainedFocus = layer.GetFocusedWindow(root); Record("focus-before-hide", new { retainedFocus, windows = layer.GetWindows() });
            layer.HideLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "hidden"); string priorText = Latest().GetProperty("text").GetString()!;
            var textOnly = layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "_TEXT_ONLY")], fullContent: true); bool textOnlyObserved = false;
            try { Wait(state => state.GetProperty("text").GetString() == priorText + "_TEXT_ONLY", 3000); textOnlyObserved = true; } catch (TimeoutException) { }
            Record("text-only-presentation", new { textOnlyObserved, textOnly.InitiallyHidden, textOnly.HiddenAfter, textOnly.Milliseconds, state = Latest() });
            if (textOnly.Frame != null) File.WriteAllBytes(Path.Combine(output, "text-only-presentation.png"), textOnly.Frame.Png);
            var noCapture = layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "_NO_CAPTURE")], captureAfter: false); bool capturelessObserved = false;
            try { Wait(state => state.GetProperty("text").GetString()!.EndsWith("_NO_CAPTURE"), 3000); capturelessObserved = true; } catch (TimeoutException) { }
            Record("captureless-presentation", new { capturelessObserved, noCapture.InitiallyHidden, noCapture.HiddenAfter, noCapture.Milliseconds, frameNull = noCapture.Frame == null, state = Latest() });
            var clickPoint = Point("editor");
            var capturelessClick = layer.PresentForInput(root, [new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.Move, clickPoint.X, clickPoint.Y), new(MouseMessage.LeftDown, clickPoint.X, clickPoint.Y, 1), new(MouseMessage.LeftUp, clickPoint.X, clickPoint.Y)]), new(PresentedInputKind.Text, Text: "_CLICK_NO_CAPTURE")], captureAfter: false);
            Wait(state => state.GetProperty("text").GetString()!.Contains("_CLICK_NO_CAPTURE"));
            Check("captureless native click and text", capturelessClick.Frame == null && capturelessClick.HiddenAfter && !Visible());
            var hiddenBeforeRejected = Latest();
            bool invalidRejected = false; try { layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: new string('x', 4097))]); } catch (ArgumentException) { invalidRejected = true; }
            bool unbalancedRejected = false; try { layer.PresentForInput(root, [new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.LeftDown, 10, 10)])]); } catch (ArgumentException) { unbalancedRejected = true; }
            bool foreignRejected = false; using (var peer = manager.CreateLayer())
            {
                peer.LaunchApplication(Program.FindProbe(), [], output); var peerRoot = CompatibilityVerification.WaitRoot(peer, "Disposable application"); owned = owned.Concat(peer.GetProcessIds()).Distinct().ToArray();
                try { layer.PresentForInput(root, [new(PresentedInputKind.Text, Window: peerRoot.Handle, Text: "FOREIGN")]); } catch (LayerOperationException) { foreignRejected = true; }
            }
            Check("invalid, unbalanced and foreign input rejected before presentation", invalidRejected && unbalancedRejected && foreignRejected && !Visible() && Latest().GetProperty("text").GetString() == hiddenBeforeRejected.GetProperty("text").GetString());
            layer.TakeHumanControl(); bool rejected = false; try { Click("toggle"); } catch (InvalidOperationException) { rejected = true; }
            bool burstRejected = false; try { layer.PresentForInput(root, [new(PresentedInputKind.Text, Text: "REJECT")]); } catch (InvalidOperationException) { burstRejected = true; }
            Check("non-owner cannot present input", burstRejected && !Visible());
            var beforeRecovery = Latest(); var recovery = Stopwatch.StartNew(); layer.RestartWorker(); double recoveryMs = recovery.Elapsed.TotalMilliseconds;
            Check("hidden source and owner survive worker recovery", !Visible() && layer.Owner == ControlOwner.Human && layer.ApplicationProcessId == pid && Latest().GetProperty("text").GetString() == beforeRecovery.GetProperty("text").GetString(), new { recoveryMs });
            Check("human-owned hidden inspection", Pixels(Capture("human-owned-inspection", true), Latest()).GetProperty("colorCorrect").GetBoolean() && !Visible());
            layer.ShowLayer(root); Wait(state => state.GetProperty("visibility").GetString() == "visible"); Replace("HUMAN_HANDOFF", ControlOwner.Human); layer.ReturnToAutomation();
            bool humanRejected = false; try { layer.SendKeyboardInput(root, "REJECT", ControlOwner.Human); } catch (InvalidOperationException) { humanRejected = true; }
            layer.SendKeyboardInput(root, "_AUTO"); Wait(state => state.GetProperty("text").GetString() == "HUMAN_HANDOFF_AUTO");
            Check("ownership handoff and resumed automation", rejected && humanRejected); Capture("final-resumed");
            Record("memory", layer.GetProcessIds().Select(id =>
            {
                try { using var process = Process.GetProcessById(id); return (object)new { pid = id, available = true, name = process.ProcessName, workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                { return new { pid = id, available = false, reason = "Process exited during the read-only memory sample.", error = exception.Message }; }
            }).ToArray());
            Check("no WebGL context loss", Latest().GetProperty("contextLost").GetInt32() == 0);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            if (!layer.Closed) { owned = owned.Concat(layer.GetProcessIds()).Distinct().ToArray(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch (Exception exception) { Record("close-failure", exception.ToString()); } }
            layer.Dispose(); Thread.Sleep(300); cleanup = owned.All(id => !CompatibilityVerification.Alive(id));
        }
        Native.GetCursorPos(out var after); bool pointerEquality = pointer.X == after.X && pointer.Y == after.Y, foregroundEquality = foreground == Native.GetForegroundWindow(), inputDesktopEquality = inputDesktop == Native.InputDesktopName();
        power.Dispose(); bool powerObserverStopped = power.ThreadStopped;
        var report = new { passed, nativeOcclusion, interactionOnly, failure, checks, evidence, sourcePid = pid, owned, graceful, cleanup, foregroundEquality, pointerEquality, inputDesktopEquality, inputBefore = inputTick, inputAfter = Native.LastInputTick(), powerEvents = power.Events(), powerObserverStopped, logicalProcessors = Environment.ProcessorCount, inspectionRpcMedianMs = inspectionTimes.Count == 0 ? (double?)null : inspectionTimes.Order().ElementAt(inspectionTimes.Count / 2), chromeVersion = FileVersionInfo.GetVersionInfo(chrome).FileVersion, note = "Unmodified installed Chrome, unique disposable profile on a non-input private desktop. Native scoped messages/queue state and PrintWindow full-content only; no browser debug protocol, extension, pixel export, source hook or software renderer override. Local test content reports passive observations. CPU includes that content and its observation traffic, not an ordinary application's idle benchmark. Short GPU engine samples are separate values, not aggregate GPU percentage. Human ownership is simulated; physical acceptance pending." };
        File.WriteAllText(Path.Combine(output, "private-browser-visibility.json"), JsonSerializer.Serialize(report, Format)); Console.WriteLine(JsonSerializer.Serialize(new { output, passed, failure, graceful, cleanup, foregroundEquality, pointerEquality }));
        return passed && cleanup && foregroundEquality && pointerEquality && inputDesktopEquality && powerObserverStopped ? 0 : 1;
    }
    internal static JsonElement Pixels(CaptureFrame frame, JsonElement state, string? path = null)
    {
        if (path != null) File.WriteAllBytes(path, frame.Png); using var stream = new MemoryStream(frame.Png); using var bitmap = new Bitmap(stream);
        var rect = state.GetProperty("surface"); int x = (int)rect.GetProperty("x").GetDouble(), y = (int)rect.GetProperty("y").GetDouble() + frame.Height - state.GetProperty("innerHeight").GetInt32();
        bool odd = state.GetProperty("changes").GetInt32() % 2 != 0; int correct = 0, samples = 0;
        for (int row = 35; row < 170; row += 15) for (int column = 10; column < 530; column += 15)
        { var pixel = bitmap.GetPixel(x + column, y + row); samples++; if (odd ? pixel.R > 200 && pixel.G is > 100 and < 180 && pixel.B < 100 : pixel.R < 110 && pixel.G > 150 && pixel.B < 140) correct++; }
        int encodedFrame = 0;
        for (int bit = 0; bit < 16; bit++) { var pixel = bitmap.GetPixel(x + bit * 20 + 10, y + 10); if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) encodedFrame |= 1 << bit; }
        var center = bitmap.GetPixel(x + 270, y + 90);
        return JsonSerializer.SerializeToElement(new { colorCorrect = correct == samples, correct, samples, encodedFrame, centerRgb = new { center.R, center.G, center.B }, expectedChanges = state.GetProperty("changes").GetInt32(), frame.Width, frame.Height });
    }
}

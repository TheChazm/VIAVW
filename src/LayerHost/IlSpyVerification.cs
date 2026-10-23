using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class IlSpyVerification
{
    internal static string Project => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal static string Executable => Path.Combine(Project, ".deps/ILSpy-10.1.0.8386-x64/app/ILSpy.exe");
    internal static string[] Prepare(string directory)
    {
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "ILSpy.xml");
        if (File.Exists(config)) throw new InvalidOperationException("Use a fresh disposable ILSpy configuration directory.");
        File.WriteAllText(config, "<ILSpy><UpdateSettings><AutomaticUpdateCheckEnabled>false</AutomaticUpdateCheckEnabled></UpdateSettings><MiscSettings LoadPreviousAssemblies=\"false\" AllowMultipleInstances=\"true\"/><SessionSettings><WindowBounds>10,10,880,440</WindowBounds><Theme>Light</Theme><CurrentCulture>en-US</CurrentCulture></SessionSettings></ILSpy>");
        return ["--newinstance", "--config", config, "--navigateto", "T:WpfProbe.Program", Path.Combine(Project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.dll")];
    }
    internal static WindowInfo WaitRoot(ApplicationLayer layer)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 15000)
        {
            var found = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text.Contains("ILSpy") && w.Width > 300);
            if (found != null) return found;
            Thread.Sleep(100);
        }
        throw new TimeoutException("No owned ILSpy window within launch deadline.");
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var beforeHashes = Directory.GetFiles(Path.GetDirectoryName(Executable)!).ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); uint? inputBefore = Native.LastInputTick(); var results = new List<object>();
        bool passed = true; using var manager = new LayerManager();
        foreach (string mode in new[] { "default", "software-hook" })
        {
            string folder = Path.Combine(output, mode); using var layer = manager.CreateLayer(); var args = Prepare(folder);
            layer.LaunchApplication(Executable, args, folder, mode == "software-hook" ? WpfHookEnvironment.Create(Executable, folder) : null);
            if (mode == "software-hook") WpfHookEnvironment.VerifyAsync(folder, layer.ApplicationProcessId).GetAwaiter().GetResult();
            var root = WaitRoot(layer); Thread.Sleep(2200);
            var captures = new List<object>(); bool visualVerified = true;
            foreach (string captureMode in new[] { "pw1", "pw3" })
            {
                var frame = layer.CaptureResearch(root.Handle, captureMode); using var stream = new MemoryStream(frame.Png); using var bitmap = new Bitmap(stream); int nonwhite = 0;
                for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2) { var pixel = bitmap.GetPixel(x, y); if (pixel.R < 235 || pixel.G < 235 || pixel.B < 235) nonwhite++; }
                bool expectedPixels = mode == "software-hook" ? nonwhite > 3000 : nonwhite == 0; visualVerified &= expectedPixels;
                captures.Add(new { captureMode, pixels = WpfCaptureResearch.Pixels(frame, Path.Combine(folder, captureMode + ".png")), nonwhite, expectedPixels });
            }
            var initial = layer.ObserveAccessible(root.Handle); File.WriteAllText(Path.Combine(folder, "initial-observation.json"), JsonSerializer.Serialize(initial, new JsonSerializerOptions { WriteIndented = true }));
            var steps = new List<object>(); bool inputVerified = false;
            if (mode == "software-hook")
            {
                JsonElement Node(JsonElement snapshot, string id) => snapshot.GetProperty("nodes").EnumerateArray().First(node => node.GetProperty("id").GetString() == id);
                string Value(JsonElement snapshot, string id) => Node(snapshot, id).GetProperty("value").GetString() ?? "";
                JsonElement Step(string name, Action action, Func<JsonElement, bool>? expected = null)
                {
                    action(); Thread.Sleep(600);
                    var snapshot = layer.ObserveAccessible(root.Handle);
                    var until = Stopwatch.StartNew();
                    while (expected != null && !expected(snapshot) && until.ElapsedMilliseconds < 4000) { Thread.Sleep(100); snapshot = layer.ObserveAccessible(root.Handle); }
                    bool correct = expected == null || expected(snapshot); passed &= correct;
                    File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                    var frame = layer.CaptureVisualOutput(root.Handle); File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.Png);
                    steps.Add(new { name, frame.CaptureMilliseconds, frame.NativeRenderingMilliseconds, frame.EncodingMilliseconds, correct });
                    return snapshot;
                }
                void Click(int x, int y, ControlOwner sender = ControlOwner.Automation) => layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true, sender);
                Step("search-open", () => Click(610, 38), snapshot => Value(snapshot, "searchBox") == "");
                Step("search-typed", () => layer.SendKeyboardInput(root.Handle, "ProbeWindow"), snapshot => Value(snapshot, "searchBox") == "ProbeWindow");
                layer.TakeHumanControl(); bool rejected = false; try { layer.SendKeyboardInput(root.Handle, "REJECT"); } catch (InvalidOperationException) { rejected = true; }
                Step("human-search", () => { layer.SendQueueStateChord(root.Handle, [0x11, 0x41], sender: ControlOwner.Human); layer.SendKeyboardInput(root.Handle, "Program", ControlOwner.Human); }, snapshot => Value(snapshot, "searchBox") == "Program");
                layer.ReturnToAutomation();
                Step("search-resumed", () => { layer.SendQueueStateChord(root.Handle, [0x11, 0x41]); layer.SendKeyboardInput(root.Handle, "ProbeWindow"); }, snapshot => Value(snapshot, "searchBox") == "ProbeWindow");
                Step("navigate-result", () => { layer.SendKey(root.Handle, 0x28); layer.SendKey(root.Handle, 0x0D); }, snapshot => Value(snapshot, "textEditor").Contains("ProbeWindow(bool"));
                var expanded = Step("close-search", () => Click(846, 70), snapshot => Node(snapshot, "textEditor").GetProperty("height").GetDouble() > 250);
                Step("scroll-code", () => layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 650, 290), new(MouseMessage.Wheel, 650, 290, Delta: -720)], true));
                using var beforeScroll = new Bitmap(Path.Combine(folder, "close-search.png")); using var afterScroll = new Bitmap(Path.Combine(folder, "scroll-code.png"));
                int scrollPixelChanges = 0;
                for (int y = 90; y < 370; y += 2) for (int x = 340; x < 825; x += 2) if (beforeScroll.GetPixel(x, y).ToArgb() != afterScroll.GetPixel(x, y).ToArgb()) scrollPixelChanges++;
                double beforePercent = Node(expanded, "textEditor").GetProperty("verticalScrollPercent").GetDouble();
                double afterPercent = Node(layer.ObserveAccessible(root.Handle), "textEditor").GetProperty("verticalScrollPercent").GetDouble();
                bool scrollVerified = scrollPixelChanges > 1000 && afterPercent > beforePercent; passed &= scrollVerified;
                Step("drag-code", () => layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 400, 145), new(MouseMessage.LeftDown, 400, 145, 1), new(MouseMessage.Move, 650, 190, 1), new(MouseMessage.LeftUp, 650, 190)], true), snapshot => Node(snapshot, "textEditor").GetProperty("selection").EnumerateArray().Any(part => part.GetString()!.Length > 0));
                bool sameProcess = layer.ApplicationProcessId == root.ProcessId; inputVerified = rejected && sameProcess && steps.All(step => JsonSerializer.SerializeToElement(step).GetProperty("correct").GetBoolean()); passed &= inputVerified;
                inputVerified &= scrollVerified; steps.Add(new { rejected, sameProcess, scrollVerified, scrollPixelChanges, beforePercent, afterPercent });
            }
            int[] ids = layer.GetProcessIds(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(4)); if (!graceful) layer.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
            passed &= cleanup && visualVerified; results.Add(new { mode, sourcePid = root.ProcessId, captures, visualVerified, steps, inputTested = mode == "software-hook", inputVerified, graceful, cleanup }); Console.WriteLine(JsonSerializer.Serialize(results[^1]));
        }
        bool binariesUnchanged = beforeHashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Executable)!, pair.Key)))));
        Native.GetCursorPos(out var after);
        passed &= binariesUnchanged;
        File.WriteAllText(Path.Combine(output, "ilspy.json"), JsonSerializer.Serialize(new { passed, results, binariesUnchanged, beforeHashes, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == after.X && cursor.Y == after.Y, inputBefore, inputAfter = Native.LastInputTick(), note = "Official unmodified ILSpy portable WPF application; only owned disposable assembly metadata loaded. Explicit private config and new instance, update checks disabled. Targeted message input; UIA only reads resulting values, document, viewport and selection. Physical human acceptance pending. Software hook is an opt-in process-local compatibility adapter." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

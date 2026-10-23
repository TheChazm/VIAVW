using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Installed IDE, normal package loading, existing experimental profile. All input
// goes through the same serialized private-desktop operations as the viewer.
internal static class VisualStudioPrivateVerification
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static VisualStudioResearch.Setup Prepare(string output, string seedFile)
    {
        var seed = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(seedFile))!;
        VisualStudioResearch.ValidateSetup(seed, seedFile);
        GpuBridgeVerification.PrepareOutput(output);
        var setup = VisualStudioResearch.Prepare(output);
        setup = setup with { Suffix = seed.Suffix, Arguments = ["/RootSuffix", seed.Suffix, "/SafeMode", "/NoSplash", "/Log", Path.Combine(output, "ActivityLog.xml"), setup.Solution] };
        File.WriteAllText(Path.Combine(output, "launch.json"), JsonSerializer.Serialize(setup, Json));
        VisualStudioResearch.ValidateSetup(setup, Path.Combine(output, "launch.json")); return setup;
    }
    internal static int Run(string output, string seedFile, string mode)
    {
        if (mode is not ("options" or "options-command" or "software-options" or "options-keyboard" or "options-keyboard-release" or "options-character" or "options-character-primed" or "options-character-posted" or "workflow" or "workflow-tab" or "workflow-release" or "workflow-build")) throw new ArgumentException("Choose a controlled Visual Studio research mode.");
        bool releaseFirst = mode is "options-keyboard-release" or "workflow-release";
        foreach (var process in Process.GetProcessesByName("devenv")) { using (process) throw new InvalidOperationException("An existing Visual Studio instance must not be reused."); }
        var setup = Prepare(output, seedFile);
        string[] arguments = setup.Arguments.Where(a => a != "/SafeMode").ToArray();
        if (mode is "options-command" or "software-options") arguments = [..arguments, "/Command", "Tools.Options"];
        File.WriteAllText(Path.Combine(output, "effective-launch.json"), JsonSerializer.Serialize(new { setup.Executable, arguments, environmentOverrides = "none", setup.Suffix }, Json));
        var personal = VisualStudioResearch.PersonalSettings();
        string binaryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable)));
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var identities = new Dictionary<int, ProcessIdentity>(); var frames = new List<object>(); var checks = new List<string>(); var resources = new List<object>();
        long main = 0; int pid = 0; bool graceful = false, ideExitedBeforeDispose = false; string stage = "launch", failure = ""; var clock = Stopwatch.StartNew();
        void Census()
        {
            foreach (var id in ProcessCensus.Read(layer.GetProcessIds())) identities[id.Pid] = id;
        }
        void Resource(string name)
        {
            Census(); double cpu = 0; long working = 0, privateBytes = 0; int count = 0; var processes = new List<object>();
            foreach (var id in identities.Values)
            {
                try { using var p = Process.GetProcessById(id.Pid); if (p.StartTime.ToFileTimeUtc() != id.CreationFileTime) continue; double processCpu = p.TotalProcessorTime.TotalMilliseconds; cpu += processCpu; long ws = p.WorkingSet64, pb = p.PrivateMemorySize64; working += ws; privateBytes += pb; count++; processes.Add(new { id.Pid, id.CreationFileTime, id.Name, cpuMs = processCpu, workingSetMiB = ws / 1048576.0, privateMiB = pb / 1048576.0 }); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            resources.Add(new { name, ms = clock.Elapsed.TotalMilliseconds, processCount = count, liveCpuMilliseconds = cpu, summedWorkingSetMiB = working / 1048576.0, privateMiB = privateBytes / 1048576.0, processes });
        }
        void Wait(Func<bool> test, string reason, int timeout = 10000)
        {
            var watch = Stopwatch.StartNew(); do { Census(); if (test()) return; Thread.Sleep(100); } while (watch.ElapsedMilliseconds < timeout); throw new TimeoutException(reason);
        }
        void Check(bool test, string name) { if (!test) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine(name); }
        void Frame(string name, long root)
        {
            var frame = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), frame.Png);
            using var stream = new MemoryStream(frame.Png); using var bitmap = new Bitmap(stream); int samples = 0, nonwhite = 0; var colors = new HashSet<int>();
            for (int y = 0; y < bitmap.Height; y += 4) for (int x = 0; x < bitmap.Width; x += 4) { var c = bitmap.GetPixel(x, y); samples++; if (c.R < 250 || c.G < 250 || c.B < 250) nonwhite++; colors.Add(c.ToArgb()); }
            frames.Add(new { name, frame.Width, frame.Height, frame.Backend, frame.CaptureMilliseconds, frame.NativeRenderingMilliseconds, frame.EncodingMilliseconds, samples, nonwhite, sampledColors = colors.Count });
        }
        JsonElement Observe(string name, long root, string? id = null)
        { var a = layer.ObserveAccessible(root, id); File.WriteAllText(Path.Combine(output, name + ".json"), a.GetRawText()); return a; }
        void Chord(long root, params int[] keys)
        { long focus = layer.GetFocusedWindow(root); layer.SendQueueStateChord(focus != 0 ? focus : root, keys, posted: true, followFocusOnKeyUp: true, releaseStateBeforeKeyUp: releaseFirst); Thread.Sleep(250); }
        void ClickNode(long root, JsonElement node)
        {
            var origin = layer.ClientOrigin(root);
            int x = (int)(node.GetProperty("x").GetDouble() + node.GetProperty("width").GetDouble() / 2) - origin.X;
            int y = (int)(node.GetProperty("y").GetDouble() + node.GetProperty("height").GetDouble() / 2) - origin.Y;
            var target = layer.HitTest(root, x, y);
            layer.SendQueueMouseSequence(target.Window, [new(MouseMessage.Move, target.X, target.Y), new(MouseMessage.LeftDown, target.X, target.Y, 1), new(MouseMessage.LeftUp, target.X, target.Y)], true); Thread.Sleep(400);
        }
        void Open(string path)
        {
            Chord(main, 0x11, 0x4F); WindowInfo? picker = null;
            Wait(() => (picker = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770" && w.Text.Contains("Open", StringComparison.OrdinalIgnoreCase))) != null, "Open File dialog did not appear.");
            Frame("open-" + Path.GetFileName(path), picker!.Handle);
            long field = layer.GetFocusedWindow(picker.Handle); var windows = layer.GetWindows();
            Check(windows.Any(w => w.Handle == field && w.ClassName.Contains("Edit", StringComparison.OrdinalIgnoreCase)), "Filename field focused: " + Path.GetFileName(path));
            Chord(field, 0x11, 0x41); layer.PostKeyboardInput(field, path); layer.PostKey(field, 0x0D);
            Wait(() => !layer.GetWindows().Any(w => w.Handle == picker.Handle && w.Visible), "Open File dialog did not close."); Thread.Sleep(600);
            var modal = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770");
            if (modal != null) { Frame("unexpected-modal", modal.Handle); Observe("unexpected-modal", modal.Handle); throw new InvalidOperationException("Unexpected modal after opening code; no further input."); }
            var a = Observe("opened-" + Path.GetFileName(path), main, "WpfTextView");
            Check(a.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("text").GetString() == File.ReadAllText(path)), "Application-loaded document matches disk: " + Path.GetFileName(path));
            Frame("document-" + Path.GetFileName(path), main);
        }
        void ReplaceSave(string path, string text)
        {
            long focus = layer.GetFocusedWindow(main); Check(focus != 0, "Private editor focus exists: " + Path.GetFileName(path));
            Chord(main, 0x11, 0x41);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++) { if (i != 0) { layer.PostKey(focus, 0x0D); Thread.Sleep(150); } layer.PostKeyboardInput(focus, lines[i]); Thread.Sleep(100); }
            Thread.Sleep(300); Chord(main, 0x11, 0x53);
            Wait(() => File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n') == text.Replace("\r\n", "\n").TrimEnd('\n'), "Saved code did not match typed content: " + path);
            Check(true, "Ordinary typing and Ctrl+S saved exact content: " + Path.GetFileName(path));
            Observe("saved-" + Path.GetFileName(path), main, "WpfTextView"); Frame("saved-" + Path.GetFileName(path), main);
        }
        try
        {
            pid = layer.LaunchApplication(setup.Executable, arguments, output); Console.WriteLine("Owned IDE " + pid + " / " + layer.Id);
            Wait(() => { var root = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text.Contains("VIAVWResearch -", StringComparison.Ordinal)); main = root?.Handle ?? 0; return main != 0; }, "IDE did not open disposable solution.", 90000);
            Thread.Sleep(1500); Resource("startup"); Frame("01-startup", main); Observe("01-startup", main);
            File.WriteAllText(Path.Combine(output, "startup-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json));
            Check(!layer.GetWindows().Any(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770" && !((mode is "options-command" or "software-options") && w.Text == "Options")), "No unexpected native startup modal");
            if (mode.StartsWith("options", StringComparison.Ordinal) || mode == "software-options")
            {
                stage = "options discovery";
                if (mode == "options")
                {
                layer.SendQueueMouseSequence(main, [new(MouseMessage.Move, 400, 150), new(MouseMessage.LeftDown, 400, 150, 1), new(MouseMessage.LeftUp, 400, 150)], true);
                var mainTree = Observe("tools-menu-before", main);
                ClickNode(main, mainTree.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "Tools"));
                bool optionsClicked = false;
                foreach (var root in layer.GetWindows().Where(w => w.Parent == 0 && w.Visible))
                {
                    var tree = Observe("tools-popup-" + root.Handle, root.Handle);
                    var options = tree.GetProperty("nodes").EnumerateArray().FirstOrDefault(n => !n.GetProperty("offscreen").GetBoolean() && n.GetProperty("name").GetString()!.StartsWith("Options", StringComparison.Ordinal) && n.GetProperty("type").GetString() == "ControlType.MenuItem");
                    if (options.ValueKind != JsonValueKind.Undefined) { ClickNode(root.Handle, options); optionsClicked = true; break; }
                }
                Check(optionsClicked, "Options menu item clicked using observed private UI bounds"); Thread.Sleep(600);
                }
                if (mode is "options-keyboard" or "options-keyboard-release")
                {
                    layer.SendQueueMouseSequence(main, [new(MouseMessage.Move, 400, 150), new(MouseMessage.LeftDown, 400, 150, 1), new(MouseMessage.LeftUp, 400, 150)], true);
                    Chord(main, 0x12, 0x54); Observe("after-alt-t", main); Frame("after-alt-t", main);
                    long focused = layer.GetFocusedWindow(main); layer.PostKey(focused != 0 ? focused : main, 0x4F);
                    Thread.Sleep(700);
                }
                if (mode.StartsWith("options-character", StringComparison.Ordinal))
                {
                    if (mode != "options-character") layer.SendQueueMouseSequence(main, [new(MouseMessage.Move, 400, 150), new(MouseMessage.LeftDown, 400, 150, 1), new(MouseMessage.LeftUp, 400, 150)], true);
                    layer.ResearchMenuCharacter(main, 't', posted: mode == "options-character-posted"); Thread.Sleep(400);
                    Observe("after-menu-character", main); Frame("after-menu-character", main);
                    File.WriteAllText(Path.Combine(output, "menu-character-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json));
                    long focus = layer.GetFocusedWindow(main); layer.PostKeyboardInput(focus != 0 ? focus : main, "o"); Thread.Sleep(700);
                }
                var windows = layer.GetWindows(); File.WriteAllText(Path.Combine(output, "options-windows.json"), JsonSerializer.Serialize(windows, Json));
                foreach (var root in windows.Where(w => w.Parent == 0 && w.Visible && (w.Text.Contains("Options", StringComparison.OrdinalIgnoreCase) || w.Handle == main))) { Frame("options-" + root.Handle, root.Handle); Observe("options-" + root.Handle, root.Handle); }
                var dialog = windows.SingleOrDefault(w => w.Parent == 0 && w.Visible && w.Text == "Options");
                Check(dialog != null, mode.StartsWith("options-character", StringComparison.Ordinal) ? "Options opened through native system character and ordinary character input" : mode.StartsWith("options-keyboard", StringComparison.Ordinal) ? "Options opened through Alt+T then O" : mode == "options" ? "Options dialog opened through pointer menu input" : "Options dialog opened by documented Tools.Options startup command");
                if (mode == "software-options")
                {
                    stage = "software graphics selection";
                    JsonElement Setting(string prefix) => layer.ObserveAccessible(dialog!.Handle).GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "ControlType.CheckBox" && n.GetProperty("name").GetString()!.StartsWith(prefix, StringComparison.Ordinal)).Clone();
                    var automatic = Setting("Automatically adjust visual experience");
                    if (automatic.GetProperty("toggleState").GetString() == "On") ClickNode(dialog!.Handle, automatic);
                    Check(Setting("Automatically adjust visual experience").GetProperty("toggleState").GetString() == "Off", "Automatic graphics selection disabled in experimental profile");
                    var hardware = Setting("Use hardware graphics acceleration");
                    if (hardware.GetProperty("toggleState").GetString() == "On") ClickNode(dialog!.Handle, hardware);
                    Check(Setting("Use hardware graphics acceleration").GetProperty("toggleState").GetString() == "Off", "Hardware graphics option disabled in experimental profile");
                    Observe("software-options-before-apply", dialog!.Handle); Frame("software-options-before-apply", dialog.Handle);
                    var ok = layer.GetWindows().Single(w => w.ClassName == "Button" && w.Text.Replace("&", "") == "OK" && LayerWindowSelection.InSubtree(layer.GetWindows(), w.Handle, dialog.Handle));
                    layer.SendMouseInput(ok.Handle, 10, 10); Wait(() => !layer.GetWindows().Any(w => w.Handle == dialog.Handle && w.Visible), "Options dialog did not close.");
                    Thread.Sleep(1500); Frame("software-after-apply", main); Observe("software-after-apply", main); Resource("software selected");
                }
            }
            else
            {
                stage = "open first code file";
                layer.SendQueueMouseSequence(main, [new(MouseMessage.Move, 400, 150), new(MouseMessage.LeftDown, 400, 150, 1), new(MouseMessage.LeftUp, 400, 150)], true);
                Open(setup.First);
                stage = "edit and save first"; ReplaceSave(setup.First, "namespace ViavwDisposable { public static class Greeting { public const string Text = \"Edited greeting\"; } }\r\n// VIAVW first file saved independently");
                Resource("first saved");
                stage = "open and edit second"; Open(setup.Second); ReplaceSave(setup.Second, "namespace ViavwDisposable { public static class Details { public const string Status = \"Reviewed\"; } }\r\n// VIAVW second file saved independently");
                bool tab = mode is "workflow-tab" or "workflow-release";
                string switchName = tab ? "Ctrl+Tab" : "Ctrl+F6";
                stage = "switch files with " + switchName; Chord(main, 0x11, tab ? 0x09 : 0x75);
                Observe("switch-immediate", main); Frame("switch-immediate", main);
                Wait(() => layer.ObserveAccessible(main, "WpfTextView").GetRawText().Contains("Edited greeting", StringComparison.Ordinal), switchName + " did not return to the first document.", 4000);
                Observe("switched-editor", main, "WpfTextView"); Check(true, switchName + " returned to first edited document"); Frame("05-switched-first", main);
                stage = "close and reopen"; Chord(main, 0x11, 0x73); Open(setup.First);
                stage = "ownership"; layer.RequestAttention("Review disposable Visual Studio code"); layer.TakeHumanControl();
                Check(layer.Owner == ControlOwner.Human, "Human ownership granted");
                bool rejected = false; try { layer.SendKeyboardInput(main, "REJECTED_AUTOMATION"); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Wrong-owner automation input rejected");
                long humanFocus = layer.GetFocusedWindow(main);
                layer.SendQueueStateChord(humanFocus, [0x11, 0x23], posted: true, sender: ControlOwner.Human, followFocusOnKeyUp: true);
                layer.PostKeyboardInput(humanFocus, " // Human-sender verification", ControlOwner.Human); Thread.Sleep(200);
                layer.SendQueueStateChord(humanFocus, [0x11, 0x53], posted: true, sender: ControlOwner.Human, followFocusOnKeyUp: true);
                Wait(() => File.ReadAllText(setup.First).EndsWith(" // Human-sender verification", StringComparison.Ordinal), "Authorized Human-sender edit was not saved.");
                Check(true, "Automated Human-sender input saved in the same IDE"); Frame("07-human-sender-save", main);
                layer.ReturnToAutomation();
                rejected = false; try { layer.SendKeyboardInput(main, "REJECTED_HUMAN", ControlOwner.Human); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Human input rejected after automation resumes");
                Check(identities.ContainsKey(pid) && CompatibilityVerification.Alive(pid), "Same IDE process survived file workflow and ownership changes");
                if (mode == "workflow-build")
                {
                    stage = "build solution through IDE";
                    string dll = Path.Combine(output, "DisposableProject", "bin", "Debug", "ViavwDisposable.dll");
                    Check(!File.Exists(dll), "No build output exists before the IDE build shortcut");
                    var buildClock = Stopwatch.StartNew(); Chord(main, 0x11, 0x10, 0x42);
                    Wait(() => File.Exists(dll), "IDE build did not create the disposable assembly.", 45000);
                    var constants = VisualStudioResearch.ReadCompiledConstants(dll);
                    Check(constants.GetValueOrDefault("Text") == "Edited greeting" && constants.GetValueOrDefault("Status") == "Reviewed", "IDE-generated assembly contains both edited constants");
                    File.WriteAllText(Path.Combine(output, "ide-build.json"), JsonSerializer.Serialize(new { shortcut = "Ctrl+Shift+B", buildMilliseconds = buildClock.Elapsed.TotalMilliseconds, dll, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))), constants, externalBuildProcessInvokedByHarness = false }, Json));
                    Frame("08-ide-build", main); Observe("08-ide-build", main);
                }
                Resource("workflow complete");
                stage = "idle measurement"; Resource("idle before"); Thread.Sleep(2000); Resource("idle after");
            }
            stage = "complete";
        }
        catch (Exception error)
        {
            failure = error.ToString(); Console.Error.WriteLine(failure);
            if (!layer.Closed && main != 0) try { Frame("failure-main", main); Observe("failure-editor", main, "WpfTextView"); Observe("failure-accessible", main); File.WriteAllText(Path.Combine(output, "failure-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json)); } catch { }
        }
        finally
        {
            if (!layer.Closed) { Census(); Resource("before close"); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(8)); } catch { } ideExitedBeforeDispose = pid != 0 && !CompatibilityVerification.Alive(pid); }
            layer.Dispose(); Thread.Sleep(300);
        }
        var survivors = ProcessCensus.Read(identities.Keys.ToArray()).Where(now => identities.TryGetValue(now.Pid, out var old) && now.CreationFileTime == old.CreationFileTime).ToArray();
        var physical = monitor.Complete(identities.Keys.ToArray()); var after = VisualStudioResearch.PersonalSettings();
        bool settingsUnchanged = personal.Count == after.Count && personal.All(k => after.TryGetValue(k.Key, out string? hash) && hash == k.Value);
        bool binaryUnchanged = binaryHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable)));
        bool passed = failure.Length == 0 && survivors.Length == 0 && physical.GetProperty("passed").GetBoolean() && settingsUnchanged && binaryUnchanged;
        File.WriteAllText(Path.Combine(output, "private-workflow.json"), JsonSerializer.Serialize(new { passed, functionalWorkflowPassed = mode.StartsWith("workflow", StringComparison.Ordinal) && stage == "complete", automatedVisualAssessment = "Pixel statistics only; inspect saved screenshots for readability", mode, releaseStateBeforeKeyUp = releaseFirst, stage, failure, elapsedMs = clock.Elapsed.TotalMilliseconds, logicalProcessors = Environment.ProcessorCount, setup, effectiveArguments = arguments, checks, frames, resources, graceful, ideExitedBeforeDispose, identities = identities.Values, survivors, physical, settingsUnchanged, binaryUnchanged, instrumentation = "none", physicalHumanAcceptance = "pending", outsideProfilePaths = VisualStudioResearch.OutsideProfiles(setup.Suffix) }, Json));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, stage, checks = checks.Count, survivors = survivors.Length, output })); return passed ? 0 : 1;
    }
}

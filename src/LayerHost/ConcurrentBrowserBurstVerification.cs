using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class ConcurrentBrowserBurstVerification
{
    sealed class Subject : IDisposable
    {
        internal readonly ApplicationLayer Layer;
        internal readonly PassivePageServer Server;
        internal readonly List<JsonElement> Evidence = [];
        internal readonly string Name, Folder;
        internal readonly long Root;
        internal readonly int Height, Pid;
        readonly bool activation;
        readonly bool notifications;
        readonly int inspectionSettle;
        readonly ResearchBrowserProfile? profileLease;
        internal Subject(LayerManager manager, string name, string output, string html, bool activation = false, bool notifications = false, int inspectionSettle = 150)
        {
            this.inspectionSettle = inspectionSettle;
            this.notifications = notifications;
            this.activation = activation; Name = name; Folder = Path.Combine(output, name); Directory.CreateDirectory(Folder); Server = new(html); Layer = manager.CreateLayer();
            try
            {
                string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
                if (activation) profileLease = new ResearchBrowserProfile(project, name == "A" ? "concurrent-chrome" : "concurrent-edge");
                string executable = activation && name == "B" ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : "C:/Program Files/Google/Chrome/Application/chrome.exe";
                Pid = Layer.LaunchApplication(executable, ["--user-data-dir=" + (profileLease?.Path ?? Path.Combine(Folder, "profile")), "--app=" + Server.Url, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--disable-component-update", "--disable-backgrounding-occluded-windows", "--window-size=800,660"], Folder);
                Wait(state => state.GetProperty("webgl").GetBoolean(), 20000);
                Root = Layer.GetWindows().Where(window => window.Parent == 0 && window.Visible && window.ClassName == "Chrome_WidgetWin_1").OrderByDescending(window => window.Width * window.Height).First().Handle;
                var frame = Layer.CaptureVisualOutput(Root, CaptureBackend.PrintWindowFullContent); Height = frame.Height;
                if (activation)
                {
                    var rectangle = State.GetProperty("editor"); int x = (int)(rectangle.GetProperty("x").GetDouble() + rectangle.GetProperty("width").GetDouble() / 2), y = (int)(rectangle.GetProperty("y").GetDouble() + rectangle.GetProperty("height").GetDouble() / 2) + Height - State.GetProperty("innerHeight").GetInt32();
                    Layer.SendQueueMouseSequence(Root, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true);
                    Layer.SendKeyboardInput(Root, "INITIAL_" + name); Wait(state => state.GetProperty("text").GetString() == "INITIAL_" + name);
                }
                Layer.HideLayer(Root); Wait(state => state.GetProperty("visibility").GetString() == "hidden");
            }
            catch { Layer.Dispose(); Server.Dispose(); profileLease?.Dispose(); throw; }
        }
        internal JsonElement State => Server.Observation ?? throw new InvalidOperationException("No passive page state.");
        internal JsonElement Wait(Func<JsonElement, bool> condition, int deadline = 6000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < deadline) { var state = Server.Observation; if (state != null && condition(state.Value)) return state.Value; Thread.Sleep(25); }
            throw new TimeoutException(Name + " observation deadline; last: " + Server.Observation);
        }
        internal bool Hidden => !Layer.GetWindows().First(window => window.Handle == Root).Visible;
        internal bool Burst(string text, ControlOwner sender = ControlOwner.Automation)
        {
            Wait(state => state.GetProperty("visibility").GetString() == "hidden"); var before = State; int changes = before.GetProperty("changes").GetInt32(), frames = before.GetProperty("frames").GetInt32();
            PresentedInput Click(string name)
            {
                var rect = before.GetProperty(name); int x = (int)(rect.GetProperty("x").GetDouble() + rect.GetProperty("width").GetDouble() / 2), y = (int)(rect.GetProperty("y").GetDouble() + rect.GetProperty("height").GetDouble() / 2) + Height - before.GetProperty("innerHeight").GetInt32();
                return new(PresentedInputKind.MouseSequence, Steps: [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)]);
            }
            PresentedInput[] input = activation ? [new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: text), new(PresentedInputKind.KeyChord, Keys: [0x9]), new(PresentedInputKind.KeyChord, Keys: [0x20]), new(PresentedInputKind.KeyChord, Keys: [0x10, 0x9])] : [Click("editor"), new(PresentedInputKind.KeyChord, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Text: text), Click("toggle")];
            var watch = Stopwatch.StartNew(); var result = notifications ? Layer.ResearchHiddenInteraction(Root, input, settleMilliseconds: 0, sender: sender) : Layer.PresentForInput(Root, input, fullContent: true, sender: sender, notifyActivation: activation); double inputRpc = watch.Elapsed.TotalMilliseconds;
            var after = Wait(state => state.GetProperty("text").GetString() == text && state.GetProperty("changes").GetInt32() == changes + 1 && state.GetProperty("visibility").GetString() == "hidden");
            if (notifications) result = result with { Frame = Layer.InspectHiddenWindow(Root, true, inspectionSettle) };
            double rpc = watch.Elapsed.TotalMilliseconds;
            var pixels = PrivateBrowserVisibilityVerification.Pixels(result.Frame!, after, Path.Combine(Folder, text + ".png"));
            bool noAdditionalMouse = !activation || after.GetProperty("mouseDowns").GetInt32() == before.GetProperty("mouseDowns").GetInt32();
            bool correct = Hidden && result.InitiallyHidden && result.HiddenAfter && pixels.GetProperty("colorCorrect").GetBoolean() && pixels.GetProperty("encodedFrame").GetInt32() > frames && Layer.ApplicationProcessId == Pid && noAdditionalMouse;
            Evidence.Add(JsonSerializer.SerializeToElement(new { text, sender = sender.ToString(), rpcMs = rpc, inputRpcMs = inputRpc, notifications, result.Milliseconds, result.ActivationNotified, framesBefore = frames, noAdditionalMouse, correct, pixels, after })); return correct;
        }
        bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; Layer.Dispose(); Server.Dispose(); profileLease?.Dispose(); }
    }
    internal static int Run(string output, bool activation = false, bool notifications = false, int inspectionSettle = 150)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); string html = activation ? BrowserActivationVerification.Page(project) : File.ReadAllText(Path.Combine(project, "experiments/gpu-visibility.html"));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputBefore = Native.LastInputTick(); string desktop = Native.InputDesktopName();
        var events = new List<JsonElement>(); int[] owned = []; bool passed = true, aGraceful = false, bGraceful = false; string? failure = null; Subject? a = null, b = null;
        using var manager = new LayerManager();
        var drivers = new List<Task>();
        void Check(string name, bool result, object? detail = null) { passed &= result; events.Add(JsonSerializer.SerializeToElement(new { name, passed = result, detail })); Console.WriteLine((result ? "PASS " : "FAIL ") + name); }
        try
        {
            a = new(manager, "A", output, html, activation, notifications, inspectionSettle); b = new(manager, "B", output, html, activation, notifications, inspectionSettle); owned = a.Layer.GetProcessIds().Concat(b.Layer.GetProcessIds()).ToArray();
            Check("independent private hardware browser layers", a.Layer.Id != b.Layer.Id && a.Pid != b.Pid && a.Hidden && b.Hidden && new[] { a, b }.All(subject => subject.State.GetProperty("renderer").GetString()!.Contains("NVIDIA")), new { aPid = a.Pid, bPid = b.Pid, aDesktop = a.Layer.Id, bDesktop = b.Layer.Id });
            PresentedInteraction Interact(Subject subject, PresentedInput[] commands, ControlOwner sender = ControlOwner.Automation) => notifications ? subject.Layer.ResearchHiddenInteraction(subject.Root, commands, settleMilliseconds: 0, sender: sender) : subject.Layer.PresentForInput(subject.Root, commands, sender: sender, notifyActivation: activation);
            bool foreignRejected = false; try { Interact(a, [new(PresentedInputKind.Text, Window: b.Root, Text: "FOREIGN")]); } catch (LayerOperationException) { foreignRejected = true; }
            a.Layer.TakeHumanControl(); bool wrongOwnerRejected = false; try { Interact(a, [new(PresentedInputKind.Text, Text: "REJECT")]); } catch (InvalidOperationException) { wrongOwnerRejected = true; }
            Check("foreign and non-owner bursts rejected while hidden", foreignRejected && wrongOwnerRejected && a.Hidden && b.Hidden);
            var activity = Stopwatch.StartNew(); var gpu = Task.Run(() => GpuCounters.Sample(owned));
            var aDriver = Task.Run(() => Enumerable.Range(0, 6).All(cycle => a.Burst("A_HUMAN_" + cycle, ControlOwner.Human)));
            var bDriver = Task.Run(() => Enumerable.Range(0, 6).All(cycle => b.Burst("B_AUTO_" + cycle)));
            drivers.AddRange([aDriver, bDriver]);
            Task.WaitAll(aDriver, bDriver);
            Check("concurrent fresh native bursts and independent owners", aDriver.Result && bDriver.Result && a.Layer.Owner == ControlOwner.Human && b.Layer.Owner == ControlOwner.Automation, new { elapsedMs = activity.Elapsed.TotalMilliseconds, gpu = gpu.GetAwaiter().GetResult() });
            string aText = a.State.GetProperty("text").GetString()!; int oldWorker = a.Layer.WorkerProcessId;
            var peerActivity = Task.Run(() => Enumerable.Range(0, 6).All(cycle => b.Burst("B_RECOVERY_" + cycle))); Thread.Sleep(75);
            drivers.Add(peerActivity);
            var recovery = Stopwatch.StartNew(); a.Layer.RestartWorker(); double recoveryMs = recovery.Elapsed.TotalMilliseconds;
            bool recovered = a.Hidden && a.Layer.WorkerProcessId != oldWorker && a.Layer.ApplicationProcessId == a.Pid && a.Layer.Owner == ControlOwner.Human && a.State.GetProperty("text").GetString() == aText;
            Check("hidden worker recovery preserves source and peer work", recovered && a.Burst("A_RECOVERED_HUMAN", ControlOwner.Human) && peerActivity.GetAwaiter().GetResult(), new { recoveryMs });
            a.Layer.ReturnToAutomation(); bool humanRejected = false; try { Interact(a, [new(PresentedInputKind.Text, Text: "REJECT")], ControlOwner.Human); } catch (InvalidOperationException) { humanRejected = true; }
            Check("automation resumes after hidden handoff", humanRejected && a.Burst("A_RESUMED_AUTO"));
            events.Add(JsonSerializer.SerializeToElement(new { name = "memory", values = a.Layer.GetProcessIds().Concat(b.Layer.GetProcessIds()).Distinct().Select(id => { using var process = Process.GetProcessById(id); return new { pid = id, name = process.ProcessName, workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray() }));
            owned = owned.Concat(a.Layer.GetProcessIds()).Concat(b.Layer.GetProcessIds()).Distinct().ToArray();
            aGraceful = a.Layer.CloseGracefully(TimeSpan.FromSeconds(3)); if (!aGraceful) a.Layer.Dispose();
            Check("closing A preserves independently usable hidden B", CompatibilityVerification.Alive(b.Pid) && b.Burst("B_AFTER_A_CLOSE"));
            bGraceful = b.Layer.CloseGracefully(TimeSpan.FromSeconds(3)); Check("both close gracefully", aGraceful && bGraceful);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            a?.Layer.Dispose(); b?.Layer.Dispose(); manager.Dispose();
            try { Task.WhenAll(drivers).GetAwaiter().GetResult(); } catch (Exception exception) { events.Add(JsonSerializer.SerializeToElement(new { name = "driver failure during cleanup", error = exception.ToString() })); }
            a?.Dispose(); b?.Dispose();
        }
        Thread.Sleep(300); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); Native.GetCursorPos(out var after);
        bool foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == after.X && pointer.Y == after.Y, inputDesktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && inputDesktopEquality, new { cleanup, foregroundEquality, pointerEquality, inputDesktopEquality, inputBefore, inputAfter = Native.LastInputTick() });
        File.WriteAllText(Path.Combine(output, "concurrent-browser-bursts.json"), JsonSerializer.Serialize(new { passed, activation, notifications, applications = activation ? new[] { "Chrome", "Edge" } : new[] { "Chrome", "Chrome" }, failure, events, a = a?.Evidence, b = b?.Evidence, owned, cleanup, aGraceful, bGraceful, note = "Two unchanged browser executables, separate private desktops/profiles/jobs. Native scoped keyboard/mouse or opt-in targeted activation notifications with PrintWindow GPU pixels. Activation mode selects each field once at startup, then uses keyboard only and designated reusable profiles. Passive local page observations only; no debug protocol, extension, renderer override or source hook. Notification mode keeps native visibility hidden during input and separately inspects through accepted temporary presentation. Finite transactions are serialized within a layer and concurrent between layers. Ownership commands simulate human handoff; physical hardware acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

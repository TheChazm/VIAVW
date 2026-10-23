using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Exercises the real viewer through its canvas on an outer private desktop.
// Timing ends at the published frame/command-ready report, not at physical scanout.
internal static class VisualStudioResponsivenessVerification
{
    internal static int Run(string output, string setupFile, string viewerExecutable, bool requireDeletion, bool copyPreview = false,bool windowDcPreview=false,bool asyncStatusReports=false,bool preciseMouseWaits=false,bool incrementalPaint=false,int preciseChordHoldMilliseconds=0,bool fastPreview=false,bool preferTextHint=false,bool nativeSubtreeGeometry=false,bool preparePointer=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        VisualStudioResearch.ValidateSetup(setup, setupFile);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")).TrimEnd(Path.DirectorySeparatorChar);
        viewerExecutable = Path.GetFullPath(viewerExecutable);
        if (!viewerExecutable.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(viewerExecutable) != "LayerHost.exe") throw new ArgumentException("Use a preserved VIAVW viewer within this project.");
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var timings = new List<object>(); var checks = new Dictionary<string, bool>(); int[] owned = []; string stage = "startup"; string? failure = null; long viewer = 0, canvas = 0;
        bool isolated=File.Exists(Path.Combine(Path.GetDirectoryName(setupFile)!,"isolated-profile.json"));
        var personal = VisualStudioResearch.PersonalSettings(allowUnavailable:isolated);
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Wait(Func<bool> condition, string reason, int timeout = 30000) { var watch = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(10); } while (watch.ElapsedMilliseconds < timeout); throw new TimeoutException(reason); }
        void Button(string name) => qa.SendMouseInput(qa.GetWindows().Single(w => w.Text == name).Handle, 10, 10);
        void Ack(long before, string name) => Wait(() => Preview().GetProperty("inputSequence").GetInt64() > before, "Viewer did not acknowledge " + name);
        void Chord(params int[] keys) { long before = Preview().GetProperty("inputSequence").GetInt64(); qa.SendQueueStateChord(canvas, keys); Ack(before, "chord"); }
        string ObservedText(JsonElement editor) { string text=editor.GetProperty("text").GetString()??"";return isolated?System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ",""):text; }
        JsonElement Editor(JsonElement observation)=>observation.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("id").GetString()=="WpfTextView"&&!n.GetProperty("offscreen").GetBoolean()).OrderByDescending(n=>n.GetProperty("width").GetDouble()*n.GetProperty("height").GetDouble()).First();
        string Text() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));return ObservedText(Editor(doc.RootElement)); }
        void ClickEditor(JsonElement editor)
        {
            var p=Preview();var origin=p.GetProperty("sourceClientOrigin");var presentation=p.GetProperty("presentation");
            int x=(int)Math.Ceiling((editor.GetProperty("x").GetDouble()+400-origin.GetProperty("X").GetInt32())*presentation.GetProperty("DrawWidth").GetInt32()/presentation.GetProperty("SourceWidth").GetInt32());
            int y=(int)Math.Ceiling((editor.GetProperty("y").GetDouble()+8-origin.GetProperty("Y").GetInt32())*presentation.GetProperty("DrawHeight").GetInt32()/presentation.GetProperty("SourceHeight").GetInt32());
            Input("focus-actual-source-editor",()=>qa.SendMouseInput(canvas,x,y));
        }
        void Inspect(string name)
        {
            string inspectButton = qa.GetWindows().Any(w => w.Text == "Inspect details") ? "Inspect details" : "Refresh";
            long observation = Preview().GetProperty("observationVersion").GetInt64(); Button(inspectButton);
            Wait(() =>
            {
                // Automatic frames are not failed inspections. Re-clicking
                // for each one queued slow UIA work ahead of the next input.
                long current=Preview().GetProperty("observationVersion").GetInt64();
                if(current<=observation)return false;
                using var snapshot=JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json")));
                if(snapshot.RootElement.GetProperty("nodes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()=="WpfTextView"&&!n.GetProperty("offscreen").GetBoolean()))return true;
                // Passive reads may publish an incomplete sample. Request the
                // next sample only after this explicit inspection has returned.
                observation=current;Button(inspectButton);return false;
            }, "Explicit inspection did not finish.");
            File.WriteAllBytes(Path.Combine(output, name + ".png"), qa.CaptureVisualOutput(viewer).Png);
        }
        void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); if (!passed) throw new InvalidOperationException(name); }
        void Input(string kind, Action send, int count = 1)
        {
            long before = Preview().GetProperty("inputSequence").GetInt64(); var watch = Stopwatch.StartNew(); send();
            Wait(() => Preview().GetProperty("inputSequence").GetInt64() >= before + count, "Frame publication did not catch up: " + kind);
            var p = Preview(); timings.Add(new { kind, count, milliseconds = watch.Elapsed.TotalMilliseconds, preview = p });
        }
        object? activity = null, idle = null;
        try
        {
            var startup = Stopwatch.StartNew(); string[] launchArguments=[incrementalPaint?"--run-vs-damage-viewer":preciseMouseWaits?"--run-vs-precise-viewer":asyncStatusReports?"--run-vs-async-report-viewer":windowDcPreview?"--run-vs-dc-viewer":copyPreview?"--run-vs-copy-preview-viewer":"--run-vs-private-viewer", Path.GetFullPath(setupFile), output];
            if(preciseChordHoldMilliseconds>0)launchArguments=["--run-vs-chord-viewer",setupFile,output,preciseChordHoldMilliseconds.ToString()];
            if(fastPreview)launchArguments=[preparePointer?"--run-vs-prepared-viewer":nativeSubtreeGeometry?"--run-vs-subtree-viewer":preferTextHint?"--run-vs-hint-viewer":"--run-vs-fast-viewer",setupFile,output];
            qa.LaunchApplication(viewerExecutable,launchArguments,output);
            Wait(() => File.Exists(Path.Combine(output, "viewer-preview.json")), "Viewer did not start.", 90000);
            var wins = qa.GetWindows(); viewer = wins.Single(w => w.Text == "VIAVW — private application layer").Handle; canvas = wins.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            timings.Add(new { kind = "startup-first-report", milliseconds = startup.Elapsed.TotalMilliseconds });
            Wait(() => Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs", StringComparison.Ordinal), "Startup did not present its document.", 60000);
            timings.Add(new { kind = "startup-document-title-report", milliseconds = startup.Elapsed.TotalMilliseconds, note = "Document title reached the preview; solution loading can still continue." });
            if(isolated)qa.EnablePrivateInputSession();Button("Human control"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Human owns input", StringComparison.Ordinal) || w.Text.StartsWith("Human | PID ", StringComparison.Ordinal))), "Human control was not applied.");
            Inspect("01-startup"); Thread.Sleep(2500);
            using(var initial=JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output,"viewer-accessible.json"))))ClickEditor(Editor(initial.RootElement));
            long sourcePid = Preview().GetProperty("applicationPid").GetInt64();
            stage = "click Enter typing latency";
            for (int trial = 0; trial < 5; trial++)
            {
                Chord(0x11, 0x41);
                Input("prepare-click-line", () => qa.PostKeyboardInput(canvas, "// click target"), 15);
                Inspect("click-" + trial + "-before");
                using var observation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
                var editor = Editor(observation.RootElement);
                Check("click target visible " + trial, ObservedText(editor)=="// click target");
                var p = Preview(); var origin = p.GetProperty("sourceClientOrigin"); var presentation = p.GetProperty("presentation");
                int x = (int)Math.Ceiling((editor.GetProperty("x").GetDouble() + 400 - origin.GetProperty("X").GetInt32()) * presentation.GetProperty("DrawWidth").GetInt32() / presentation.GetProperty("SourceWidth").GetInt32());
                int y = (int)Math.Ceiling((editor.GetProperty("y").GetDouble() + 8 - origin.GetProperty("Y").GetInt32()) * presentation.GetProperty("DrawHeight").GetInt32() / presentation.GetProperty("SourceHeight").GetInt32());
                Input("prepare-caret-away-from-click", () => qa.PostKey(canvas, 0x24));
                Input("editor-click", () => qa.SendMouseInput(canvas, x, y));
                Input("editor-enter", () => qa.PostKey(canvas, 0x0D));
                string marker = "// fast" + trial;
                Input("editor-type-after-enter", () => qa.PostKeyboardInput(canvas, marker), marker.Length);
                Inspect("click-" + trial + "-after");
                Check("click Enter typing exact text " + trial, Text().Replace("\r\n", "\n") == "// click target\n" + marker);
            }
            // Queue a realistic click / Enter / text burst without waiting for
            // per-command frame acknowledgements. Exact text catches reordering.
            for (int trial = 0; trial < 5; trial++)
            {
                Chord(0x11, 0x41); Input("prepare-pipeline", () => qa.PostKeyboardInput(canvas, "// click target"), 15);
                Inspect("pipeline-" + trial + "-before");
                using var observation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
                var editor = Editor(observation.RootElement);
                var p = Preview(); var origin = p.GetProperty("sourceClientOrigin"); var presentation = p.GetProperty("presentation");
                int x = (int)Math.Ceiling((editor.GetProperty("x").GetDouble() + 400 - origin.GetProperty("X").GetInt32()) * presentation.GetProperty("DrawWidth").GetInt32() / presentation.GetProperty("SourceWidth").GetInt32());
                int y = (int)Math.Ceiling((editor.GetProperty("y").GetDouble() + 8 - origin.GetProperty("Y").GetInt32()) * presentation.GetProperty("DrawHeight").GetInt32() / presentation.GetProperty("SourceHeight").GetInt32());
                string marker = "// immediate ordered typing " + trial;
                Input("prepare-caret-away-from-pipeline-click", () => qa.PostKey(canvas, 0x24));
                Input("click-enter-typing-without-frame-waits", () => { qa.SendMouseInput(canvas, x, y); qa.PostKey(canvas, 0x0D); qa.PostKeyboardInput(canvas, marker); }, 2 + marker.Length);
                Inspect("pipeline-" + trial + "-after"); Check("immediate click Enter typing preserves order " + trial, Text().Replace("\r\n", "\n") == "// click target\n" + marker);
                Input("backspace-then-immediate-typing", () => { qa.PostKey(canvas, 0x08); qa.PostKeyboardInput(canvas, "R"); }, 2);
                Inspect("pipeline-" + trial + "-backspace"); Check("immediate Backspace typing preserves order " + trial, Text().Replace("\r\n", "\n") == "// click target\n" + marker[..^1] + "R");
            }
            Chord(0x11, 0x41); Input("prepare-latency-comment",()=>qa.PostKeyboardInput(canvas,"// latency "),11);
            Inspect("02-initial-text");
            Check("initial ordinary typing", Text() == "// latency ");
            stage = "single character latency";
            var processes = qa.GetProcessIds().Select(id => { try { return Process.GetProcessById(id); } catch (ArgumentException) { return null; } }).Where(p => p != null).Cast<Process>().ToArray();
            double cpuBefore = processes.Sum(p => p.TotalProcessorTime.TotalMilliseconds); var active = Stopwatch.StartNew();
            string expected = "// latency ";
            foreach (char c in "abcdefgh") { Input("single-character", () => qa.PostKeyboardInput(canvas, c.ToString())); expected += c; Thread.Sleep(60); }
            stage = "paced typing burst";
            const string burst = "ijklmnopqrstuvwxyz012345";
            Input("paced-burst-40-chars-per-second", () => { foreach (char c in burst) { qa.PostKeyboardInput(canvas, c.ToString()); Thread.Sleep(25); } }, burst.Length); expected += burst;
            double activeMs = active.Elapsed.TotalMilliseconds, cpuMs = processes.Sum(p => { p.Refresh(); return p.TotalProcessorTime.TotalMilliseconds; }) - cpuBefore;
            activity = new { wallMilliseconds = activeMs, cpuMilliseconds = cpuMs, averageOneCorePercent = 100 * cpuMs / activeMs, peakWorkingSetBytes = processes.Sum(p => p.PeakWorkingSet64), processIds = processes.Select(p => p.Id).ToArray(), note = "Owned viewer, source and helpers; CPU includes app work and diagnostics. Not whole-machine utilization or a GPU measurement." };
            foreach (var process in processes) process.Dispose();
            Inspect("03-typed-burst"); Check("paced typing preserves every character in order", Text() == expected);
            stage = "Backspace";
            Input("backspace", () => qa.PostKey(canvas, 0x08)); Inspect("04-backspace"); bool deletion = Text() == expected[..^1]; checks["Backspace deletes exactly one character"] = deletion;
            Console.WriteLine((deletion ? "PASS " : "FAIL ") + "Backspace deletes exactly one character");
            if (requireDeletion && !deletion) throw new InvalidOperationException("Backspace failed.");
            if (deletion)
            {
                expected = expected[..^1];
                Input("repeated-backspace", () => { for (int i = 0; i < 8; i++) { qa.PostKey(canvas, 0x08); Thread.Sleep(25); } }, 8); expected = expected[..^8];
                Inspect("05-repeated-backspace"); Check("repeated Backspace has no duplicate or missing deletion", Text() == expected);
                Input("append-word", () => qa.PostKeyboardInput(canvas, " word"), 5);
                Input("ctrl-backspace", () => qa.SendQueueStateChord(canvas, [0x11, 0x08]));
                expected += " "; Inspect("06-word-deletion"); Check("Ctrl+Backspace deletes a word", Text() == expected);
                Chord(0x11, 0x41); Input("selection-backspace", () => qa.PostKey(canvas, 0x08)); Inspect("06a-selection-deletion"); Check("Backspace deletes the selected text exactly once", Text() == "");
                Input("restore-typed-text", () => qa.PostKeyboardInput(canvas, expected), expected.Length);
                Input("home", () => qa.PostKey(canvas, 0x24)); Input("delete", () => qa.PostKey(canvas, 0x2E)); expected = expected[1..];
                Inspect("06b-forward-delete"); Check("Delete still removes exactly one character", Text() == expected);
            }
            else expected = Text();
            stage = "save and handoff";
            Chord(0x11, 0x53); Wait(() => File.ReadAllText(setup.First) == expected, "Saved document differs from input."); Check("ordinary Ctrl+S saves exact edited text", true);
            Button("Return to automation"); Inspect("07-automation"); long sequence = Preview().GetProperty("inputSequence").GetInt64();
            qa.PostKeyboardInput(canvas, "BLOCKED"); qa.PostKey(canvas, 0x08); Inspect("08-rejected-input");
            Check("Automation rejects typing and Backspace", Text() == expected && Preview().GetProperty("inputSequence").GetInt64() == sequence);
            Button("Human control"); Inspect("09-handoff"); Check("handoff preserves the same IDE and text", Preview().GetProperty("applicationPid").GetInt64() == sourcePid && Text() == expected);
            // Leave both the bounded startup preview and shortcut watch behind.
            Thread.Sleep(16000);
            long idleVersion = Preview().GetProperty("previewVersion").GetInt64();
            object humanIdle;
            using (var viewerProcess = Process.GetProcessById(qa.ApplicationProcessId))
            {
                double beforeCpu = viewerProcess.TotalProcessorTime.TotalMilliseconds; var clock = Stopwatch.StartNew(); Thread.Sleep(2000); viewerProcess.Refresh();
                humanIdle = new { milliseconds = clock.Elapsed.TotalMilliseconds, viewerCpuMilliseconds = viewerProcess.TotalProcessorTime.TotalMilliseconds - beforeCpu, previewVersionBefore = idleVersion, previewVersionAfter = Preview().GetProperty("previewVersion").GetInt64() };
            }
            if (requireDeletion) Check("visible Human preview keeps asynchronous main-window output current", Preview().GetProperty("previewVersion").GetInt64() > idleVersion);
            Button("Return to automation"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Automation owns input", StringComparison.Ordinal) || w.Text.StartsWith("Automation | PID ", StringComparison.Ordinal))), "Automation idle ownership missing."); Thread.Sleep(3500);
            idleVersion = Preview().GetProperty("previewVersion").GetInt64();
            using (var viewerProcess = Process.GetProcessById(qa.ApplicationProcessId))
            {
                double beforeCpu = viewerProcess.TotalProcessorTime.TotalMilliseconds; var clock = Stopwatch.StartNew(); Thread.Sleep(2000); viewerProcess.Refresh();
                idle = new { humanIdle, automationIdle = new { milliseconds = clock.Elapsed.TotalMilliseconds, viewerCpuMilliseconds = viewerProcess.TotalProcessorTime.TotalMilliseconds - beforeCpu, previewVersionBefore = idleVersion, previewVersionAfter = Preview().GetProperty("previewVersion").GetInt64() } };
            }
            Check("Automation idle preview stops capturing after bounded watches", Preview().GetProperty("previewVersion").GetInt64() == idleVersion);
            stage = "complete";
        }
        catch (Exception e) { failure = e.ToString(); Console.Error.WriteLine(failure); }
        finally { if (!qa.Closed) { owned = qa.GetProcessIds(); try { qa.CloseGracefully(TimeSpan.FromSeconds(6)); } catch { } } qa.Dispose(); }
        var physical = monitor.Complete(owned); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); var after = VisualStudioResearch.PersonalSettings(allowUnavailable:isolated); bool settings = personal.Where(kv=>!kv.Value.StartsWith("unavailable:")).All(kv => after.TryGetValue(kv.Key, out var value) && value == kv.Value);
        bool passed = failure == null && cleanup && settings && physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output, "responsiveness.json"), JsonSerializer.Serialize(new { passed, stage, failure, checks, timings, activity, idle, cleanup, owned, physical, readablePersonalSettingsUnchanged = settings,unavailableSettings=personal.Where(kv=>kv.Value.StartsWith("unavailable:")).Select(kv=>kv.Key), viewerExecutable, viewerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(viewerExecutable, ".dll")))), requireDeletion,copyPreview,windowDcPreview,asyncStatusReports,preferTextHint,nativeSubtreeGeometry,preparePointer,preciseMouseWaits, note = "No foreground-session comparison. Frame-report timings include queueing, native capture and report publication; not physical input-to-display latency. Human acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

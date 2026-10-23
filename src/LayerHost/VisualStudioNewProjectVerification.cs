using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class VisualStudioNewProjectVerification
{
    internal static int Run(string output, string setupFile, string viewerExecutable)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        VisualStudioResearch.ValidateSetup(setup, setupFile);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")).TrimEnd(Path.DirectorySeparatorChar);
        viewerExecutable = Path.GetFullPath(viewerExecutable);
        if (!viewerExecutable.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(viewerExecutable) != "LayerHost.exe") throw new ArgumentException("Use a preserved project-local viewer.");
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        string stage = "startup"; string? failure = null; int[] owned = []; long viewer = 0, canvas = 0; var samples = new List<object>(); var checks = new Dictionary<string, bool>();
        var personal = VisualStudioResearch.PersonalSettings(); string original = File.ReadAllText(setup.First); object? loadingResources = null;
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Wait(Func<bool> condition, string reason, int timeout = 20000) { var watch = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(50); } while (watch.ElapsedMilliseconds < timeout); throw new TimeoutException(reason); }
        void Button(string name) => qa.SendMouseInput(qa.GetWindows().Single(w => w.Text == name).Handle, 10, 10);
        void Chord(params int[] keys) { long before = Preview().GetProperty("inputSequence").GetInt64(); qa.SendQueueStateChord(canvas, keys); Wait(() => Preview().GetProperty("inputSequence").GetInt64() > before, "Viewer input did not finish."); }
        JsonElement[] Nodes() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json"))); return doc.RootElement.GetProperty("nodes").EnumerateArray().Select(n => n.Clone()).ToArray(); }
        void Pixels(string name) { File.WriteAllBytes(Path.Combine(output, name + ".png"), qa.CaptureVisualOutput(viewer).Png); samples.Add(new { name, preview = Preview() }); }
        void Inspect(string name)
        {
            long before = Preview().GetProperty("previewVersion").GetInt64(); Button("Inspect details");
            Wait(() => { var p = Preview(); long current = p.GetProperty("previewVersion").GetInt64(); if (current <= before) return false; if (p.GetProperty("observationVersion").GetInt64() == current) return true; before = current; Button("Inspect details"); return false; }, "Inspection did not finish.");
            File.WriteAllText(Path.Combine(output, name + "-accessible.json"), SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json"))); Pixels(name);
        }
        void ClickNode(JsonElement node)
        {
            var p = Preview(); var origin = p.GetProperty("sourceClientOrigin"); var presentation = p.GetProperty("presentation");
            double x = node.GetProperty("x").GetDouble() + node.GetProperty("width").GetDouble()/2 - origin.GetProperty("X").GetInt32(), y = node.GetProperty("y").GetDouble() + node.GetProperty("height").GetDouble()/2 - origin.GetProperty("Y").GetInt32();
            long before = p.GetProperty("inputSequence").GetInt64(); qa.SendMouseInput(canvas, (int)Math.Ceiling(x * presentation.GetProperty("DrawWidth").GetInt32()/presentation.GetProperty("SourceWidth").GetInt32()), (int)Math.Ceiling(y * presentation.GetProperty("DrawHeight").GetInt32()/presentation.GetProperty("SourceHeight").GetInt32()));
            Wait(() => Preview().GetProperty("inputSequence").GetInt64() > before, "Pointer input did not finish.");
        }
        void Check(string name, bool value) { checks[name] = value; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) throw new InvalidOperationException(name); }
        try
        {
            qa.LaunchApplication(viewerExecutable, ["--run-vs-private-viewer", Path.GetFullPath(setupFile), output], output);
            Wait(() => File.Exists(Path.Combine(output, "viewer-preview.json")), "Viewer startup failed.", 90000);
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle; canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            Wait(() => Preview().GetProperty("activeText").GetString()!.Contains("Greeting.cs", StringComparison.Ordinal), "Initial code document missing.", 60000);
            Button("Human control"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Human owns input", StringComparison.Ordinal) || w.Text.StartsWith("Human | PID ", StringComparison.Ordinal))), "Human control missing.");
            Inspect("01-editor"); Thread.Sleep(2500); int sourcePid = Preview().GetProperty("applicationPid").GetInt32();
            stage = "open New Project"; Chord(0x11, 0x10, 0x4E);
            Wait(() => Preview().GetProperty("root").GetInt64() != Preview().GetProperty("mainRoot").GetInt64(), "New Project did not become the active surface."); Pixels("02-new-project-initial");
            long initialVersion = Preview().GetProperty("previewVersion").GetInt64();
            stage = "unassisted loading";
            using (var viewerProcess = Process.GetProcessById(qa.ApplicationProcessId))
            using (var sourceProcess = Process.GetProcessById(sourcePid))
            {
                double viewerBefore = viewerProcess.TotalProcessorTime.TotalMilliseconds, sourceBefore = sourceProcess.TotalProcessorTime.TotalMilliseconds; var loading = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++) { Thread.Sleep(4000); samples.Add(new { name = "unassisted-" + (i+1)*4 + "s", preview = Preview() }); }
                viewerProcess.Refresh(); sourceProcess.Refresh();
                loadingResources = new { milliseconds = loading.Elapsed.TotalMilliseconds, viewerCpuMilliseconds = viewerProcess.TotalProcessorTime.TotalMilliseconds - viewerBefore, sourceCpuMilliseconds = sourceProcess.TotalProcessorTime.TotalMilliseconds - sourceBefore, viewerWorkingSetBytes = viewerProcess.WorkingSet64, sourceWorkingSetBytes = sourceProcess.WorkingSet64, note = "Main viewer and IDE processes only; includes template loading, omits helper CPU and GPU. Capture cadence is not an application-ready acknowledgement." };
            }
            Pixels("03-after-20s-without-refresh");
            long automaticVersion = Preview().GetProperty("previewVersion").GetInt64();
            Inspect("04-forced-fresh-observation");
            var nodes = Nodes();
            File.WriteAllText(Path.Combine(output, "new-project-names.json"), JsonSerializer.Serialize(nodes.Select(n => new { type=n.GetProperty("type").GetString(), name=n.GetProperty("name").GetString(), id=n.GetProperty("id").GetString(), text=n.GetProperty("text").ValueKind==JsonValueKind.String?n.GetProperty("text").GetString():null })));
            Check("automatic frames continued while dialog content loaded", automaticVersion > initialVersion);
            var templates = nodes.Where(n => n.GetProperty("type").GetString() == "ControlType.ListItem").ToArray();
            Check("New Project templates loaded in the actual IDE", templates.Length > 0);
            stage = "dialog preview ownership";
            Button("Hide preview"); Wait(() => qa.GetWindows().Any(w => w.Text.Contains("Preview hidden; no captures", StringComparison.Ordinal)), "Preview did not hide."); long hiddenVersion = Preview().GetProperty("previewVersion").GetInt64(); Thread.Sleep(1200);
            Check("hidden preview stops dialog captures", Preview().GetProperty("previewVersion").GetInt64() == hiddenVersion);
            Button("Show preview"); Wait(() => Preview().GetProperty("previewVersion").GetInt64() > hiddenVersion, "Preview did not resume.");
            Button("Return to automation"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Automation owns input", StringComparison.Ordinal) || w.Text.StartsWith("Automation | PID ", StringComparison.Ordinal))), "Automation ownership missing."); long automationVersion = Preview().GetProperty("previewVersion").GetInt64(); Thread.Sleep(1200);
            Check("Automation ownership stops dialog heartbeat", Preview().GetProperty("previewVersion").GetInt64() == automationVersion);
            Button("Human control"); Wait(() => Preview().GetProperty("previewVersion").GetInt64() > automationVersion, "Human dialog heartbeat did not resume.");
            stage = "select template";
            var chosen = templates.FirstOrDefault(n => n.GetProperty("name").GetString() == "Blazor Web App" && !n.GetProperty("offscreen").GetBoolean());
            if (chosen.ValueKind == JsonValueKind.Undefined) chosen = templates.First(n => !n.GetProperty("offscreen").GetBoolean());
            string chosenName = chosen.GetProperty("name").GetString()!;
            ClickNode(chosen);
            Inspect("05-template-selected");
            var next = Nodes().First(n => n.GetProperty("type").GetString() == "ControlType.Button" && n.GetProperty("name").GetString() == "Next"); ClickNode(next);
            var configure = Stopwatch.StartNew();
            do { Inspect("06-configure-project"); if (Nodes().Any(n => (n.GetProperty("name").GetString() ?? "").Contains("Configure your new project", StringComparison.OrdinalIgnoreCase))) break; Thread.Sleep(250); } while (configure.ElapsedMilliseconds < 20000);
            Check("Next opens the configuration page", Nodes().Any(n => (n.GetProperty("name").GetString() ?? "").Contains("Configure your new project", StringComparison.OrdinalIgnoreCase)));
            Check("configuration uses the mouse-selected template", Nodes().Any(n => n.GetProperty("type").GetString() == "ControlType.Text" && n.GetProperty("name").GetString() == chosenName));
            stage = "cancel configuration";
            var cancel = Nodes().FirstOrDefault(n => n.GetProperty("type").GetString() == "ControlType.Button" && n.GetProperty("name").GetString() == "Cancel");
            if (cancel.ValueKind != JsonValueKind.Undefined) ClickNode(cancel); else qa.PostKey(canvas, 0x1B);
            Wait(() => Preview().GetProperty("root").GetInt64() == Preview().GetProperty("mainRoot").GetInt64(), "Cancellation did not return to the same editor."); Inspect("07-returned-editor");
            Check("Cancel preserves the same IDE and source document", Preview().GetProperty("applicationPid").GetInt32() == sourcePid && File.ReadAllText(setup.First) == original);
            stage = "complete";
        }
        catch (Exception e) { failure = e.ToString(); Console.Error.WriteLine(failure); }
        finally { if (!qa.Closed) { owned = qa.GetProcessIds(); try { qa.CloseGracefully(TimeSpan.FromSeconds(6)); } catch { } } qa.Dispose(); }
        var physical = monitor.Complete(owned); var after = VisualStudioResearch.PersonalSettings(); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), settings = personal.Count == after.Count && personal.All(kv => after.TryGetValue(kv.Key, out var value) && value == kv.Value);
        bool passed = failure == null && checks.Values.All(v => v) && cleanup && settings && physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"new-project.json"), JsonSerializer.Serialize(new { passed, stage, failure, checks, samples, loadingResources, cleanup, owned, personalUnchanged=settings, physical, viewerExecutable, assemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(viewerExecutable,".dll")))) },new JsonSerializerOptions { WriteIndented=true }));
        return passed?0:1;
    }
}

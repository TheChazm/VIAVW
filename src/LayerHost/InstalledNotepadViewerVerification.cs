using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class InstalledNotepadViewerVerification
{
    internal static int Run(string output, Barrier? pairing = null)
    {
        GpuBridgeVerification.PrepareOutput(output); var setup = InstalledNotepadResearch.Prepare(output, true);
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        bool passed = true, graceful = false; string? failure = null; var checks = new List<object>(); int[] owned = []; int sourcePid = 0; string? sourceDesktop = null;
        long viewer = 0, canvas = 0;
        void Wait(Func<bool> condition, string reason) { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(40); } while (clock.ElapsedMilliseconds < 10000); throw new TimeoutException(reason); }
        void Check(string name, bool value) { checks.Add(new { name, passed = value }); passed &= value; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) throw new InvalidOperationException(name); }
        bool Status(string text) => qa.GetWindows().Any(w => w.Text.Contains(text, StringComparison.Ordinal));
        void Button(string text) { var button = qa.GetWindows().Single(w => w.Text == text); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(100); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Forward(Action send, int count = 1)
        {
            long before = Preview().GetProperty("inputSequence").GetInt64(); send();
            Wait(() => Preview().GetProperty("inputSequence").GetInt64() >= before + count, "Viewer forwarding did not finish before the next workflow step.");
        }
        try
        {
            string ready = Path.Combine(output, "session.json"); var args = new List<string> { "--demo", "--full-content", "--queue-pointer", "--posted-shortcuts", "--ready-file", ready, "--qa-report-directory", output, setup.Executable };
            args.AddRange(setup.Arguments); qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
            Wait(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "Viewer startup did not complete.");
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle;
            canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            sourcePid = Preview().GetProperty("applicationPid").GetInt32(); sourceDesktop = Preview().GetProperty("desktopId").GetString();
            if (pairing != null && !pairing.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Two installed application viewers did not overlap.");
            Check("installed Notepad++ viewer and private source ready", sourcePid > 0 && sourceDesktop != qa.Id);
            Button("Human control"); Wait(() => Status("Human owns input"), "Human owner was not applied.");
            Forward(() => qa.SendMouseInput(canvas, 120, 60)); Forward(() => qa.SendQueueStateChord(canvas, [0x11, 0x41]));
            Forward(() => qa.PostKeyboardInput(canvas, "REAL_INSTALLED_VIEWER"), "REAL_INSTALLED_VIEWER".Length); Forward(() => qa.SendQueueStateChord(canvas, [0x11, 0x53]));
            Wait(() => File.ReadAllText(setup.Document) == "REAL_INSTALLED_VIEWER", "Viewer click/typing/save did not update the document.");
            Check("actual viewer pointer, select-all, typing and save reach installed editor", true);
            Forward(() => qa.PostKeyboardInput(canvas, "XXXX"), 4); Forward(() => qa.SendQueueStateChord(canvas, [0x11, 0x53]));
            Wait(() => File.ReadAllText(setup.Document) == "REAL_INSTALLED_VIEWERXXXX", "Typed deletion fixture was not saved before Backspace.");
            for (int i = 0; i < 4; i++) Forward(() => qa.PostKey(canvas, 0x08));
            Forward(() => qa.SendQueueStateChord(canvas, [0x11, 0x53]));
            Wait(() => File.ReadAllText(setup.Document) == "REAL_INSTALLED_VIEWER", "Backspace duplicated or missed deletion in the installed editor.");
            Check("viewer Backspace deletes each typed character exactly once", true);
            Button("Return to automation"); Wait(() => Status("Automation owns input"), "Automation owner was not applied.");
            qa.PostKeyboardInput(canvas, "REJECTED"); qa.SendQueueStateChord(canvas, [0x11, 0x53]); Thread.Sleep(180);
            Button("Human control"); qa.SendQueueStateChord(canvas, [0x11, 0x53]);
            Check("picture typing is rejected after automation takes ownership", File.ReadAllText(setup.Document) == "REAL_INSTALLED_VIEWER");
            Button("Return to automation"); Button("Automation: send text"); Thread.Sleep(160);
            Button("Human control"); qa.SendQueueStateChord(canvas, [0x11, 0x53]);
            Wait(() => File.ReadAllText(setup.Document) == "REAL_INSTALLED_VIEWERHello from virtual input", "Automation toolbar text did not reach retained editor.");
            Check("automation toolbar resumes same installed application", Preview().GetProperty("applicationPid").GetInt32() == sourcePid);
            Button("Refresh"); File.WriteAllBytes(Path.Combine(output, "installed-notepad-viewer.png"), qa.CaptureVisualOutput(viewer).Png);
            Button("Hide preview"); Wait(() => Status("Preview hidden; no captures"), "Preview did not hide."); var recorded = Preview();
            qa.PostKeyboardInput(canvas, "_NO_PREVIEW"); Thread.Sleep(120); qa.SendQueueStateChord(canvas, [0x11, 0x53]);
            Wait(() => File.ReadAllText(setup.Document).EndsWith("_NO_PREVIEW", StringComparison.Ordinal), "Input with preview hidden did not save.");
            Check("hidden preview preserves document and input session", Preview().GetProperty("previewVersion").GetInt64() == recorded.GetProperty("previewVersion").GetInt64());
        }
        catch (Exception error) { passed = false; failure = error.ToString(); Console.Error.WriteLine(failure); }
        finally
        {
            if (!qa.Closed) { owned = qa.GetProcessIds(); try { graceful = qa.CloseGracefully(TimeSpan.FromSeconds(4)); } catch { } }
            qa.Dispose(); Thread.Sleep(200);
        }
        var physical = monitor.Complete(owned); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)); passed &= cleanup && physical.GetProperty("passed").GetBoolean() && (failure != null || graceful);
        File.WriteAllText(Path.Combine(output, "installed-notepad-viewer.json"), JsonSerializer.Serialize(new { passed, failure, checks, sourcePid, sourceDesktop, graceful, cleanup, physical, owned, physicalHumanAcceptance = "pending", rendering = "built-in GDI setting", plugins = "disabled", instrumentation = "none" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, cleanup, graceful, output })); return passed ? 0 : 1;
    }

    internal static int Concurrent(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); using var pairing = new Barrier(2);
        var runs = new[] { "one", "two" }.Select(name => Task.Run(() => Run(Path.Combine(output, name), pairing))).ToArray(); Task.WaitAll(runs);
        var reports = new[] { "one", "two" }.Select(name => { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, name, "installed-notepad-viewer.json"))); return doc.RootElement.Clone(); }).ToArray();
        bool independent = reports.Select(r => r.GetProperty("sourcePid").GetInt32()).Distinct().Count() == 2 && reports.Select(r => r.GetProperty("sourceDesktop").GetString()).Distinct().Count() == 2;
        bool passed = runs.All(t => t.Result == 0) && independent;
        File.WriteAllText(Path.Combine(output, "concurrent.json"), JsonSerializer.Serialize(new { passed, independent, simultaneousStartupBarrier = true, reports }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

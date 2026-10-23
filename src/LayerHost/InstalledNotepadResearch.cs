using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

// Exercises the installed application through existing layer input/capture APIs.
// No injected code, Scintilla messages, plugin, clipboard writes or document model API.
internal static class InstalledNotepadResearch
{
    internal const string Executable = @"C:\Program Files\Notepad++\notepad++.exe";
    internal record Setup(string Executable, string[] Arguments, string Document, string Settings);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static Setup Prepare(string output, bool gdi = false)
    {
        if (!File.Exists(Executable)) throw new FileNotFoundException("Installed Notepad++ was not found.", Executable);
        string settings = Path.Combine(output, "settings"); Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "config.xml"), """
            <?xml version="1.0" encoding="UTF-8" ?>
            <NotepadPlus><History nbMaxFile="0" /><GUIConfigs>
              <GUIConfig name="noUpdate">yes</GUIConfig>
              <GUIConfig name="RememberLastSession">no</GUIConfig>
              <GUIConfig name="Backup" action="0" useCustumDir="no" dir="" isSnapshotMode="no" snapshotBackupTiming="7000" />
              <GUIConfig name="AppPosition" x="30" y="30" width="900" height="650" isMaximized="no" />
            </GUIConfigs></NotepadPlus>
            """, new UTF8Encoding(false));
        if (gdi)
        {
            string config = Path.Combine(settings, "config.xml");
            File.WriteAllText(config, File.ReadAllText(config).Replace("</GUIConfigs>", "<GUIConfig name=\"MISC\" writeTechnologyEngine=\"0\" /></GUIConfigs>"), new UTF8Encoding(false));
        }
        string document = Path.Combine(output, "disposable.txt");
        File.WriteAllText(document, "Original disposable document", new UTF8Encoding(true));
        return new(Executable, ["-multiInst", "-nosession", "-noPlugin", "-settingsDir=" + settings, document], document, settings);
    }

    internal static int PrepareDemo(string output, bool gdi = false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        File.WriteAllText(Path.Combine(output, "launch.json"), JsonSerializer.Serialize(Prepare(output, gdi), Json)); return 0;
    }

    internal static string PersonalSettingsDigest()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notepad++");
        var entries = new List<string>();
        foreach (string name in new[] { "config.xml", "session.xml", "shortcuts.xml", "contextMenu.xml" })
        {
            string file = Path.Combine(folder, name);
            entries.Add(name + ":" + (File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : "absent"));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }

    internal static int Run(string output, bool gdi = false)
    {
        GpuBridgeVerification.PrepareOutput(output); var setup = Prepare(output, gdi);
        var checks = new List<object>(); var captures = new List<object>(); bool passed = true, graceful = false; string? failure = null;
        string personalBefore = PersonalSettingsDigest(), binaryBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Executable)));
        var existing = Process.GetProcessesByName("notepad++").Select(p => { using (p) return (Pid: p.Id, Created: p.StartTime.ToUniversalTime()); }).ToArray();
        using var monitor = new ResearchDesktopMonitor(); using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var ownedIdentities = new Dictionary<int, DateTime>();
        int pid = 0; long root = 0, editor = 0; int[] owned = []; object? performance = null; Process? launched = null;
        void Check(string name, bool ok, object? detail = null)
        {
            passed &= ok; checks.Add(new { name, passed = ok, detail }); Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) throw new InvalidOperationException(name);
        }
        bool Wait(Func<bool> condition, int ms = 4000)
        {
            var watch = Stopwatch.StartNew(); do { if (condition()) return true; Thread.Sleep(40); } while (watch.ElapsedMilliseconds < ms); return condition();
        }
        void Chord(int key, ControlOwner sender = ControlOwner.Automation) => layer.SendQueueStateChord(editor, [0x11, key], posted: true, sender: sender);
        void Save(string expected, string phase, ControlOwner sender = ControlOwner.Automation)
        {
            var clock = Stopwatch.StartNew(); Chord(0x53, sender);
            Check(phase, Wait(() => File.ReadAllText(setup.Document) == expected), new { elapsedMs = clock.Elapsed.TotalMilliseconds, expected, actual = File.ReadAllText(setup.Document) });
        }
        void Capture(string phase)
        {
            var frame = layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent);
            File.WriteAllBytes(Path.Combine(output, phase + ".png"), frame.Png);
            using var stream = new MemoryStream(frame.Png); using var bitmap = new Bitmap(stream);
            int ink = 0, samples = 0; for (int y = 0; y < bitmap.Height; y += 7) for (int x = 0; x < bitmap.Width; x += 7)
            { var p = bitmap.GetPixel(x, y); if (p.R + p.G + p.B > 60 && p.R + p.G + p.B < 650) ink++; samples++; }
            captures.Add(new { phase, frame.Width, frame.Height, frame.CaptureMilliseconds, frame.Backend, ink, samples });
            Check(phase + " native image has content", frame.Width > 300 && frame.Height > 200 && ink > 30, new { ink, samples });
        }
        try
        {
            var startup = Stopwatch.StartNew(); pid = layer.LaunchApplication(setup.Executable, setup.Arguments, output); launched = Process.GetProcessById(pid);
            Check("installed application creates an owned private window", Wait(() =>
            {
                var windows = layer.GetWindows(); var main = windows.FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "Notepad++" && w.ProcessId == pid);
                if (main == null) return false; root = main.Handle;
                editor = windows.FirstOrDefault(w => w.ClassName == "Scintilla" && w.Visible && LayerWindowSelection.InSubtree(windows, w.Handle, root))?.Handle ?? 0;
                return editor != 0;
            }, 10000), new { pid, desktop = layer.Id });
            startup.Stop(); File.WriteAllText(Path.Combine(output, "initial-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json));
            Check("launch uses new process and disposable file", existing.All(p => p.Pid != pid) && layer.GetWindows().Any(w => w.Handle == root && w.Text.Contains("disposable.txt")));
            Capture("01-initial");
            var processSamples = layer.GetProcessIds().Select(id => { using var p = Process.GetProcessById(id); ownedIdentities[id] = p.StartTime.ToUniversalTime(); return (Id: id, Created: p.StartTime.ToUniversalTime(), Cpu: p.TotalProcessorTime); }).ToArray();
            var activity = Stopwatch.StartNew();
            Chord(0x41); layer.SendKeyboardInput(editor, "VIAVW installed Notepad++: café Ω"); Save("VIAVW installed Notepad++: café Ω", "native select-all, Unicode typing and Ctrl+S save exact text");
            Capture("02-unicode-saved");
            layer.SendKeyboardInput(editor, " APPEND"); Save("VIAVW installed Notepad++: café Ω APPEND", "append saves through ordinary application");
            Chord(0x5A); Save("VIAVW installed Notepad++: café Ω", "Ctrl+Z undo changes saved document");
            Chord(0x59); Save("VIAVW installed Notepad++: café Ω APPEND", "Ctrl+Y redo changes saved document");
            layer.RequestAttention("Review installed Notepad++"); layer.TakeHumanControl(); bool automationRejected = false;
            try { layer.SendKeyboardInput(editor, "REJECTED"); } catch (InvalidOperationException) { automationRejected = true; }
            Check("automation rejected under Human ownership", automationRejected);
            layer.SendKeyboardInput(editor, " HUMAN", ControlOwner.Human); Save("VIAVW installed Notepad++: café Ω APPEND HUMAN", "Human sender edits same running document", ControlOwner.Human);
            layer.ReturnToAutomation(); bool humanRejected = false;
            try { layer.SendKeyboardInput(editor, "REJECTED", ControlOwner.Human); } catch (InvalidOperationException) { humanRejected = true; }
            Check("Human sender rejected after automation resumes", humanRejected);
            Chord(0x4F);
            long picker = 0; Check("Ctrl+O opens ordinary file picker", Wait(() => { picker = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770" && w.Handle != root)?.Handle ?? 0; return picker != 0; }));
            File.WriteAllText(Path.Combine(output, "picker-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json));
            File.WriteAllBytes(Path.Combine(output, "03-open-dialog.png"), layer.CaptureVisualOutput(picker, CaptureBackend.PrintWindowFullContent).Png);
            layer.PostKey(picker, 0x1B); Check("Escape closes file picker", Wait(() => !layer.GetWindows().Any(w => w.Handle == picker && w.Visible)));
            int oldWorker = layer.WorkerProcessId; layer.RestartWorker();
            Check("worker recovery preserves installed application and root", layer.WorkerProcessId != oldWorker && layer.ApplicationProcessId == pid && layer.GetWindows().Any(w => w.Handle == root));
            layer.SendKeyboardInput(editor, " RECOVERED"); Save("VIAVW installed Notepad++: café Ω APPEND HUMAN RECOVERED", "recovered worker edits original document without replay");
            Capture("04-recovered");
            // A toolbar is not evidence that the document pixels were captured.
            // Inspect the first-line document area independently of chrome/gutter.
            var pixelTrials = new List<object>(); bool visibleDocument = false;
            foreach (string mode in new[] { "pw1", "pw3", "redraw-pw3", "print-client" })
            {
                var frame = layer.CaptureResearch(root, mode); int ink = DocumentInk(frame, layer.ClientOrigin(root), layer.ClientOrigin(editor));
                File.WriteAllBytes(Path.Combine(output, "document-" + mode + ".png"), frame.Png);
                pixelTrials.Add(new { mode, ink, frame.CaptureMilliseconds, documentVisible = ink > 100 });
                visibleDocument |= ink > 100;
            }
            File.WriteAllText(Path.Combine(output, "document-pixel-trials.json"), JsonSerializer.Serialize(new { visibleDocument, trials = pixelTrials }, Json));
            if (gdi) Check("GDI setting captures document text, beyond toolbar pixels", visibleDocument);
            else Console.WriteLine("OBSERVED default document pixels visible: " + visibleDocument);
            activity.Stop();
            var finalSamples = processSamples.Select(s => { try { using var p = Process.GetProcessById(s.Id); return new { pid = s.Id, sameIdentity = p.StartTime.ToUniversalTime() == s.Created, cpuMs = (p.TotalProcessorTime - s.Cpu).TotalMilliseconds, privateMiB = p.PrivateMemorySize64 / 1048576d, workingMiB = p.WorkingSet64 / 1048576d }; } catch (ArgumentException) { return null; } }).Where(s => s != null).ToArray();
            performance = new { startupMs = startup.Elapsed.TotalMilliseconds, activeMs = activity.Elapsed.TotalMilliseconds, processes = finalSamples, machineCpuPercent = finalSamples.Where(s => s!.sameIdentity).Sum(s => s!.cpuMs) / activity.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, note = "Active workflow, source and surviving original worker only; replaced worker and supervisor costs excluded. Not an idle benchmark." };
        }
        catch (Exception error)
        {
            passed = false; failure = error.ToString(); Console.Error.WriteLine(failure);
            if (!layer.Closed) try { File.WriteAllText(Path.Combine(output, "failure-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), Json)); } catch (Exception observation) { File.WriteAllText(Path.Combine(output, "failure-observation.txt"), observation.ToString()); }
            File.WriteAllText(Path.Combine(output, "failure-process.json"), JsonSerializer.Serialize(new { pid, exited = launched?.HasExited, job = layer.Closed ? [] : layer.GetProcessIds() }, Json));
        }
        finally
        {
            if (!layer.Closed)
            {
                owned = layer.GetProcessIds(); foreach (int id in owned) try { using var p = Process.GetProcessById(id); ownedIdentities[id] = p.StartTime.ToUniversalTime(); } catch (ArgumentException) { }
                try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch (Exception error) { failure ??= error.ToString(); }
            }
            layer.Dispose(); Thread.Sleep(150);
            launched?.Dispose();
        }
        var physical = monitor.Complete(ownedIdentities.Keys.ToArray());
        bool SameIdentityAlive(int id, DateTime created) { try { using var p = Process.GetProcessById(id); return !p.HasExited && p.StartTime.ToUniversalTime() == created; } catch (ArgumentException) { return false; } }
        void FinalCheck(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        FinalCheck("physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical);
        FinalCheck("owned process identities cleaned up", ownedIdentities.All(p => !SameIdentityAlive(p.Key, p.Value)), new { graceful, owned });
        FinalCheck("existing Notepad++ process identities preserved", existing.All(p => SameIdentityAlive(p.Pid, p.Created)));
        FinalCheck("personal settings and installed executable unchanged", personalBefore == PersonalSettingsDigest() && binaryBefore == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Executable)))) ;
        if (failure == null) FinalCheck("successful workflow exits gracefully", graceful);
        File.WriteAllText(Path.Combine(output, "installed-notepad.json"), JsonSerializer.Serialize(new { passed, failure, executable = Executable, version = FileVersionInfo.GetVersionInfo(Executable).FileVersion, sha256 = binaryBefore, setup, pid, root, editor, checks, captures, performance, physical, graceful, owned, physicalHumanAcceptance = "pending", plugins = "disabled", instrumentation = "none", rendering = gdi ? "built-in GDI setting" : "default DirectWrite setting", note = "passed covers functional assertions; document-pixel-trials.json separately records visual compatibility" }, Json));
        return passed ? 0 : 1;
    }

    static int DocumentInk(CaptureFrame frame, (int X, int Y) rootOrigin, (int X, int Y) editorOrigin)
    {
        using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream); int ink = 0;
        int left = editorOrigin.X - rootOrigin.X, top = editorOrigin.Y - rootOrigin.Y;
        for (int y = Math.Max(0, top + 2); y < Math.Min(image.Height, top + 42); y++)
            for (int x = Math.Max(0, left + 80); x < Math.Min(image.Width, left + 700); x++)
            { var p = image.GetPixel(x, y); if (p.R < 100 && p.G < 100 && p.B < 100) ink++; }
        return ink;
    }
}

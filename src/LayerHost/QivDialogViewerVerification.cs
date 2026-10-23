using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace LayerHost;

internal static class QivDialogViewerVerification
{
    internal static int Run(string output, bool nativeStartup = false, Barrier? pairing = null)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var checks = new List<object>(); var frames = new List<object>(); var costs = new List<object>(); var pings = new List<double>();
        bool passed = true, graceful = false; string? failure = null; int[] owned = []; int sourcePid = 0; long viewer = 0, canvas = 0;
        string registry = QivResearch.RegistryState();
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Wait(Func<bool> condition, string reason)
        { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(30); } while (clock.ElapsedMilliseconds < 10000); throw new TimeoutException(reason); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        bool Modal() => Preview().GetProperty("ownedModal").GetBoolean();
        bool Status(string value) => qa.GetWindows().Any(w => w.Text.Contains(value, StringComparison.Ordinal));
        void Button(string name) { var button = qa.GetWindows().Single(w => w.Text == name); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(60); }
        void Inspect(string name, bool modal, bool second = false)
        {
            var clock = Stopwatch.StartNew(); bool verified = false; JsonElement info = default; int attempts = 0;
            do
            {
                long version = Preview().GetProperty("previewVersion").GetInt64(); Button("Inspect hidden app"); Wait(() => Preview().GetProperty("previewVersion").GetInt64() > version, "Viewer inspection did not publish.");
                info = Preview(); attempts++;
                verified = info.GetProperty("ownedModal").GetBoolean() == modal;
                if (!modal) verified &= QivResearch.Pixels(Path.Combine(output, "viewer-preview.png")).GetProperty(second ? "magenta" : "red").GetInt32() > 1000;
                if (!verified) Thread.Sleep(30);
            } while (!verified && clock.ElapsedMilliseconds < 3500);
            Check(name, verified && info.GetProperty("applicationPid").GetInt32() == sourcePid, new { info, attempts });
            File.WriteAllBytes(Path.Combine(output, name + ".png"), SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png"))); frames.Add(new { name, info, attempts });
        }
        void ClickPicture(int x, int y)
        { qa.SendQueueMouseSequence(canvas, [new(MouseMessage.Move, x, y), new(MouseMessage.LeftDown, x, y, 1), new(MouseMessage.LeftUp, x, y)], true); Thread.Sleep(120); }
        try
        {
            string ready = Path.Combine(output, "session.json"); var args = new List<string> { "--demo", "--ready-file", ready, "--qa-report-directory", output, "--experimental-gpu-bridge", "qiv", "--root-input", "--queue-pointer" };
            if (nativeStartup) args.AddRange(["--qiv-native-startup", "--inspection-settle", "30"]); args.Add(QivResearch.Executable);
            qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
            Wait(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "Dialog viewer startup failed.");
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle;
            canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            sourcePid = Preview().GetProperty("applicationPid").GetInt32(); long main = Preview().GetProperty("mainRoot").GetInt64();
            Inspect("initial-main", false);
            bool firstPhase = true;
            foreach (bool native in new[] { nativeStartup, !nativeStartup })
            {
                string prefix = native ? "native" : "gpu";
                Button(native ? "Use native inspection" : "Use hidden GPU capture"); Inspect(prefix + "-ready", false);
                var before = Preview(); int? reader = before.GetProperty("helperPid").ValueKind == JsonValueKind.Number ? before.GetProperty("helperPid").GetInt32() : null;
                string? mapping = before.GetProperty("gpuMapping").ValueKind == JsonValueKind.String ? before.GetProperty("gpuMapping").GetString() : null;
                var cpuBefore = qa.GetProcessIds().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; }); var activity = Stopwatch.StartNew();
                Button("Automation: open file dialog"); Wait(Modal, "Viewer did not select owned picker."); Inspect(prefix + "-picker", true);
                if (pairing != null && firstPhase) Check("independent pickers simultaneously open", pairing.SignalAndWait(TimeSpan.FromSeconds(10)));
                firstPhase = false;
                Check(prefix + " modal keeps main binding and reader", Preview().GetProperty("mainRoot").GetInt64() == main && Preview().GetProperty("root").GetInt64() != main && Preview().GetProperty("Backend").GetString()!.StartsWith("Owned modal dialog") && Preview().GetProperty("helperPid").ToString() == before.GetProperty("helperPid").ToString());
                Button("Use hidden GPU capture"); Wait(() => Status("Close the owned dialog"), "Backend change was not refused during modal input.");
                Check(prefix + " backend changes refused while modal", true);
                Button("Recover input worker"); Inspect(prefix + "-picker-worker-recovered", true);
                using (var sharedMapping = mapping != null ? MemoryMappedFile.OpenExisting(mapping, MemoryMappedFileRights.Read) : null)
                using (var shared = sharedMapping?.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read))
                {
                    long modalVersion = Preview().GetProperty("previewVersion").GetInt64(); int? copies = shared?.ReadInt32(72); Thread.Sleep(320);
                    long settledVersion = Preview().GetProperty("previewVersion").GetInt64(); Thread.Sleep(320);
                    long finalVersion = Preview().GetProperty("previewVersion").GetInt64(); int? finalCopies = shared?.ReadInt32(72);
                    Check(prefix + " modal observation does not continuously capture", settledVersion - modalVersion <= 1 && finalVersion == settledVersion && finalCopies == copies, new { modalVersion, settledVersion, finalVersion, copies, finalCopies, note = "At most one already-queued explicit inspection may finish in the first interval; the next interval must remain still." });
                }
                Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership missing.");
                Button("Automation: cancel dialog"); Wait(() => Status("Input rejected: Human"), "Non-owner cancellation was not refused."); Check(prefix + " human ownership blocks automation cancellation", Modal());
                int height = Preview().GetProperty("Height").GetInt32(), width = Preview().GetProperty("Width").GetInt32();
                Check(prefix + " picker fits interactive canvas", qa.GetWindows().Single(w => w.Handle == canvas).Height >= height && qa.GetWindows().Single(w => w.Handle == canvas).Width >= width, new { width, height });
                ClickPicture(width - 62, height - 30); Wait(() => !Modal(), "Human pointer cancellation did not restore main window."); Inspect(prefix + "-pointer-cancelled", false);
                qa.PostKey(canvas, 0x71); Wait(Modal, "Human F2 did not open picker."); Inspect(prefix + "-human-picker", true);
                width = Preview().GetProperty("Width").GetInt32(); height = Preview().GetProperty("Height").GetInt32();
                ClickPicture(300, height - 60);
                string chosen = Path.Combine(output, "images", "02-alternate.png"); qa.SendKeyboardInput(canvas, chosen); Thread.Sleep(150);
                Inspect(prefix + "-typed-picker", true);
                qa.PostKey(canvas, 0x0D); Wait(() => !Modal(), "Human filename Enter did not close picker."); Inspect(prefix + "-opened-image", false, true);
                Check(prefix + " same process owner and capture resources after picker", Preview().GetProperty("owner").GetString() == "Human" && Preview().GetProperty("mainRoot").GetInt64() == main && !Preview().GetProperty("sourceWindowVisible").GetBoolean() && (reader == null ? Preview().GetProperty("helperPid").ValueKind == JsonValueKind.Null : Preview().GetProperty("helperPid").GetInt32() == reader && Preview().GetProperty("gpuMapping").GetString() == mapping));
                for (int i = 0; i < 6; i++) { var ping = Stopwatch.StartNew(); qa.ResearchProbeWindowResponse(viewer); pings.Add(ping.Elapsed.TotalMilliseconds); }
                Button("Return to automation"); Wait(() => Status("Automation owns input"), "Automation ownership missing.");
                Button("Hide preview"); long version = Preview().GetProperty("previewVersion").GetInt64(); Button("Automation: open file dialog"); Thread.Sleep(450);
                Check(prefix + " hidden preview opens dialog without capturing", Preview().GetProperty("previewVersion").GetInt64() == version);
                Inspect(prefix + "-delayed-picker", true); Button("Automation: cancel dialog"); Wait(() => !Modal(), "Automation could not cancel delayed picker.");
                Button("Automation: next image"); Inspect(prefix + "-reset-first", false);
                double ms = activity.Elapsed.TotalMilliseconds;
                var processes = qa.GetProcessIds().Where(cpuBefore.ContainsKey).Select(id => { using var process = Process.GetProcessById(id); double cpuMs = process.TotalProcessorTime.TotalMilliseconds - cpuBefore[id]; return new { pid = id, name = process.ProcessName, cpuMs, machinePercent = cpuMs / ms * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 }; }).ToArray();
                costs.Add(new { backend = prefix, ms, processes, note = "Active dialog workflow and pacing; surviving child identities present before/after only. Replaced worker and supervisor CPU are excluded. Not a backend ranking." });
            }
            Check("viewer responds to targeted pings", pings.Count == 12 && pings.Max() < 750, new { maximumMs = pings.Max(), medianMs = pings.Order().ElementAt(pings.Count / 2) });
            File.WriteAllBytes(Path.Combine(output, "actual-viewer.png"), qa.CaptureVisualOutput(viewer).Png);
            owned = qa.GetProcessIds(); graceful = qa.CloseGracefully(TimeSpan.FromSeconds(5)); Check("viewer closes owned source and reader", graceful && owned.All(id => !CompatibilityVerification.Alive(id)));
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); if(!qa.Closed) { try { File.WriteAllText(Path.Combine(output,"failed-viewer-windows.json"),JsonSerializer.Serialize(qa.GetWindows(),new JsonSerializerOptions{WriteIndented=true})); } catch { } } }
        finally { if (!qa.Closed) { owned = qa.GetProcessIds(); qa.Dispose(); } Thread.Sleep(200); }
        var physical = monitor.Complete(owned); Check("sampled physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical);
        Check("owned processes and registry cleaned", owned.All(id => !CompatibilityVerification.Alive(id)) && registry == QivResearch.RegistryState());
        File.WriteAllText(Path.Combine(output, "dialog-viewer.json"), JsonSerializer.Serialize(new { passed, failure, nativeStartup, checks, frames, costs, pings, sourcePid, owned, graceful, note = "Actual viewer UI on an outer private QA desktop. Human senders are simulated; physical human acceptance remains pending. Modal pixels use native capture; main GPU source stays bound." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
    internal static int Concurrent(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); using var pairing = new Barrier(2);
        int[] codes = Task.WhenAll(Task.Run(() => Run(Path.Combine(output, "gpu-first"), false, pairing)), Task.Run(() => Run(Path.Combine(output, "native-first"), true, pairing))).GetAwaiter().GetResult();
        using var a = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "gpu-first", "dialog-viewer.json")));
        using var b = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native-first", "dialog-viewer.json")));
        string Desktop(JsonElement value) => value.GetProperty("frames")[0].GetProperty("info").GetProperty("desktopId").GetString()!;
        bool independent = a.RootElement.GetProperty("sourcePid").GetInt32() != b.RootElement.GetProperty("sourcePid").GetInt32() && Desktop(a.RootElement) != Desktop(b.RootElement);
        bool passed = codes.All(c => c == 0) && independent;
        File.WriteAllText(Path.Combine(output, "concurrent-dialogs.json"), JsonSerializer.Serialize(new { passed, codes, independent, aPid = a.RootElement.GetProperty("sourcePid"), bPid = b.RootElement.GetProperty("sourcePid"), aDesktop = Desktop(a.RootElement), bDesktop = Desktop(b.RootElement) }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

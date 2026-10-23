using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class GpuBridgeViewerVerification
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SuspendThread(nint thread);
    [DllImport("kernel32.dll")] static extern uint GetProcessIdOfThread(nint thread);

    internal static int Run(string output, bool offset = false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string video = Path.Combine(output, "disposable.y4m"); MpvGpuResearch.MakeVideo(video);
        nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputTick = Native.LastInputTick(); var clipBefore = QivResearch.CursorClip();
        using var manager = new LayerManager(); using var outer = manager.CreateLayer();
        MemoryMappedFile? mapping = null; MemoryMappedViewAccessor? shared = null;
        var checks = new List<object>(); var frames = new List<object>(); var activity = new List<object>(); var pings = new List<double>();
        bool passed = true, graceful = false; string? failure = null, mappingName = null; int[] owned = []; int sourcePid = 0; long viewerRoot = 0, canvas = 0;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Until(Func<bool> condition, string reason, int timeout = 8000)
        {
            var watch = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(40); } while (watch.ElapsedMilliseconds < timeout);
            throw new TimeoutException(reason + ": " + string.Join(" | ", outer.GetWindows().Where(window => window.ClassName.Contains("STATIC", StringComparison.OrdinalIgnoreCase)).Select(window => window.Text)));
        }
        void Button(string name) { var button = outer.GetWindows().Single(window => window.Text == name); outer.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(80); }
        bool Status(string text) => outer.GetWindows().Any(window => window.Text.Contains(text, StringComparison.Ordinal));
        JsonElement NextPreview(Action action)
        {
            long version = Preview().GetProperty("previewVersion").GetInt64(); action(); Until(() => Preview().GetProperty("previewVersion").GetInt64() > version, "Viewer did not publish a new frame"); return Preview();
        }
        int MediaCounter()
        {
            using var image = SnapshotFiles.ReadBitmap(Path.Combine(output, "viewer-preview.png")); int counter = 0;
            for (int bit = 0; bit < 16; bit++) { var cell = image.GetPixel((14 + bit * 10) * image.Width / 320, 164 * image.Height / 180); if (cell.R > 215 && cell.G > 215 && cell.B > 215) counter |= 1 << bit; }
            return counter;
        }
        int Pixels(string name, int expected = 0, int width = 0, int height = 0)
        {
            var watch = Stopwatch.StartNew(); int counter = 0; bool cells = false, color = false; JsonElement info;
            do
            {
                info = NextPreview(() => Button("Inspect hidden app")); counter = 0; cells = true;
                using (var image = SnapshotFiles.ReadBitmap(Path.Combine(output, "viewer-preview.png")))
                {
                    for (int bit = 0; bit < 16; bit++)
                    {
                        var cell = image.GetPixel((14 + bit * 10) * image.Width / 320, 164 * image.Height / 180);
                        bool high = cell.R > 215 && cell.G > 215 && cell.B > 215, low = cell.R < 35 && cell.G < 35 && cell.B < 35;
                        cells &= high || low; if (high) counter |= 1 << bit;
                    }
                    var marker = image.GetPixel(image.Width / 2, image.Height / 2);
                    color = counter % 2 == 0 ? marker.R > 180 && marker.G > 80 && marker.G < 200 && marker.B < 120 : marker.G > 120 && marker.R < 110 && marker.B < 150;
                }
                if (expected == 0 || counter == expected) break; Thread.Sleep(220);
            } while (watch.ElapsedMilliseconds < 4000);
            bool size = width == 0 || (info.GetProperty("Width").GetInt32() == width && info.GetProperty("Height").GetInt32() == height);
            Check(name, cells && color && counter is >= 1 and <= 40 && (expected == 0 || counter == expected) && size && !info.GetProperty("sourceWindowVisible").GetBoolean() && info.GetProperty("applicationPid").GetInt32() == sourcePid, new { counter, expected, size });
            File.WriteAllBytes(Path.Combine(output, name + ".png"), SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png")));
            frames.Add(new { name, counter, info, verificationMs = watch.Elapsed.TotalMilliseconds }); return counter;
        }
        Dictionary<int, double> Cpu() => outer.GetProcessIds().ToDictionary(id => id, id => { using var process = Process.GetProcessById(id); return process.TotalProcessorTime.TotalMilliseconds; });
        object Cost(Dictionary<int, double> before, double ms) => before.Select(pair =>
        {
            using var process = Process.GetProcessById(pair.Key); double cpu = process.TotalProcessorTime.TotalMilliseconds - pair.Value;
            return new { pid = pair.Key, name = process.ProcessName, cpuMs = cpu, machinePercent = cpu / ms * 100 / Environment.ProcessorCount, privateMiB = process.PrivateMemorySize64 / 1048576.0 };
        }).ToArray();
        try
        {
            string ready = Path.Combine(output, "session.json");
            var arguments = new List<string> { "--demo", "--ready-file", ready, "--qa-report-directory", output, "--experimental-gpu-bridge", "mpv", "--root-input", "--queue-pointer", Path.Combine(project, ".deps/mpv-7c4f662c0/mpv.exe") };
            arguments.AddRange(MpvGpuResearch.Arguments(output, video, "VIAVW GPU viewer QA"));
            int viewerPid = outer.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), arguments.ToArray(), output);
            Until(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "GPU viewer failed startup", 20000);
            var windows = outer.GetWindows(); viewerRoot = windows.Single(window => window.Text == "VIAVW — private application layer").Handle;
            canvas = windows.Single(window => window.Parent == viewerRoot && window.Width > 900 && window.Height > 200).Handle;
            var initial = Preview(); sourcePid = initial.GetProperty("applicationPid").GetInt32(); mappingName = initial.GetProperty("gpuMapping").GetString()!;
            mapping = MemoryMappedFile.OpenExisting(mappingName, MemoryMappedFileRights.Read); shared = mapping.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read);
            Check("visible viewer and hidden ordinary GPU source", windows.Any(window => window.Handle == viewerRoot && window.Visible) && initial.GetProperty("Backend").GetString()!.StartsWith("Experimental GPU bridge") && !initial.GetProperty("sourceWindowVisible").GetBoolean() && shared.ReadInt32(380) == 0, new { viewerPid, sourcePid });
            Pixels("startup", 1); int shift = offset ? 1 : 0;
            if (offset) { Button("Automation: step frame"); Pixels("initial-offset", 2); Thread.Sleep(220); }
            NextPreview(() => Button("Automation: step frame")); Check("scheduled preview observes native automation step", MediaCounter() == 2 + shift); Pixels("automation-step", 2 + shift); Thread.Sleep(220);
            Button("Human control"); Until(() => Status("Human owns input"), "Human ownership not granted");
            Button("Automation: step frame"); Until(() => Status("Input rejected: Human"), "Automation input was not rejected"); Pixels("nonowner-automation-rejected", 2 + shift);
            NextPreview(() => outer.PostKeyboardInput(canvas, ".")); Check("scheduled preview observes native human step", MediaCounter() == 3 + shift); Pixels("human-step", 3 + shift); Thread.Sleep(220);
            Button("Return to automation"); Until(() => Status("Automation owns input"), "Automation ownership not restored");
            outer.PostKeyboardInput(canvas, "."); Thread.Sleep(260); Pixels("nonowner-human-rejected", 3 + shift);
            Button("Automation: step frame"); Pixels("automation-resumed", 4 + shift); Thread.Sleep(220);
            Button("Human control"); Button("GPU size: 480 × 270"); Until(() => Status("Input rejected: Human"), "Nonowner resize was not rejected"); Pixels("nonowner-resize-rejected", 4 + shift, 640, 360);
            Button("Return to automation"); NextPreview(() => Button("GPU size: 480 × 270")); Pixels("small-hidden-source", 4 + shift, 480, 270);
            File.WriteAllBytes(Path.Combine(output, "actual-viewer.png"), outer.CaptureVisualOutput(viewerRoot).Png);
            NextPreview(() => Button("GPU size: 960 × 540")); Pixels("large-hidden-source", 4 + shift, 960, 540);
            NextPreview(() => Button("GPU size: 640 × 360")); Pixels("restored-hidden-source", 4 + shift, 640, 360);
            Button("Human control"); outer.SendKey(canvas, 0x20); Thread.Sleep(250);
            var cpu = Cpu(); var clock = Stopwatch.StartNew(); var observed = new List<int>();
            for (int i = 0; i < 6; i++) { observed.Add(Pixels("playing-" + i)); Thread.Sleep(180); }
            double activeMs = clock.Elapsed.TotalMilliseconds; activity.Add(new { phase = "playing-and-viewer-inspection", ms = activeMs, cpu = Cost(cpu, activeMs), observed });
            Check("viewer Space handler resumes native playback", observed.Distinct().Count() >= 4);
            outer.SendKey(canvas, 0x20); Thread.Sleep(300); int paused = Pixels("human-paused"); Thread.Sleep(400); Pixels("human-paused-stable", paused);
            Button("Return to automation"); Button("Hide preview"); Thread.Sleep(300); int copies = shared.ReadInt32(72);
            cpu = Cpu(); clock.Restart(); Thread.Sleep(1500); double idleMs = clock.Elapsed.TotalMilliseconds; activity.Add(new { phase = "paused-preview-hidden", ms = idleMs, cpu = Cost(cpu, idleMs) });
            Check("hidden preview performs no GPU copies", copies == shared.ReadInt32(72));
            Button("Automation: play / pause"); Thread.Sleep(900); Check("hidden playback performs no GPU copies", copies == shared.ReadInt32(72));
            NextPreview(() => Button("Show preview")); Button("Automation: play / pause"); Thread.Sleep(300); paused = Pixels("resumed-inspection"); Thread.Sleep(300); Pixels("resumed-paused-stable", paused);
            int reader = Preview().GetProperty("helperPid").GetInt32();
            NextPreview(() => Button("Recover GPU reader")); Check("reader recovery preserves source and owner", reader != Preview().GetProperty("helperPid").GetInt32() && !CompatibilityVerification.Alive(reader) && Preview().GetProperty("owner").GetString() == "Automation"); Pixels("reader-recovered", paused);
            NextPreview(() => Button("Recover input worker")); Pixels("worker-recovered", paused);
            reader = Preview().GetProperty("helperPid").GetInt32(); uint threadId;
            using (var process = Process.GetProcessById(reader))
            {
                if (!outer.GetProcessIds().Contains(reader) || !string.Equals(process.MainModule!.FileName, Path.GetFullPath(Path.Combine(project, "artifacts/gpu-dynamic-build/GpuResearchHost.exe")), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Reader fault target must be the exact owned helper.");
                threadId = (uint)process.Threads.Cast<ProcessThread>().OrderBy(thread => thread.StartTime).First().Id;
            }
            nint threadHandle = OpenThread(0x802, false, threadId); Native.Check(threadHandle != 0, "Open owned GPU reader thread"); bool suspended = false; clock.Restart();
            try
            {
                if (GetProcessIdOfThread(threadHandle) != reader) throw new InvalidOperationException("Reader thread identity changed.");
                Native.Check(SuspendThread(threadHandle) != uint.MaxValue, "Pause owned GPU reader"); suspended = true;
                Button("Refresh");
                for (int i = 0; i < 12; i++) { var ping = Stopwatch.StartNew(); outer.ResearchProbeWindowResponse(viewerRoot); pings.Add(ping.Elapsed.TotalMilliseconds); Thread.Sleep(100); }
                Until(() => Status("GPU reader deadline exceeded"), "Reader failure did not reach the viewer", 6500);
                Check("hung reader contained while viewer remains responsive", pings.All(ms => ms < 500) && !CompatibilityVerification.Alive(reader) && CompatibilityVerification.Alive(sourcePid) && clock.ElapsedMilliseconds < 7500, new { ms = clock.Elapsed.TotalMilliseconds, maxPingMs = pings.Max() });
            }
            finally
            {
                try { if (suspended && CompatibilityVerification.Alive(reader)) Native.Check(Native.ResumeThread(threadHandle) != uint.MaxValue, "Resume owned GPU reader"); }
                finally { Native.CloseHandle(threadHandle); }
            }
            NextPreview(() => Button("Recover GPU reader")); Pixels("after-reader-timeout", paused);
            Button("Automation: step frame"); Pixels("input-after-recovery", paused % 40 + 1);
            Button("Request attention"); Until(() => Status("Please inspect"), "Attention request missing");
            owned = outer.GetProcessIds(); graceful = outer.CloseGracefully(TimeSpan.FromSeconds(5));
            Check("viewer graceful close cleans nested owned processes", graceful && owned.All(id => !CompatibilityVerification.Alive(id)));
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            shared?.Dispose(); mapping?.Dispose();
            if (!outer.Closed) { owned = outer.GetProcessIds(); outer.Dispose(); }
            Thread.Sleep(300);
        }
        bool released = mappingName == null; if (mappingName != null) { try { using var stale = MemoryMappedFile.OpenExisting(mappingName); } catch (FileNotFoundException) { released = true; } }
        Native.GetCursorPos(out var afterPointer); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id));
        bool focusEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        uint? inputAfter = Native.LastInputTick(); var clipAfter = QivResearch.CursorClip(); Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
        bool quiet = inputTick != null && inputTick == inputAfter, foregroundOutsideLayer = !owned.Contains((int)foregroundPid), confinementUnchanged = clipBefore.Equals(clipAfter);
        Check("mapping, process cleanup and physical desktop samples", cleanup && released && desktopEquality && confinementUnchanged && foregroundOutsideLayer && (!quiet || (focusEquality && pointerEquality)), new { cleanup, released, focusEquality, pointerEquality, desktopEquality, quiet, inputBefore = inputTick, inputAfter, foregroundOutsideLayer, confinementUnchanged });
        File.WriteAllText(Path.Combine(output, "gpu-bridge-viewer.json"), JsonSerializer.Serialize(new { passed, failure, checks, frames, activity, pings, owned, sourcePid, offset, qaDesktop = outer.Id, graceful, cleanup, released, focusEquality, pointerEquality, desktopEquality, inputBefore = inputTick, inputAfter = Native.LastInputTick(), note = "Actual viewer, toolbar and preview handlers operated on an outer private QA desktop. Ordinary pinned mpv remains hidden on its own private desktop; capture-only process instrumentation and native input. Pixel-encoded media frames verify results. Reader fault suspends only an identified owned helper and resumes it in finally if alive. Physical human acceptance pending; WM_NULL responsiveness pings are not hardware input." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    internal static int Concurrent(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        int[] results = Task.WhenAll(Task.Run(() => Run(Path.Combine(output, "left"))), Task.Run(() => Run(Path.Combine(output, "right"), true))).GetAwaiter().GetResult();
        using var left = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "left/gpu-bridge-viewer.json")));
        using var right = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "right/gpu-bridge-viewer.json")));
        var a = left.RootElement; var b = right.RootElement;
        bool independent = a.GetProperty("sourcePid").GetInt32() != b.GetProperty("sourcePid").GetInt32() && a.GetProperty("qaDesktop").GetString() != b.GetProperty("qaDesktop").GetString();
        int HumanFrame(JsonElement report) => report.GetProperty("frames").EnumerateArray().Single(frame => frame.GetProperty("name").GetString() == "human-step").GetProperty("counter").GetInt32();
        bool differentFrames = results.All(code => code == 0) && HumanFrame(a) + 1 == HumanFrame(b);
        bool passed = results.All(code => code == 0) && independent && differentFrames;
        File.WriteAllText(Path.Combine(output, "concurrent-viewers.json"), JsonSerializer.Serialize(new { passed, independent, differentFrames, results, cleanup = a.GetProperty("cleanup").GetBoolean() && b.GetProperty("cleanup").GetBoolean(), note = "Two complete viewer verifications overlapped, including separate ownership, different paused media frames, resize, recovery and reader faults. Each viewer and its ordinary source use separate private desktops/jobs. This does not represent physical human acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

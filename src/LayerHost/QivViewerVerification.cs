using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace LayerHost;

internal static class QivViewerVerification
{
    internal static int Run(string output, bool independentFirst = false, Barrier? pairing = null, bool switchCapture = false, bool nativeStartup = false)
    {
        GpuBridgeVerification.PrepareOutput(output); using var manager = new LayerManager(); using var qa = manager.CreateLayer();
        string registryBefore = QivResearch.RegistryState(), desktop = Native.InputDesktopName(); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); uint? inputBefore = Native.LastInputTick();
        var checks = new List<object>(); var frames = new List<object>(); int[] owned = []; bool passed = true, graceful = false; string? failure = null; int sourcePid = 0;
        MemoryMappedFile? mapping = null; MemoryMappedViewAccessor? shared = null;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Wait(Func<bool> condition, string reason)
        { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(40); } while (clock.ElapsedMilliseconds < 12000); throw new TimeoutException(reason); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Button(string name) { var button = qa.GetWindows().Single(window => window.Text == name); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(80); }
        bool Status(string value) => qa.GetWindows().Any(window => window.Text.Contains(value, StringComparison.Ordinal));
        JsonElement Capture(string name, bool second = false, int width = 0, int height = 0)
        {
            var watch = Stopwatch.StartNew(); JsonElement info = default, pixels = default; int attempts = 0; bool expected;
            do
            {
                long version = Preview().GetProperty("previewVersion").GetInt64(); Button("Inspect hidden app"); Wait(() => Preview().GetProperty("previewVersion").GetInt64() > version, "qIV preview missing");
                info = Preview(); pixels = QivResearch.Pixels(Path.Combine(output, "viewer-preview.png")); attempts++;
                expected = pixels.GetProperty(second ? "magenta" : "red").GetInt32() > 1000 && (width == 0 || (pixels.GetProperty("Width").GetInt32() == width && pixels.GetProperty("Height").GetInt32() == height));
                if (expected) break;
                if (attempts == 1) File.WriteAllBytes(Path.Combine(output, name + "-first.png"), SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png")));
                Thread.Sleep(60);
            } while (watch.ElapsedMilliseconds < 3500);
            Check(name, expected && !info.GetProperty("sourceWindowVisible").GetBoolean() && info.GetProperty("applicationPid").GetInt32() == sourcePid, new { pixels, info, attempts });
            File.WriteAllBytes(Path.Combine(output, name + ".png"), SnapshotFiles.ReadBytes(Path.Combine(output, "viewer-preview.png"))); frames.Add(new { name, pixels, info, attempts }); return pixels;
        }
        try
        {
            string ready = Path.Combine(output, "session.json");
            var launchArgs=new List<string>{"--demo", "--ready-file", ready, "--qa-report-directory", output, "--experimental-gpu-bridge", "qiv", "--root-input", "--queue-pointer"};if(nativeStartup)launchArgs.AddRange(["--qiv-native-startup","--inspection-settle","30"]);launchArgs.Add(QivResearch.Executable);
            qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), launchArgs.ToArray(), output);
            Wait(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "qIV viewer startup failed");
            var windows = qa.GetWindows(); long root = windows.Single(window => window.Text == "VIAVW — private application layer").Handle;
            long canvas = windows.Single(window => window.Parent == root && window.Width > 900 && window.Height > 200).Handle;
            var info = Preview(); sourcePid = info.GetProperty("applicationPid").GetInt32();
            if(nativeStartup)
            {
                Check("viewer starts with standalone guard and no GPU reader",info.GetProperty("qivNativeInspection").GetBoolean()&&info.GetProperty("helperPid").ValueKind==JsonValueKind.Null&&info.GetProperty("qivGuard").GetProperty("installed").GetInt32()==1&&!info.GetProperty("qivGuardSetup").GetProperty("gpuBridgeLoaded").GetBoolean()&&!info.GetProperty("qivGuardSetup").GetProperty("helperGraphicsModules").GetBoolean());
                Capture("native-startup");File.WriteAllBytes(Path.Combine(output,"actual-viewer-native-startup.png"),qa.CaptureVisualOutput(root).Png);
                Button("Use hidden GPU capture");Wait(()=>Preview().GetProperty("helperPid").ValueKind==JsonValueKind.Number,"GPU capture after native startup missing");info=Preview();
            }
            mapping = MemoryMappedFile.OpenExisting(info.GetProperty("gpuMapping").GetString()!, MemoryMappedFileRights.Read); shared = mapping.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read);
            Check("guarded ordinary image viewer launched visibly", windows.Any(window => window.Handle == root && window.Visible) && shared.ReadInt32(384) == 1);
            var initial = Capture("initial");
            Button("Automation: next image"); Thread.Sleep(200); Capture("automation-next", true);
            Button("Automation: next image"); Thread.Sleep(200); Capture("automation-wrap");
            Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership missing");
            Button("Automation: next image"); Wait(() => Status("Input rejected: Human"), "Automation mouse command not rejected"); Capture("nonowner-rejected");
            for (int i = 0; i < 3; i++) { qa.SendQueueMouseSequence(canvas, [new(MouseMessage.Move, 320, 180), new(MouseMessage.Wheel, 320, 180, 8, 120), new(MouseMessage.Move, 320, 180)], true); Thread.Sleep(120); }
            var zoomed = Capture("human-control-wheel"); Check("viewer preserves native wheel modifiers", zoomed.GetProperty("colorArea").GetInt32() > initial.GetProperty("colorArea").GetInt32() + 1000, new { initial, zoomed });
            qa.SendQueueMouseSequence(canvas, [new(MouseMessage.Move, 320, 180), new(MouseMessage.LeftDown, 320, 180, 1), new(MouseMessage.Move, 410, 230, 1), new(MouseMessage.LeftUp, 410, 230)], true); Thread.Sleep(150);
            var dragged = Capture("human-buffered-pan"); Check("viewer pan changes actual image fiducial", Math.Abs(dragged.GetProperty("darkY").GetDouble() - zoomed.GetProperty("darkY").GetDouble()) > 15);
            Button("Return to automation"); Wait(() => Status("Automation owns input"), "Automation ownership missing");
            qa.SendMouseEvent(canvas, MouseMessage.Wheel, 320, 180, wheelDelta: -120); Thread.Sleep(180); Capture("nonowner-picture-wheel-rejected");
            Button("Automation: reset zoom"); Thread.Sleep(150); var reset = Capture("automation-reset"); Check("automation restores fit after handoff", Math.Abs(reset.GetProperty("darkY").GetDouble() - initial.GetProperty("darkY").GetDouble()) < 3);
            Button("Hide preview"); Thread.Sleep(200); int copies = shared.ReadInt32(72); Button("Automation: next image"); Thread.Sleep(600); Check("no copies with preview hidden", copies == shared.ReadInt32(72)); Capture("hidden-input-inspected", true);
            Button("Recover GPU reader"); Thread.Sleep(200); Capture("reader-recovered", true); Button("Recover input worker"); Thread.Sleep(200); Capture("worker-recovered", true);
            Button("GPU size: 480 × 270"); Thread.Sleep(200); Capture("small-preview", true, 480, 270); File.WriteAllBytes(Path.Combine(output, "actual-viewer.png"), qa.CaptureVisualOutput(root).Png);
            Check("real confinement requests denied without key shim", (nativeStartup?Preview().GetProperty("qivGuard").GetProperty("clipRequests").GetInt32()>0:shared.ReadInt32(388)>0) && shared.ReadInt32(380) == 0, new { deniedClipRequests = nativeStartup?Preview().GetProperty("qivGuard").GetProperty("clipRequests").GetInt32():shared.ReadInt32(388), deniedWarpRequests = shared.ReadInt32(392) });
            if (switchCapture)
            {
                Button("GPU size: 640 × 360"); Capture("switch-ready", true, 640, 360);
                int oldReader = Preview().GetProperty("helperPid").GetInt32(); string oldMapping = Preview().GetProperty("gpuMapping").GetString()!;
                Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership before native switch missing");
                Button("Use native inspection"); var native = Capture("native-switched", true, 640, 360); var nativeInfo=Preview();
                Check("native switch stops interception and closes reader without restarting source", nativeInfo.GetProperty("qivNativeInspection").GetBoolean() && nativeInfo.GetProperty("helperPid").ValueKind == JsonValueKind.Null && nativeInfo.GetProperty("gpuMetrics").ValueKind == JsonValueKind.Null && shared.ReadInt32(24)==0 && !CompatibilityVerification.Alive(oldReader) && nativeInfo.GetProperty("owner").GetString()=="Human");
                Button("Automation: next image"); Wait(() => Status("Input rejected: Human"), "Native mode automation ownership rejection missing");
                for(int i=0;i<3;i++){qa.SendQueueMouseSequence(canvas,[new(MouseMessage.Move,320,180),new(MouseMessage.Wheel,320,180,8,120),new(MouseMessage.Move,320,180)],true);Thread.Sleep(120);}
                var nativeZoom=Capture("native-human-zoom",true); Check("native inspection retains human Ctrl-wheel zoom",nativeZoom.GetProperty("colorArea").GetInt32()>native.GetProperty("colorArea").GetInt32()+1000);
                qa.SendQueueMouseSequence(canvas,[new(MouseMessage.Move,320,180),new(MouseMessage.LeftDown,320,180,1),new(MouseMessage.Move,410,230,1),new(MouseMessage.LeftUp,410,230)],true);Thread.Sleep(150);
                var nativePan=Capture("native-human-pan",true); Check("native inspection retains buffered image pan",Math.Abs(nativePan.GetProperty("darkY").GetDouble()-nativeZoom.GetProperty("darkY").GetDouble())>15);
                Button("Recover input worker"); Capture("native-worker-recovered",true); Check("native recovery retains human owner",Preview().GetProperty("owner").GetString()=="Human");
                File.WriteAllBytes(Path.Combine(output,"actual-viewer-native.png"),qa.CaptureVisualOutput(root).Png);
                Button("Use hidden GPU capture"); var back=Capture("gpu-restored",true); var gpuInfo=Preview();
                Check("GPU switch recreates reader and preserves source pixels and owner",gpuInfo.GetProperty("helperPid").GetInt32()!=oldReader && gpuInfo.GetProperty("gpuMapping").GetString()!=oldMapping && !gpuInfo.GetProperty("qivNativeInspection").GetBoolean() && gpuInfo.GetProperty("owner").GetString()=="Human" && Math.Abs(back.GetProperty("darkY").GetDouble()-nativePan.GetProperty("darkY").GetDouble())<3);
                Button("Return to automation"); Wait(() => Status("Automation owns input"),"Ownership after capture switch missing"); Button("Automation: reset zoom"); Capture("switch-reset",true);
                for(int i=0;i<3;i++)
                {
                    int reader=Preview().GetProperty("helperPid").GetInt32(); Button("Use native inspection"); Capture("native-cycle-"+i,true);
                    Check("native cycle closes reader "+i,!CompatibilityVerification.Alive(reader));
                    Button("Use hidden GPU capture"); Capture("gpu-cycle-"+i,true);
                }
                Button("Use native inspection"); Capture("native-input-ready",true); Button("Hide preview"); long version=Preview().GetProperty("previewVersion").GetInt64();
                Button("Automation: next image"); Thread.Sleep(500); Check("native hidden preview does not inspect or create a reader",Preview().GetProperty("previewVersion").GetInt64()==version && Preview().GetProperty("helperPid").ValueKind==JsonValueKind.Null);
                Capture("native-delayed-inspection"); Button("Automation: next image"); Capture("native-back-to-second",true);
                if(pairing == null || independentFirst) { Button("Use hidden GPU capture"); Capture("final-gpu",true); }
                File.WriteAllBytes(Path.Combine(output,"actual-viewer-switched.png"),qa.CaptureVisualOutput(root).Png);
            }
            if (pairing != null)
            {
                if (independentFirst) { Button("Automation: next image"); Thread.Sleep(150); }
                Capture(independentFirst ? "independent-first" : "independent-second", !independentFirst);
                Check("different layer states overlap before close", pairing.SignalAndWait(TimeSpan.FromSeconds(10)));
            }
            owned = qa.GetProcessIds(); graceful = qa.CloseGracefully(TimeSpan.FromSeconds(5)); Check("viewer graceful cleanup", graceful && owned.All(id => !CompatibilityVerification.Alive(id)));
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { shared?.Dispose(); mapping?.Dispose(); if (!qa.Closed) { owned = qa.GetProcessIds(); qa.Dispose(); } Thread.Sleep(200); }
        Native.GetCursorPos(out var after); uint? inputAfter = Native.LastInputTick(); bool pointerEquality = pointer.X == after.X && pointer.Y == after.Y, focusEquality = foreground == Native.GetForegroundWindow();
        string desktopAfter = Native.InputDesktopName(); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), registryUnchanged = registryBefore == QivResearch.RegistryState(), desktopUnchanged = desktop == desktopAfter;
        Check("registry, desktop and owned cleanup", cleanup && registryUnchanged && desktopUnchanged && (inputBefore == null || inputBefore != inputAfter || pointerEquality), new { cleanup, registryUnchanged, desktopUnchanged, focusEquality, pointerEquality, inputBefore, inputAfter });
        File.WriteAllText(Path.Combine(output, "qiv-viewer.json"), JsonSerializer.Serialize(new { passed, failure, switchCapture, nativeStartup, checks, frames, sourcePid, qaDesktop = qa.Id, owned, graceful, cleanup, registryUnchanged, desktopUnchanged, desktopBefore = desktop, desktopAfter, focusEquality, pointerEquality, inputBefore, inputAfter, note = "Actual wrapper controls and native mouse input exercised on an outer private QA desktop. Pinned qIV uses dedicated file-backed configuration and process-local cursor-confinement denial. Ctrl-wheel state captured in the viewer before asynchronous dispatch; drag is delivered on release. No physical human acceptance claimed." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
    internal static int Concurrent(string output, bool switchCapture = false, bool nativeStartup = false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var pairing = new Barrier(2);
        int[] results = Task.WhenAll(Task.Run(() => Run(Path.Combine(output, "left"), pairing: pairing, switchCapture: switchCapture,nativeStartup:nativeStartup)), Task.Run(() => Run(Path.Combine(output, "right"), true, pairing, switchCapture,nativeStartup))).GetAwaiter().GetResult();
        using var a = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "left/qiv-viewer.json"))); using var b = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "right/qiv-viewer.json")));
        bool independent = a.RootElement.GetProperty("sourcePid").GetInt32() != b.RootElement.GetProperty("sourcePid").GetInt32() && a.RootElement.GetProperty("qaDesktop").GetString() != b.RootElement.GetProperty("qaDesktop").GetString();
        bool differentImages = a.RootElement.GetProperty("frames").EnumerateArray().Any(frame => frame.GetProperty("name").GetString() == "independent-second") && b.RootElement.GetProperty("frames").EnumerateArray().Any(frame => frame.GetProperty("name").GetString() == "independent-first");
        bool? mixedBackends = switchCapture ? a.RootElement.GetProperty("frames").EnumerateArray().Single(frame=>frame.GetProperty("name").GetString()=="independent-second").GetProperty("info").GetProperty("qivNativeInspection").GetBoolean() && !b.RootElement.GetProperty("frames").EnumerateArray().Single(frame=>frame.GetProperty("name").GetString()=="independent-first").GetProperty("info").GetProperty("qivNativeInspection").GetBoolean() : null;
        bool passed = results.All(code => code == 0) && independent && differentImages && mixedBackends != false;
        File.WriteAllText(Path.Combine(output, "concurrent-qiv.json"), JsonSerializer.Serialize(new { passed, results, independent, differentImages, mixedBackends, cleanup = a.RootElement.GetProperty("cleanup").GetBoolean() && b.RootElement.GetProperty("cleanup").GetBoolean() }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

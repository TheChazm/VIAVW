using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class WpfNativeViewerVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); var results = new List<object>(); bool passed = true;
        using var manager = new LayerManager();
        foreach (string mode in new[] { "software", "layered", "startup-hook" })
        {
            string folder = Path.Combine(output, mode); Directory.CreateDirectory(folder); using var qa = manager.CreateLayer();
            var args = new List<string> { "--demo", "--queue-pointer" }; if (mode == "layered") args.Add("--full-content");
            if (mode == "startup-hook") { args.Add("--wpf-software-hook"); args.AddRange([Path.Combine(project, "artifacts/baselines/accepted-private-gpu-20261014/src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), "--report"]); }
            else args.AddRange([Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), "--" + mode, "--report"]);
            qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), folder);
            var watch = Stopwatch.StartNew(); WindowInfo[] windows = [];
            while (watch.ElapsedMilliseconds < 15000) { windows = qa.GetWindows(); if (windows.Any(w => w.Text.StartsWith("Automation | PID"))) break; Thread.Sleep(100); }
            var root = windows.First(w => w.Text == "VIAVW — private application layer"); var picture = windows.First(w => w.Parent == root.Handle && w.Width > 900 && w.Height > 200);
            JsonElement Observe(Func<JsonElement, bool> condition)
            {
                var until = Stopwatch.StartNew(); JsonElement state;
                do { state = CompatibilityVerification.Read(folder); if (condition(state)) return state; Thread.Sleep(50); } while (until.ElapsedMilliseconds < 6000);
                throw new TimeoutException("WPF viewer state deadline: " + state);
            }
            void Button(string text) { var button = qa.GetWindows().First(w => w.Text == text); qa.SendMouseInput(button.Handle, 15, 12); }
            long PreviewVersion() { using var report = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(folder, "viewer-preview.json"))); return report.RootElement.GetProperty("previewVersion").GetInt64(); }
            CaptureFrame RefreshFrame()
            {
                long before = PreviewVersion(); Button("Refresh"); var until = Stopwatch.StartNew();
                while (PreviewVersion() <= before && until.ElapsedMilliseconds < 6000) Thread.Sleep(50);
                if (PreviewVersion() <= before) throw new TimeoutException("Viewer preview publication deadline.");
                return qa.CaptureVisualOutput(root.Handle);
            }
            int TextInk(CaptureFrame frame)
            {
                var origin = qa.ClientOrigin(root.Handle);
                using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream); int dark = 0;
                int left = picture.X - origin.X + 24, top = picture.Y - origin.Y + 60;
                for (int y = top + 3; y < Math.Min(image.Height, top + 47); y++) for (int x = left + 3; x < Math.Min(image.Width, left + 567); x++) { var p = image.GetPixel(x, y); if (p.R < 80 && p.G < 80 && p.B < 80) dark++; }
                return dark;
            }
            Button("Automation: send text"); var automated = Observe(v => v.GetProperty("text").GetString() == "Hello from virtual input");
            Button("Human control"); Thread.Sleep(200); qa.SendMouseInput(picture.Handle, 80, 80); qa.PostKeyboardInput(picture.Handle, "_HUMAN");
            Observe(v => v.GetProperty("text").GetString()!.Contains("_HUMAN")); qa.SendQueueStateChord(picture.Handle, [0x11, 0x41]); Thread.Sleep(250); qa.PostKeyboardInput(picture.Handle, "NATIVE_WPF");
            Observe(v => v.GetProperty("text").GetString() == "NATIVE_WPF"); qa.SendQueueStateChord(picture.Handle, [0x11, 0x4B]); Observe(v => v.GetProperty("shortcuts").GetInt32() == 1);
            qa.SendQueueMouseSequence(picture.Handle, [new(MouseMessage.Move, 32, 80), new(MouseMessage.LeftDown, 32, 80, 1), new(MouseMessage.Move, 155, 80, 1), new(MouseMessage.LeftUp, 155, 80)], true);
            var dragged = Observe(v => v.GetProperty("selection").GetString()!.Length > 0);
            // Losing viewer capture cancels the buffered gesture without delivering source input.
            qa.SendMouseEvent(picture.Handle, MouseMessage.LeftDown, 90, 80, buttons: 1);
            Button("Return to automation"); Thread.Sleep(200);
            bool cancelledGesture = CompatibilityVerification.Read(folder).GetProperty("selection").GetString() == dragged.GetProperty("selection").GetString();
            Button("Human control"); Thread.Sleep(100); qa.SendMouseEvent(picture.Handle, MouseMessage.LeftUp, 220, 80); Thread.Sleep(150);
            cancelledGesture &= CompatibilityVerification.Read(folder).GetProperty("selection").GetString() == dragged.GetProperty("selection").GetString();
            qa.SendMouseInput(picture.Handle, 80, 150); Observe(v => v.GetProperty("clicks").GetInt32() == 1);
            qa.SendMouseEvent(picture.Handle, MouseMessage.Wheel, 90, 285, wheelDelta: -120); var scrolled = Observe(v => v.GetProperty("scrollOffset").GetDouble() > 0);
            Button("Hide app window"); Thread.Sleep(150); long inspectionBefore = PreviewVersion(); Button("Inspect hidden app"); var inspectWait = Stopwatch.StartNew();
            bool Inspected() { using var preview = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(folder, "viewer-preview.json"))); return preview.RootElement.GetProperty("previewVersion").GetInt64() > inspectionBefore && preview.RootElement.GetProperty("Backend").GetString()!.Contains("temporary private show") && !preview.RootElement.GetProperty("sourceWindowVisible").GetBoolean(); }
            while (!Inspected() && inspectWait.ElapsedMilliseconds < 6000) Thread.Sleep(50);
            using var inspectReport = System.Text.Json.JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(folder, "viewer-preview.json")));
            bool hiddenInspection = inspectReport.RootElement.GetProperty("Backend").GetString()!.Contains("temporary private show") && !inspectReport.RootElement.GetProperty("sourceWindowVisible").GetBoolean();
            var inspectedPixels = WpfCaptureResearch.Pixels(qa.CaptureVisualOutput(root.Handle), Path.Combine(folder, "hidden-inspection.png"));
            hiddenInspection &= JsonSerializer.SerializeToElement(inspectedPixels).GetProperty("orange").GetInt32() > 500;
            Button("Show app window"); Thread.Sleep(150);
            qa.SendMouseInput(picture.Handle, 80, 80); Thread.Sleep(200); Button("Return to automation"); Thread.Sleep(200); qa.PostKeyboardInput(picture.Handle, "REJECT"); Thread.Sleep(350);
            bool rejected = CompatibilityVerification.Read(folder).GetProperty("text").GetString() == "NATIVE_WPF";
            var beforeFrame = RefreshFrame(); int beforeInk = TextInk(beforeFrame);
            Button("Automation: send text"); var resumed = Observe(v => v.GetProperty("text").GetString()!.Contains("Hello from virtual input"));
            var afterFrame = RefreshFrame(); int afterInk = TextInk(afterFrame), refreshRetries = 0;
            while (afterInk < beforeInk + 200 && refreshRetries < 3) { Thread.Sleep(150); afterFrame = RefreshFrame(); afterInk = TextInk(afterFrame); refreshRetries++; }
            bool updatedTextPixels = afterInk >= beforeInk + 200;
            var pixels = WpfCaptureResearch.Pixels(afterFrame, Path.Combine(folder, "native-wpf-viewer.png"));
            int[] ids = qa.GetProcessIds(); qa.Dispose(); Thread.Sleep(300); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
            bool correct = rejected && cancelledGesture && hiddenInspection && resumed.GetProperty("pid").GetInt32() == automated.GetProperty("pid").GetInt32() && JsonSerializer.SerializeToElement(pixels).GetProperty("orange").GetInt32() > 500 && updatedTextPixels;
            passed &= correct && cleanup; results.Add(new { mode, automated, dragged, cancelledGesture, scrolled, hiddenInspection, inspectedPixels, resumed, rejected, pixels, updatedTextPixels, beforeInk, afterInk, refreshRetries, correct, cleanup }); Console.WriteLine(JsonSerializer.Serialize(new { mode, correct, cleanup, cancelledGesture, hiddenInspection, updatedTextPixels, beforeInk, afterInk, refreshRetries }));
        }
        Native.GetCursorPos(out var endCursor);
        File.WriteAllText(Path.Combine(output, "wpf-native-viewer.json"), JsonSerializer.Serialize(new { passed, results, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == endCursor.X && cursor.Y == endCursor.Y, note = "Actual multithreaded viewer and human ownership routing exercised on private QA desktops. WPF software/layered fixture modes and unchanged baseline executable with process-local startup software adapter; native PrintWindow, no grab adapter. Human physical acceptance pending. Default opaque hardware WPF remains unsupported by this capture path." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

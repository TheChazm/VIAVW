using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class DynamicGpuViewerVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName();
        using var manager = new LayerManager(); using var qa = manager.CreateLayer();
        var checks = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = [];
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Until(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(50); } while (watch.ElapsedMilliseconds < 10000);
            throw new TimeoutException("Dynamic viewer observation did not arrive.");
        }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        JsonElement State() => CompatibilityVerification.Read(output);
        void Button(string name) { var button = qa.GetWindows().Single(window => window.Text == name); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(100); }
        JsonElement Capture(string name, int clicks, int width, int height)
        {
            long version = Preview().GetProperty("previewVersion").GetInt64(); Button("Inspect hidden app"); Until(() => Preview().GetProperty("previewVersion").GetInt64() > version);
            var info = Preview(); using var image = SnapshotFiles.ReadBitmap(Path.Combine(output, "viewer-preview.png")); var marker = image.GetPixel(image.Width / 2, image.Height / 2);
            bool color = clicks % 2 == 0 ? marker.G > 120 && marker.R < 110 && marker.B < 150 : marker.R > 180 && marker.G > 80 && marker.G < 190 && marker.B < 100;
            int counter = 0; for (int bit = 0; bit < 16; bit++) { var cell = image.GetPixel(14 + bit * 10, image.Height - 16); if (cell.R > 220 && cell.G > 220 && cell.B > 220) counter |= 1 << bit; }
            Check(name, color && counter > 0 && image.Width == width && image.Height == height && !info.GetProperty("sourceWindowVisible").GetBoolean(), new { counter, info });
            image.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png); return info;
        }
        try
        {
            string ready = Path.Combine(output, "session.json");
            qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), ["--demo", "--ready-file", ready, "--qa-report-directory", output, "--experimental-gpu-bridge", "dynamic", "--root-input", Path.Combine(project, "artifacts/gpu-dynamic-build/DynamicD3DProbe.exe")], output);
            Until(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""));
            var windows = qa.GetWindows(); long root = windows.Single(window => window.Text == "VIAVW — private application layer").Handle;
            long canvas = windows.Single(window => window.Parent == root && window.Width > 900 && window.Height > 200).Handle;
            var first = Capture("initial", 0, 620, 440); int pid = first.GetProperty("applicationPid").GetInt32();
            qa.SendKey(canvas, 0x20); Thread.Sleep(150); Check("automation owner rejects picture input", State().GetProperty("clicks").GetInt32() == 0);
            Button("Human control"); qa.SendKey(canvas, 0x20); Until(() => State().GetProperty("clicks").GetInt32() == 1); Capture("human-space", 1, 620, 440);
            qa.SendQueueStateChord(canvas, [0x11, 0x4B]); Until(() => State().GetProperty("shortcuts").GetInt32() == 1);
            Check("fixture-only scoped async modifier reaches viewer chord", State().GetProperty("asyncCtrlState").GetBoolean());
            qa.SendKey(canvas, 0x74); Until(() => State().GetProperty("width").GetInt32() == 800); Capture("human-f5-resize", 1, 800, 600);
            Button("Recover GPU reader"); Thread.Sleep(200); var recovered = Capture("reader-recovered", 1, 800, 600);
            Check("fixture reader recovery retains source", recovered.GetProperty("applicationPid").GetInt32() == pid && recovered.GetProperty("owner").GetString() == "Human");
            Button("Return to automation"); Button("GPU size: 480 × 270"); Until(() => State().GetProperty("width").GetInt32() == 480); Capture("automation-resize", 1, 480, 270);
            owned = qa.GetProcessIds(); graceful = qa.CloseGracefully(TimeSpan.FromSeconds(5)); Check("dynamic viewer graceful cleanup", graceful && owned.All(id => !CompatibilityVerification.Alive(id)));
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; }
        finally { if (!qa.Closed) { owned = qa.GetProcessIds(); qa.Dispose(); } Thread.Sleep(200); }
        Native.GetCursorPos(out var after); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), physical = foreground == Native.GetForegroundWindow() && pointer.X == after.X && pointer.Y == after.Y && desktop == Native.InputDesktopName();
        Check("fixture cleanup and physical samples", cleanup && physical);
        File.WriteAllText(Path.Combine(output, "dynamic-viewer.json"), JsonSerializer.Serialize(new { passed, failure, checks, owned, graceful, cleanup, physical, note = "Actual experimental viewer operated on a private QA desktop. The separate native fixture carries a frame counter in GPU pixels; its passive telemetry verifies input. Async modifier shim is fixture-only. Physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

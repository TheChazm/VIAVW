using System.Text.Json;

namespace LayerHost;

internal static class WpfCaptureResearch
{
    internal static object Pixels(CaptureFrame frame, string path)
    {
        File.WriteAllBytes(path, frame.Png); using var stream = new MemoryStream(frame.Png); using var bitmap = new Bitmap(stream);
        int green = 0, orange = 0, magenta = 0, dark = 0, samples = 0;
        for (int y = 0; y < bitmap.Height; y += 3) for (int x = 0; x < bitmap.Width; x += 3)
        {
            var p = bitmap.GetPixel(x, y); samples++;
            if (p.R < 110 && p.G > 150 && p.B < 140) green++;
            if (p.R > 200 && p.G is > 100 and < 180 && p.B < 100) orange++;
            if (p.R > 240 && p.B > 240 && p.G < 30) magenta++;
            if (p.R < 20 && p.G < 20 && p.B < 20) dark++;
        }
        return new { frame.Backend, frame.Width, frame.Height, frame.CaptureMilliseconds, green, orange, magenta, dark, samples };
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); var results = new List<object>(); bool passed = true;
        using var manager = new LayerManager();
        foreach (var variant in new[] { "default", "software", "layered", "layered-software" })
        {
            string folder = Path.Combine(output, variant); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer();
            var args = new List<string> { "--enable-grab-adapter" }; if (variant.Contains("software")) args.Add("--software"); if (variant.Contains("layered")) args.Add("--layered");
            layer.LaunchApplication(Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), args.ToArray(), folder);
            var root = CompatibilityVerification.WaitRoot(layer, "Disposable WPF application"); Thread.Sleep(350);
            var modes = new List<object>();
            foreach (string mode in new[] { "pw0", "pw1", "pw2", "pw3", "print-client", "print-window", "window-dc", "redraw-pw3", "redraw-print-client", "redraw-window-dc" })
            {
                try { modes.Add(new { mode, pixels = Pixels(layer.CaptureResearch(root.Handle, mode), Path.Combine(folder, mode + ".png")) }); }
                catch (Exception e) { modes.Add(new { mode, error = e.Message }); }
            }
            layer.SendKeyboardInput(root.Handle, "RESEARCH_WPF");
            layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 80, 150), new(MouseMessage.LeftDown, 80, 150, 1), new(MouseMessage.LeftUp, 80, 150)], true);
            var state = CompatibilityVerification.Read(folder); var observe = System.Diagnostics.Stopwatch.StartNew();
            while ((state.GetProperty("text").GetString() != "RESEARCH_WPF" || state.GetProperty("clicks").GetInt32() != 1) && observe.ElapsedMilliseconds < 3000)
            { Thread.Sleep(100); state = CompatibilityVerification.Read(folder); }
            var after = Pixels(layer.CaptureResearch(root.Handle, "redraw-pw3"), Path.Combine(folder, "after-input-native.png"));
            var control = Pixels(layer.CaptureVisualOutput(root.Handle, CaptureBackend.CooperativeFixture), Path.Combine(folder, "positive-control.png"));
            bool correct = state.GetProperty("text").GetString() == "RESEARCH_WPF" && state.GetProperty("clicks").GetInt32() == 1 && JsonSerializer.SerializeToElement(control).GetProperty("orange").GetInt32() > 500;
            int[] ids = layer.GetProcessIds(); layer.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); passed &= correct && cleanup;
            results.Add(new { variant, modes, after, state, positiveControl = control, correct, cleanup });
            Console.WriteLine(JsonSerializer.Serialize(new { variant, correct, cleanup, after }));
            File.WriteAllText(Path.Combine(output, "wpf-capture.json"), JsonSerializer.Serialize(new { passed, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (string mode in new[] { "external-layered", "host", "layered-host" })
        {
            string folder = Path.Combine(output, mode); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer(); object? pixels = null, state = null; string? error = null;
            layer.LaunchApplication(Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe"), [], folder);
            var root = CompatibilityVerification.WaitRoot(layer, "Disposable WPF application");
            try
            {
                layer.SendKeyboardInput(root.Handle, "PRESERVE_HOST_STATE"); Thread.Sleep(250);
                pixels = Pixels(layer.CaptureResearch(root.Handle, mode), Path.Combine(folder, "native.png"));
                layer.SendKeyboardInput(root.Handle, "_AFTER"); Thread.Sleep(400); state = CompatibilityVerification.Read(folder);
            }
            catch (Exception e) { error = e.ToString(); File.WriteAllText(Path.Combine(folder, "failure.txt"), error); }
            bool preserved = state is JsonElement observed && observed.GetProperty("text").GetString() == "PRESERVE_HOST_STATE_AFTER";
            int[] ids = layer.GetProcessIds(); layer.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); passed &= cleanup && preserved;
            results.Add(new { experiment = mode, pixels, state, error, preserved, cleanup });
        }
        Native.GetCursorPos(out var endCursor);
        File.WriteAllText(Path.Combine(output, "wpf-capture.json"), JsonSerializer.Serialize(new { passed, results, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == endCursor.X && cursor.Y == endCursor.Y, note = "Private desktops only. Fixture software/layered options are diagnostic application changes. Cooperative rendering used only as a separately labeled final pixel-positive control. Native captures and repaint do not call that adapter." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

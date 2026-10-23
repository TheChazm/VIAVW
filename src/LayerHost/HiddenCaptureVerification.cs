using System.Text.Json;

namespace LayerHost;

internal static class HiddenCaptureVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var manager = new LayerManager(); var results = new List<object>();
        foreach (string framework in new[] { "Quick", "WPF" })
        {
            string directory = Path.Combine(output, framework); Directory.CreateDirectory(directory); var layer = manager.CreateLayer();
            string executable = framework == "Quick" ? @"C:\Python314\python.exe" : Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe");
            string[] args = framework == "Quick" ? [Path.Combine(project, "experiments/quick_probe.py"), "--enable-grab-adapter"] : ["--enable-grab-adapter"];
            layer.LaunchApplication(executable, args, directory); var root = CompatibilityVerification.WaitRoot(layer, framework == "Quick" ? "Disposable Qt Quick application" : "Disposable WPF application");
            layer.SendKeyboardInput(root.Handle, "HIDDEN_BACKGROUND"); Thread.Sleep(250); var before = CompatibilityVerification.Read(directory);
            layer.HideLayer(root.Handle); Thread.Sleep(500); object? pixels = null; string? error = null;
            try { pixels = CompatibilityVerification.PixelEvidence(layer.CaptureVisualOutput(root.Handle, CaptureBackend.CooperativeFixture), Path.Combine(directory, "hidden-cooperative.png"), 490, 160, false); }
            catch (Exception e) { error = e.Message; }
            var after = CompatibilityVerification.Read(directory);
            results.Add(new { framework, before, after, pixels, error, actualWindowHidden = !layer.GetWindows().First(w => w.Handle == root.Handle).Visible }); layer.Dispose();
        }
        File.WriteAllText(Path.Combine(output, "hidden-capture.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(results)); return 0;
    }
}

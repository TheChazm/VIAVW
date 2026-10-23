using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class PrivateLauncherVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var before = Native.GetForegroundWindow(); using var manager = new LayerManager(); var qa = manager.CreateLayer();
        int pid = qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), ["--test-launcher", output], output);
        var watch = Stopwatch.StartNew(); while (CompatibilityVerification.Alive(pid) && watch.ElapsedMilliseconds < 60000) Thread.Sleep(100);
        bool finished = !CompatibilityVerification.Alive(pid); var ids = qa.GetProcessIds(); qa.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
        bool passed = false; if (finished && File.Exists(Path.Combine(output, "launcher.json"))) { using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "launcher.json"))); var root = report.RootElement; passed = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("passed", out var value) ? value.GetBoolean() : false; }
        File.WriteAllText(Path.Combine(output, "private-launcher.json"), JsonSerializer.Serialize(new { passed, finished, cleanup, foregroundEquality = Native.GetForegroundWindow() == before, note = "Real launch/reveal/duplicate/error/responsiveness tests run entirely inside a private QA desktop. No foreground controller was displayed on Default." }, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, passed, finished, cleanup })); return passed && cleanup ? 0 : 1;
    }
}

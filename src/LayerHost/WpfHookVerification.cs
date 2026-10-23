using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class WpfHookVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string executable = Path.Combine(project, "artifacts/baselines/accepted-private-gpu-20261014/src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe");
        string dll = Path.ChangeExtension(executable, ".dll"), hook = Path.Combine(project, "src/WpfRenderHook/bin/Release/net10.0-windows/WpfRenderHook.dll");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS"))) throw new InvalidOperationException("This experiment will not replace existing startup hooks.");
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string exeBefore = Hash(executable), dllBefore = Hash(dll); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor);
        var results = new List<object>(); bool passed = true; using var manager = new LayerManager();
        foreach (string mode in new[] { "control", "software-hook", "mismatched-target" })
        {
            string folder = Path.Combine(output, mode); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer(); Dictionary<string, string>? environment = null;
            if (mode != "control") environment = new() { ["DOTNET_STARTUP_HOOKS"] = hook, ["VIAVW_WPF_SOFTWARE_OPT_IN"] = "1", ["VIAVW_WPF_EXPECTED_EXE"] = mode == "software-hook" ? executable : Path.Combine(project, "artifacts", "not-the-selected-application.exe"), ["VIAVW_WPF_HOOK_REPORT"] = Path.Combine(folder, "hook.json") };
            layer.LaunchApplication(executable, [], folder, environment); var root = CompatibilityVerification.WaitRoot(layer, "Disposable WPF application"); Thread.Sleep(400);
            var initial = WpfCaptureResearch.Pixels(layer.CaptureVisualOutput(root.Handle), Path.Combine(folder, "before.png"));
            layer.SendKeyboardInput(root.Handle, "UNMODIFIED_BASELINE_WPF"); layer.SendQueueStateChord(root.Handle, [0x11, 0x4B]);
            layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 80, 150), new(MouseMessage.LeftDown, 80, 150, 1), new(MouseMessage.LeftUp, 80, 150)], true);
            var observe = Stopwatch.StartNew(); var state = CompatibilityVerification.Read(folder);
            while (state.GetProperty("clicks").GetInt32() != 1 && observe.ElapsedMilliseconds < 3000) { Thread.Sleep(50); state = CompatibilityVerification.Read(folder); }
            var after = WpfCaptureResearch.Pixels(layer.CaptureVisualOutput(root.Handle), Path.Combine(folder, "after.png"));
            var firstAfter = after; int captureAttempts = 1; var captureWait = Stopwatch.StartNew();
            if (mode == "software-hook")
            {
                // The WPF event/telemetry completes before its visual presentation.
                // Observe fresh pixels without replaying input or changing rendering.
                File.Copy(Path.Combine(folder, "after.png"), Path.Combine(folder, "after-first.png"), true);
                while (JsonSerializer.SerializeToElement(after).GetProperty("orange").GetInt32() <= 500 && captureWait.ElapsedMilliseconds < 2500)
                {
                    Thread.Sleep(50); after = WpfCaptureResearch.Pixels(layer.CaptureVisualOutput(root.Handle), Path.Combine(folder, "after.png")); captureAttempts++;
                }
            }
            captureWait.Stop();
            JsonElement? hookReport = null; if (mode != "control") { using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "hook.json"))); hookReport = report.RootElement.Clone(); }
            layer.TakeHumanControl(); bool rejected = false; try { layer.SendKeyboardInput(root.Handle, "REJECT"); } catch (InvalidOperationException) { rejected = true; }
            layer.ReturnToAutomation(); bool sameProcess = layer.ApplicationProcessId == state.GetProperty("pid").GetInt32();
            bool nativePixels = JsonSerializer.SerializeToElement(after).GetProperty("orange").GetInt32() > 500;
            bool correct = state.GetProperty("text").GetString() == "UNMODIFIED_BASELINE_WPF" && state.GetProperty("shortcuts").GetInt32() == 1 && rejected && sameProcess && (mode == "software-hook" ? nativePixels && hookReport!.Value.GetProperty("applied").GetBoolean() : !nativePixels && (mode == "control" || !hookReport!.Value.GetProperty("selected").GetBoolean()));
            int[] ids = layer.GetProcessIds(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); if (!graceful) layer.Dispose(); Thread.Sleep(250); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); passed &= correct && cleanup;
            results.Add(new { mode, initial, firstAfter, after, captureAttempts, captureWaitMilliseconds = captureWait.Elapsed.TotalMilliseconds, state, hookReport, nativePixels, rejected, sameProcess, correct, graceful, cleanup }); Console.WriteLine(JsonSerializer.Serialize(new { mode, nativePixels, correct, cleanup, captureAttempts, hookReport }));
        }
        bool binaryUnchanged = exeBefore == Hash(executable) && dllBefore == Hash(dll); passed &= binaryUnchanged; Native.GetCursorPos(out var endCursor);
        File.WriteAllText(Path.Combine(output, "wpf-hook.json"), JsonSerializer.Serialize(new { passed, binaryUnchanged, exeBefore, dllBefore, results, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == endCursor.X && cursor.Y == endCursor.Y, parentHookEnvironmentUnchanged = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS")), note = "Identical accepted baseline WPF executable in every control. Runtime-hosted process-local public render preference; injected managed startup code is explicitly an opt-in compatibility adapter, not external-only GPU capture. Tested only on disposable .NET 10 WPF. No registry/global environment change, code patching, remote thread, security bypass or driver." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

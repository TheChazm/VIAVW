using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class IlSpyVisibilityVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var power = new DisplayPowerObserver(); using var layer = manager.CreateLayer();
        var args = IlSpyVerification.Prepare(output); layer.LaunchApplication(IlSpyVerification.Executable, args, output, WpfHookEnvironment.Create(IlSpyVerification.Executable, output)); WpfHookEnvironment.VerifyAsync(output, layer.ApplicationProcessId).GetAwaiter().GetResult();
        var root = IlSpyVerification.WaitRoot(layer); Thread.Sleep(1500);
        layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 610, 38), new(MouseMessage.LeftDown, 610, 38, 1), new(MouseMessage.LeftUp, 610, 38)], true); Thread.Sleep(300);
        var results = new List<object>(); var phase = "";
        double Cpu() { using var process = Process.GetProcessById(layer.ApplicationProcessId); return process.TotalProcessorTime.TotalMilliseconds; }
        bool TextIs(string expected) { var snapshot = layer.ObserveAccessible(root.Handle, "searchBox"); return snapshot.GetProperty("nodes").EnumerateArray().Any(node => node.GetProperty("value").GetString() == expected); }
        void WaitText(string expected) { var until = Stopwatch.StartNew(); while (!TextIs(expected) && until.ElapsedMilliseconds < 5000) Thread.Sleep(80); if (!TextIs(expected)) throw new TimeoutException("Visible/hidden input state deadline."); }
        try
        {
            foreach (string variant in new[] { "visible", "hidden", "reshown", "hidden-again" })
            {
                phase = variant; bool hidden = variant.StartsWith("hidden"); if (hidden) layer.HideLayer(root.Handle); else layer.ShowLayer(root.Handle);
                Thread.Sleep(1500); double before = Cpu(); var idle = Stopwatch.StartNew(); Thread.Sleep(5000); double cpuMs = Cpu() - before, elapsedMs = idle.Elapsed.TotalMilliseconds;
                bool actualHidden = !layer.GetWindows().First(window => window.Handle == root.Handle).Visible;
                string marker = "STATE_" + variant.ToUpperInvariant(); layer.SendQueueStateChord(root.Handle, [0x11, 0x41]); layer.SendKeyboardInput(root.Handle, marker); WaitText(marker);
                var capture = layer.CaptureVisualOutput(root.Handle); File.WriteAllBytes(Path.Combine(output, variant + ".png"), capture.Png);
                int ink = 0; using (var stream = new MemoryStream(capture.Png)) using (var image = new Bitmap(stream)) for (int y = 85; y < 105; y++) for (int x = 315; x < 580; x++) { var pixel = image.GetPixel(x, y); if (pixel.R < 130 && pixel.G < 130 && pixel.B < 130) ink++; }
                var inspectionWatch = Stopwatch.StartNew(); var inspection = layer.CaptureResearch(root.Handle, "inspect-pw1"); inspectionWatch.Stop(); File.WriteAllBytes(Path.Combine(output, variant + "-inspection.png"), inspection.Png);
                int inspectionInk = 0; using (var stream = new MemoryStream(inspection.Png)) using (var image = new Bitmap(stream)) for (int y = 85; y < 105; y++) for (int x = 315; x < 580; x++) { var pixel = image.GetPixel(x, y); if (pixel.R < 130 && pixel.G < 130 && pixel.B < 130) inspectionInk++; }
                bool visibilityRestored = !layer.GetWindows().First(window => window.Handle == root.Handle).Visible == hidden;
                bool inspectionPixels = inspectionInk is > 50 and < 1500;
                results.Add(new { variant, actualHidden, expectedHidden = hidden, visibilityRestored, inspectionPixels, inputAccepted = true, marker, idleMs = elapsedMs, sourceCpuMs = cpuMs, sourceWholeMachineCpuPercent = cpuMs / elapsedMs / Environment.ProcessorCount * 100, capture.CaptureMilliseconds, nativeMs = capture.NativeRenderingMilliseconds, encodingMs = capture.EncodingMilliseconds, searchFieldInk = ink, inspectionInk, inspectionMs = inspectionWatch.Elapsed.TotalMilliseconds, stateAfterInspection = TextIs(marker), power = power.Snapshot() });
                Console.WriteLine(JsonSerializer.Serialize(results[^1]));
            }
            layer.ShowLayer(root.Handle); bool statePreserved = TextIs("STATE_HIDDEN-AGAIN"); int[] ids = layer.GetProcessIds(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(5)); if (!graceful) layer.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id)); power.Dispose();
            Native.GetCursorPos(out var after); bool passed = statePreserved && cleanup && results.All(result => { var value = JsonSerializer.SerializeToElement(result); return value.GetProperty("actualHidden").GetBoolean() == value.GetProperty("expectedHidden").GetBoolean() && value.GetProperty("visibilityRestored").GetBoolean() && value.GetProperty("inspectionPixels").GetBoolean() && value.GetProperty("stateAfterInspection").GetBoolean(); });
            File.WriteAllText(Path.Combine(output, "ilspy-visibility.json"), JsonSerializer.Serialize(new { passed, results, statePreserved, graceful, cleanup, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == after.X && cursor.Y == after.Y, inputBefore, inputAfter = Native.LastInputTick(), powerEvents = power.Events(), powerObserverStopped = power.ThreadStopped, note = "Same unmodified ILSpy process; public software render preference. Five-second source CPU intervals after 1.5-second settling; capture/input occur after each interval. Source window visibility toggles only on its non-input private desktop. No display wake or power policy changes. Ink is diagnostic; visually inspect saved PNGs for actual text." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { phase, error = e.ToString(), results })); throw; }
    }
}

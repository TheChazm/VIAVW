using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class ConcurrentGpuBridgeVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        Native.GetCursorPos(out var pointer); nint foreground = Native.GetForegroundWindow(); string desktop = Native.InputDesktopName();
        using var manager = new LayerManager(); using var a = manager.CreateLayer(); using var b = manager.CreateLayer();
        var bridges = new List<GpuBridgeResearchSession>(); var events = new List<object>(); var checks = new List<object>(); var allOwned = new HashSet<int>(); bool passed = true; string? failure = null; int[] owned = [];
        void Check(string name, bool value) { passed &= value; checks.Add(new { name, passed = value }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        JsonElement State(int index) => CompatibilityVerification.Read(Path.Combine(output, index.ToString()));
        try
        {
            var layers = new[] { a, b }; var roots = new long[2]; var pids = new int[2]; var clicks = new int[2];
            for (int index = 0; index < 2; index++)
            {
                string folder = Path.Combine(output, index.ToString()); Directory.CreateDirectory(folder);
                pids[index] = layers[index].LaunchApplication(Path.Combine(project, "artifacts/gpu-research-build/D3DProbe.exe"), [], folder);
                roots[index] = CompatibilityVerification.WaitRoot(layers[index], "Disposable Direct3D application").Handle; layers[index].HideLayer(roots[index]); bridges.Add(new(layers[index], roots[index]));
                if (index == 1) { layers[index].SendKey(roots[index], 0x20); clicks[index] = 1; }
            }
            Check("two independent owned private sources", a.Id != b.Id && pids[0] != pids[1] && bridges[0].MappingName != bridges[1].MappingName);
            void Frame(int index, int step)
            {
                // Both change color on every request, with opposite expected colors
                // across layers. A stale or cross-layer image therefore fails.
                layers[index].SendKey(roots[index], 0x20);
                clicks[index]++; string path = Path.Combine(output, index.ToString(), "frame-" + step + ".png"); var watch = Stopwatch.StartNew(); var info = bridges[index].Capture(path, true);
                var frame = new CaptureFrame(File.ReadAllBytes(path), 620, 440, watch.Elapsed.TotalMilliseconds); var pixels = JsonSerializer.SerializeToElement(CompatibilityVerification.PixelEvidence(frame, path, 310, 250, clicks[index] % 2 != 0));
                bool correct = pixels.GetProperty("correctMarker").GetBoolean() && State(index).GetProperty("clicks").GetInt32() == clicks[index] && !layers[index].GetWindows().First(window => window.Handle == roots[index]).Visible;
                lock (events) events.Add(new { index, step, correct, info, pixels }); if (!correct) throw new InvalidOperationException("Independent GPU pixels/state mismatch.");
            }
            for (int step = 0; step < 16; step++) { int current = step; Task.WaitAll(Task.Run(() => Frame(0, current)), Task.Run(() => Frame(1, current))); }
            Check("32 concurrent hidden shared GPU frames verified", events.Count == 32);
            a.TakeHumanControl(); bool rejected = false; try { bridges[0].Chord([0x11, 0x4B]); } catch (InvalidOperationException) { rejected = true; }
            bridges[1].Chord([0x11, 0x4B]); Check("ownership and virtual async state stay layer-local", rejected && State(0).GetProperty("shortcuts").GetInt32() == 0 && State(1).GetProperty("shortcuts").GetInt32() == 1 && State(1).GetProperty("asyncCtrlState").GetBoolean());
            foreach (int id in a.GetProcessIds()) allOwned.Add(id); bridges[0].Dispose(); bridges.RemoveAt(0); a.Dispose(); var survivor = bridges[0].Capture(Path.Combine(output, "survivor.png"), true);
            Check("closing one instrumented layer preserves the other", CompatibilityVerification.Alive(pids[1]) && b.ApplicationProcessId == pids[1] && survivor.GetProperty("deviceCreations").GetInt32() == 1);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally
        {
            foreach (var layer in new[] { a, b }.Where(layer => !layer.Closed)) foreach (int id in layer.GetProcessIds()) allOwned.Add(id);
            foreach (var bridge in bridges) bridge.Dispose(); owned = allOwned.ToArray();
            foreach (var layer in new[] { a, b }.Where(layer => !layer.Closed)) { try { layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } layer.Dispose(); } Thread.Sleep(300);
        }
        Native.GetCursorPos(out var afterPointer); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && desktopEquality);
        File.WriteAllText(Path.Combine(output, "concurrent-gpu-bridge.json"), JsonSerializer.Serialize(new { passed, failure, checks, events, owned, cleanup, foregroundEquality, pointerEquality, desktopEquality, note = "Two unchanged project D3DProbe instances only; same-bitness process-local adapters and separate shared textures. Simulated ownership; physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

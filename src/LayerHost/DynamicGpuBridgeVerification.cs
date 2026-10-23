using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace LayerHost;

internal static class DynamicGpuBridgeVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); GpuBridgeResearchSession? bridge = null;
        var checks = new List<object>(); var events = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = []; int pid = 0, lastCounter = 0; long root = 0;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        JsonElement State() => CompatibilityVerification.Read(output);
        JsonElement Wait(Func<JsonElement, bool> condition)
        {
            var watch = Stopwatch.StartNew(); do { var state = State(); if (condition(state)) return state; Thread.Sleep(10); } while (watch.ElapsedMilliseconds < 4000); throw new TimeoutException("Dynamic source state: " + State());
        }
        JsonElement Capture(string name)
        {
            var before = State(); int width = before.GetProperty("width").GetInt32(), height = before.GetProperty("height").GetInt32(), clicks = before.GetProperty("clicks").GetInt32();
            string path = Path.Combine(output, name + ".png"); var watch = Stopwatch.StartNew(); var info = bridge!.Capture(path, true); double ms = watch.Elapsed.TotalMilliseconds;
            using var image = new Bitmap(path); int counter = 0; bool cells = true;
            for (int bit = 0; bit < 16; bit++) { var cell = image.GetPixel(14 + bit * 10, image.Height - 16); bool high = cell.R > 220 && cell.G > 220 && cell.B > 220; bool low = cell.R < 25 && cell.G < 25 && cell.B < 25; cells &= high || low; if (high) counter |= 1 << bit; }
            var marker = image.GetPixel(image.Width / 2, image.Height / 2); bool orange = clicks % 2 != 0;
            bool color = orange ? marker.R > 180 && marker.G > 80 && marker.G < 190 && marker.B < 100 : marker.G > 120 && marker.R < 110 && marker.B < 150;
            var after = Wait(state => state.GetProperty("frames").GetInt32() >= counter);
            bool correct = color && cells && counter > lastCounter && counter >= before.GetProperty("frames").GetInt32() && image.Width == width && image.Height == height && info.GetProperty("width").GetInt32() == width && info.GetProperty("height").GetInt32() == height && !layer.GetWindows().First(window => window.Handle == root).Visible && layer.ApplicationProcessId == pid;
            Check(name, correct, new { counter, lastCounter, width, height, ms, info }); events.Add(new { name, correct, counter, previousCounter = lastCounter, ms, info, before, after, bridge = bridge.State }); lastCounter = counter; return info;
        }
        try
        {
            pid = layer.LaunchApplication(Path.Combine(project, "artifacts/gpu-dynamic-build/DynamicD3DProbe.exe"), [], output); root = CompatibilityVerification.WaitRoot(layer, "Disposable dynamic Direct3D application").Handle;
            Wait(state => state.GetProperty("frames").GetInt32() > 0); layer.HideLayer(root); bridge = new(layer, root, ResearchGpuTarget.DynamicProbe);
            Check("explicit fixture adapter and scoped input installed", bridge.State.GetProperty("keyAdapterInstalled").GetInt32() == 1);
            Capture("present1-cold");
            for (int i = 0; i < 4; i++) { layer.SendKey(root, 0x20); Capture("present1-warm-" + i); }
            Check("Present1 callbacks observed", bridge.State.GetProperty("present1Calls").GetInt32() > 0);
            layer.SendKey(root, 0x50); Wait(state => !state.GetProperty("present1").GetBoolean()); var legacy = Capture("legacy-present");
            Check("presentation switch reuses reader and surfaces", legacy.GetProperty("deviceCreations").GetInt32() == 1 && legacy.GetProperty("resourceOpens").GetInt32() == 1);
            (int Key, int Width, int Height)[] sizes = [(0x74, 800, 600), (0x75, 480, 320), (0x76, 1024, 576), (0x77, 620, 440)];
            JsonElement resized = default;
            for (int i = 0; i < 12; i++)
            {
                var size = sizes[i % sizes.Length]; layer.SendKey(root, 0x20); layer.SendKey(root, size.Key); Wait(state => state.GetProperty("width").GetInt32() == size.Width && state.GetProperty("height").GetInt32() == size.Height);
                if (i % 3 == 0) layer.SendKey(root, 0x50); resized = Capture("resize-" + i);
            }
            Check("resize releases back buffers and recreates only surfaces", State().GetProperty("resizeHresult").GetInt32() == 0 && bridge.State.GetProperty("resizeCalls").GetInt32() >= 12 && resized.GetProperty("resourceOpens").GetInt32() == 13 && resized.GetProperty("stagingCreations").GetInt32() == 13 && resized.GetProperty("deviceCreations").GetInt32() == 1);
            layer.SendKey(root, 0x52); Wait(state => state.GetProperty("devices").GetInt32() == 2); var recreated = Capture("device-recreated");
            Check("source device recreation preserves independent receiver device", recreated.GetProperty("resourceOpens").GetInt32() == 14 && recreated.GetProperty("deviceCreations").GetInt32() == 1);
            layer.TakeHumanControl(); bool rejected = false; try { bridge.Chord([0x11, 0x4B]); } catch (InvalidOperationException) { rejected = true; }
            bridge.Chord([0x11, 0x4B], ControlOwner.Human); Wait(state => state.GetProperty("shortcuts").GetInt32() == 1);
            Check("ownership and scoped asynchronous input survive resize", rejected && State().GetProperty("asyncCtrlState").GetBoolean() && bridge.State.GetProperty("activeKeys").GetInt32() == 0);
            layer.RestartWorker(); Capture("worker-recovered"); Check("worker recovery retains source and owner", layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human);
            bridge.RestartHelper(); Capture("reader-recovered"); layer.ReturnToAutomation(); bridge.Chord([0x11, 0x4B]); Wait(state => state.GetProperty("shortcuts").GetInt32() == 2);
            var idle = bridge.State; Thread.Sleep(1100); Check("no copies without requests", idle.GetProperty("copied").GetInt32() == bridge.State.GetProperty("copied").GetInt32());
            bridge.StopAdapter(); Check("original callbacks restored", bridge.State.GetProperty("initialized").GetInt32() == 0 && CompatibilityVerification.Alive(pid));
            int frames = State().GetProperty("frames").GetInt32(); Wait(state => state.GetProperty("frames").GetInt32() > frames);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { bridge?.Dispose(); if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); Thread.Sleep(300); }
        Native.GetCursorPos(out var afterPointer); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && desktopEquality, new { cleanup, foregroundEquality, pointerEquality, desktopEquality, graceful });
        File.WriteAllText(Path.Combine(output, "dynamic-gpu.json"), JsonSerializer.Serialize(new { passed, failure, checks, events, pid, root, owned, graceful, cleanup, foregroundEquality, pointerEquality, desktopEquality, note = "Separate disposable fixture; Present/Present1, real ResizeBuffers and voluntary device recreation. Visible GPU binary counter verifies freshness. Runtime adapter only; no driver/global input. Device recreation is not a simulated GPU removal or TDR. Human ownership is automated; physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

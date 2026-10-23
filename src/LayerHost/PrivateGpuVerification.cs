using System.Text.Json;

namespace LayerHost;

internal static class PrivateGpuVerification
{
    static object Pixels(CaptureFrame frame, string path)
    {
        File.WriteAllBytes(path, frame.Png); using var stream = new MemoryStream(frame.Png); using var image = new Bitmap(stream); int green = 0, orange = 0;
        for (int y = 0; y < image.Height; y += 2) for (int x = 0; x < image.Width; x += 2) { var p = image.GetPixel(x, y); if (p.R < 110 && p.G > 150 && p.B < 140) green++; if (p.R > 200 && p.G is > 100 and < 180 && p.B < 100) orange++; }
        return new { frame.Backend, frame.Width, frame.Height, frame.CaptureMilliseconds, greenSamples = green, orangeSamples = orange };
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); var before = Native.GetForegroundWindow(); var results = new List<object>(); bool cleanup = true, demonstrated = true;
        foreach (bool headless in new[] { false, true })
        {
            string folder = Path.Combine(output, headless ? "headless" : "headful"); Directory.CreateDirectory(folder);
            using var browser = new ChromiumLayer("C:/Program Files/Google/Chrome/Application/chrome.exe", Path.Combine(folder, "profile"), headless);
            browser.Navigate(new Uri(Path.Combine(project, "experiments/gpu-browser.html")).AbsoluteUri); Thread.Sleep(500);
            var renderer = browser.Inspect("observations.renderer"); var initial = browser.CaptureVisualOutput(); var beforePixels = Pixels(initial, Path.Combine(folder, "before-cdp.png"));
            void Click(string element, ControlOwner sender = ControlOwner.Automation) { var point = browser.Inspect($"(()=>{{let r={element}.getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2}}}})()"); browser.SendMouseInput((int)point.GetProperty("x").GetDouble(), (int)point.GetProperty("y").GetDouble(), sender); }
            Click("editor"); browser.SendKeyboardInput("PRIVATE_GPU"); Click("toggle"); Thread.Sleep(100); var after = browser.CaptureVisualOutput(); var afterPixels = Pixels(after, Path.Combine(folder, "after-cdp.png"));
            browser.TakeHumanControl(); bool rejected = false; try { browser.SendKeyboardInput("REJECT"); } catch (InvalidOperationException) { rejected = true; }
            Click("editor", ControlOwner.Human); browser.SendKey("a", "KeyA", 0x41, 2, ControlOwner.Human); browser.SendKeyboardInput("HUMAN_PRIVATE", ControlOwner.Human); browser.ReturnToAutomation();
            var state = browser.Inspect("({text:editor.value,...observations})");
            bool visual = JsonSerializer.SerializeToElement(beforePixels).GetProperty("greenSamples").GetInt32() > 15000 && JsonSerializer.SerializeToElement(afterPixels).GetProperty("orangeSamples").GetInt32() > 15000;
            var native = new List<object>(); var root = browser.NativeWindows().Where(w => w.Parent == 0 && w.Visible && w.ClassName == "Chrome_WidgetWin_1").OrderByDescending(w => w.Width * w.Height).FirstOrDefault();
            if (root != null)
            {
                browser.HideNative(root.Handle); Thread.Sleep(100); var hidden = browser.CaptureVisualOutput(); native.Add(new { phase = "SW_HIDE CDP", pixels = Pixels(hidden, Path.Combine(folder, "hidden-cdp.png")) }); browser.ShowNative(root.Handle);
                foreach (var backend in new[] { CaptureBackend.PrintWindow, CaptureBackend.PrintWindowFullContent, CaptureBackend.ReusableWindowsGraphicsCapture })
                    try { native.Add(new { backend = backend.ToString(), pixels = Pixels(browser.NativeCapture(root.Handle, backend), Path.Combine(folder, backend + ".png")) }); }
                    catch (Exception e) { native.Add(new { backend = backend.ToString(), error = e.Message }); }
                var nativePointer = browser.NativePointer(root.Handle, 100, 193); browser.NativeChord(root.Handle, [0x11, 0x41]); browser.NativeText(root.Handle, "NATIVE_PRIVATE_TEXT"); Thread.Sleep(150);
                var nativeState = browser.Inspect("({text:editor.value,...observations})"); native.Add(new { phase = "native pointer/chord/text", nativePointer, state = nativeState });
                File.WriteAllText(Path.Combine(folder, "native-input.json"), JsonSerializer.Serialize(nativeState, new JsonSerializerOptions { WriteIndented = true }));
                native.Add(new { phase = "native pixels after native input", pixels = Pixels(browser.NativeCapture(root.Handle, CaptureBackend.PrintWindowFullContent), Path.Combine(folder, "native-input.png")) });
            }
            int pid = browser.ApplicationProcessId; var ids = browser.GetProcessIds(); bool hardware = renderer.GetString()?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true;
            bool correctState = state.GetProperty("text").GetString() == "HUMAN_PRIVATE" && state.GetProperty("changes").GetInt32() == 1 && rejected && hardware && visual;
            demonstrated &= correctState; browser.Dispose(); Thread.Sleep(250); bool cleaned = ids.All(id => !CompatibilityVerification.Alive(id)); cleanup &= cleaned;
            results.Add(new { headless, pid, privateDesktop = browser.Id, renderer, beforePixels, afterPixels, state, rejected, correctState, native, cleanup = cleaned });
            Console.WriteLine($"Private Chrome {(headless ? "headless" : "headful")}: hardware={hardware}; input/state={correctState}; cleanup={cleaned}");
        }
        using var manager = new LayerManager();
        foreach (string name in new[] { "WPF", "Quick", "D3D" })
        {
            string folder = Path.Combine(output, name); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer();
            string exe = name == "WPF" ? Path.Combine(project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe") : name == "Quick" ? "C:/Python314/python.exe" : Path.Combine(project, "artifacts/native-build/D3DProbe.exe");
            layer.LaunchApplication(exe, name == "Quick" ? [Path.Combine(project, "experiments/quick_probe.py")] : [], folder);
            var root = CompatibilityVerification.WaitRoot(layer, name == "WPF" ? "Disposable WPF application" : name == "Quick" ? "Disposable Qt Quick application" : "Disposable Direct3D application"); var captures = new List<object>();
            layer.SendKeyboardInput(root.Handle, "PRIVATE_NATIVE_GPU");
            var first = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent); captures.Add(new { phase = "before", pixels = Pixels(first, Path.Combine(folder, "before-native.png")) });
            if (name == "WPF")
            {
                var primed = layer.SendQueueMouseSequence(root.Handle, [new(MouseMessage.Move, 80, 150), new(MouseMessage.LeftDown, 80, 150, 1), new(MouseMessage.LeftUp, 80, 150)], true);
                Thread.Sleep(200); var activated = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent);
                captures.Add(new { phase = "after private focus/capture activation", primed, pixels = Pixels(activated, Path.Combine(folder, "after-activation.png")), state = CompatibilityVerification.Read(folder) });
            }
            if (name != "WPF")
            {
                layer.SendMouseInput(root.Handle, name == "Quick" ? 80 : 310, name == "Quick" ? 150 : 250); Thread.Sleep(100);
                var changed = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent); captures.Add(new { phase = "after", pixels = Pixels(changed, Path.Combine(folder, "after-native.png")), state = CompatibilityVerification.Read(folder) });
                using var changedStream = new MemoryStream(changed.Png); using var image = new Bitmap(changedStream); var p = image.GetPixel(name == "Quick" ? 490 : 310, name == "Quick" ? 160 : 250); demonstrated &= p.R > 200 && p.G is > 100 and < 180 && p.B < 100;
            }
            foreach (var backend in new[] { CaptureBackend.PrintWindowFullContent, CaptureBackend.ReusableWindowsGraphicsCapture })
                try { captures.Add(new { backend = backend.ToString(), pixels = Pixels(layer.CaptureVisualOutput(root.Handle, backend), Path.Combine(folder, backend + ".png")) }); }
                catch (Exception e) { captures.Add(new { backend = backend.ToString(), error = e.Message, appRetained = CompatibilityVerification.Alive(layer.ApplicationProcessId) }); }
            if (name != "WPF")
            {
                layer.HideLayer(root.Handle); layer.SendKeyboardInput(root.Handle, "_HIDDEN"); Thread.Sleep(200);
                captures.Add(new { phase = "hidden after input", pixels = Pixels(layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent), Path.Combine(folder, "hidden-native.png")), state = CompatibilityVerification.Read(folder) }); layer.ShowLayer(root.Handle);
            }
            var ids = layer.GetProcessIds(); layer.Dispose(); Thread.Sleep(150); bool cleaned = ids.All(id => !CompatibilityVerification.Alive(id)); cleanup &= cleaned; results.Add(new { name, captures, cleanup = cleaned });
        }
        File.WriteAllText(Path.Combine(output, "private-gpu.json"), JsonSerializer.Serialize(new { demonstrated, cleanup, results, foregroundEquality = Native.GetForegroundWindow() == before, note = "Unmodified Chrome on private desktops; GPU pixels through explicit browser capture/input adapter. Native alternatives tested separately. Fixtures use ordinary WPF/Qt/D3D rendering; no cooperative grab enabled. API return is not pixel proof." }, new JsonSerializerOptions { WriteIndented = true }));
        return demonstrated && cleanup ? 0 : 1;
    }
}

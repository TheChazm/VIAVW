using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class FirefoxNativeVerification
{
    internal static int Run(string output, bool software = false)
    {
        Directory.CreateDirectory(output); string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string profile = Path.Combine(output, "profile"); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "user.js"), "user_pref(\"browser.shell.checkDefaultBrowser\", false);\nuser_pref(\"browser.aboutwelcome.enabled\", false);\nuser_pref(\"browser.startup.homepage_override.mstone\", \"ignore\");\nuser_pref(\"browser.tabs.warnOnClose\", false);\nuser_pref(\"network.captive-portal-service.enabled\", false);\nuser_pref(\"datareporting.policy.dataSubmissionEnabled\", false);\n");
        if (software) File.AppendAllText(Path.Combine(profile, "user.js"), "user_pref(\"gfx.webrender.software\", true);\nuser_pref(\"gfx.webrender.software.d3d11\", false);\n");
        string page = File.ReadAllText(Path.Combine(project, "experiments/gpu-browser.html")).Replace("Ordinary Chrome", "Ordinary Firefox");
        page = page.Replace("const gl=surface.getContext('webgl');", "let contextError='';surface.addEventListener('webglcontextcreationerror',e=>contextError=e.statusMessage);const gl=surface.getContext('webgl');");
        page += "<script>setInterval(()=>{const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}};fetch('/observed',{method:'POST',body:JSON.stringify({text:editor.value,innerHeight,contextError,editor:rect(editor),toggle:rect(toggle),...observations,events:observations.events.slice(-64)})}).catch(()=>{})},200)</script>";
        using var server = new PassivePageServer(page); using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); var stages = new List<object>(); bool passed = false; string? failure = null;
        JsonElement Wait(Func<JsonElement, bool> condition, int timeout = 5000)
        {
            var watch = Stopwatch.StartNew(); JsonElement? value = null;
            while (watch.ElapsedMilliseconds < timeout) { value = server.Observation; if (value != null && condition(value.Value)) return value.Value; Thread.Sleep(50); }
            throw new TimeoutException("Passive page observation deadline; last observation: " + value);
        }
        try
        {
            layer.LaunchApplication("C:/Program Files/Mozilla Firefox/firefox.exe", ["--no-remote", "--profile", profile, "--new-window", server.Url, "--width", "800", "--height", "660"], output, new() { ["MOZ_CRASHREPORTER_DISABLE"] = "1" });
            var initial = Wait(v => v.TryGetProperty("renderer", out _), 20000);
            Thread.Sleep(1500);
            var root = layer.GetWindows().Where(w => w.Parent == 0 && w.Visible && w.ClassName == "MozillaWindowClass").OrderByDescending(w => w.Width * w.Height).First();
            var native = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent);
            stages.Add(new { stage = "initial", state = initial, pixels = WpfCaptureResearch.Pixels(native, Path.Combine(output, "initial.png")), root });
            void Click(string target, ControlOwner sender = ControlOwner.Automation)
            {
                var state = server.Observation!.Value; var point = state.GetProperty(target); int x = (int)point.GetProperty("x").GetDouble(), y = (int)point.GetProperty("y").GetDouble() + native.Height - state.GetProperty("innerHeight").GetInt32();
                var hit = layer.HitTest(root.Handle, x, y);
                layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y)], true, sender);
            }
            Click("editor"); layer.SendKeyboardInput(root.Handle, "FIREFOX_NATIVE_INPUT"); var typed = Wait(v => v.GetProperty("text").GetString() == "FIREFOX_NATIVE_INPUT"); stages.Add(new { stage = "typed", state = typed });
            Click("toggle"); var changed = Wait(v => v.GetProperty("changes").GetInt32() == 1);
            var frame = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent); var pixels = WpfCaptureResearch.Pixels(frame, Path.Combine(output, "changed.png")); stages.Add(new { stage = "changed", state = changed, pixels });
            Click("editor"); layer.SendQueueStateChord(root.Handle, [0x11, 0x41]); layer.SendKeyboardInput(root.Handle, "FIREFOX_REPLACED"); var replaced = Wait(v => v.GetProperty("text").GetString() == "FIREFOX_REPLACED"); stages.Add(new { stage = "replaced", state = replaced });
            layer.TakeHumanControl(); bool rejected = false; try { layer.SendKeyboardInput(root.Handle, "REJECT"); } catch (InvalidOperationException) { rejected = true; }
            Click("editor", ControlOwner.Human); layer.SendKeyboardInput(root.Handle, "_HUMAN", ControlOwner.Human); var human = Wait(v => v.GetProperty("text").GetString() == "FIREFOX_REPLACED_HUMAN");
            layer.ReturnToAutomation(); layer.SendKeyboardInput(root.Handle, "_AUTO"); var resumed = Wait(v => v.GetProperty("text").GetString() == "FIREFOX_REPLACED_HUMAN_AUTO");
            stages.Add(new { stage = "handoff", rejected, human, resumed });
            passed = rejected && JsonSerializer.SerializeToElement(pixels).GetProperty("orange").GetInt32() > 10000;
        }
        catch (Exception e)
        {
            failure = e.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure);
            try
            {
                var windows = layer.GetWindows(); File.WriteAllText(Path.Combine(output, "windows-after-failure.json"), JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true }));
                var root = windows.Where(w => w.Parent == 0 && w.Visible && w.ClassName == "MozillaWindowClass").OrderByDescending(w => w.Width * w.Height).First();
                stages.Add(new { stage = "after failure", pixels = WpfCaptureResearch.Pixels(layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent), Path.Combine(output, "after-failure.png")) });
            }
            catch (Exception diagnostic) { File.WriteAllText(Path.Combine(output, "diagnostic-failure.txt"), diagnostic.ToString()); }
        }
        var latest = server.Observation; if (latest != null) File.WriteAllText(Path.Combine(output, "last-observed.json"), JsonSerializer.Serialize(latest.Value, new JsonSerializerOptions { WriteIndented = true }));
        int[] ids = layer.GetProcessIds(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); if (!graceful) layer.Dispose(); Thread.Sleep(250); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
        Native.GetCursorPos(out var afterCursor);
        File.WriteAllText(Path.Combine(output, "firefox-native.json"), JsonSerializer.Serialize(new { passed, software, stages, failure, graceful, cleanup, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == afterCursor.X && cursor.Y == afterCursor.Y, note = "Installed unmodified Firefox, unique disposable profile on a private desktop. Native Windows input and native PrintWindow pixels. Local test page sends passive observations; no browser control/debug protocol, extension, input adapter or pixel export. Software configuration, if selected, changes only this disposable profile. Physical user acceptance pending." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, passed, failure, graceful, cleanup })); return passed && cleanup ? 0 : 1;
    }
}

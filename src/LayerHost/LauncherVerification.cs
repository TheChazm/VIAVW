using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class LauncherVerification
{
    static string Project => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    static WindowInfo? Window(int pid, bool visibleOnly = false)
    {
        WindowInfo? result = null;
        Native.EnumProc callback = (hwnd, _) => { Native.GetWindowThreadProcessId(hwnd, out uint owner); if (owner == pid && Native.WindowText(hwnd) == "VIAVW — private application layer" && (!visibleOnly || Native.IsWindowVisible(hwnd))) result = new((long)hwnd, 0, pid, Native.ClassName(hwnd), "viewer", Native.IsWindowVisible(hwnd), true, 0, 0, 0, 0); return true; };
        Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), callback, 0); return result;
    }
    static (int Exit, string Output, string Error) Script(string name, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Project };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File"); start.ArgumentList.Add(Path.Combine(Project, "scripts", name)); foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Owned launcher script exceeded deadline."); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
    static JsonElement State(string file) { using var doc = JsonDocument.Parse(File.ReadAllText(file)); return doc.RootElement.Clone(); }
    static bool Close(int pid, int[] children)
    {
        var window = Window(pid); if (window != null) Native.Check(Native.PostMessageW((nint)window.Handle, 0x10, 0, 0), "Close owned viewer");
        var watch = Stopwatch.StartNew(); while (CompatibilityVerification.Alive(pid) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(50);
        return !CompatibilityVerification.Alive(pid) && children.All(p => !CompatibilityVerification.Alive(p));
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var results = new List<object>();
        string status = Path.Combine(output, "visible-session.json");
        var launched = Script("demo.ps1", "-SkipBuild", "-StatusFile", status);
        results.Add(new { operation = "default script startup", exit = launched.Exit, launched.Output, launched.Error });
        if (launched.Exit != 0) { File.WriteAllText(Path.Combine(output, "launcher.json"), JsonSerializer.Serialize(results)); return 1; }
        var state = State(status); int pid = state.GetProperty("pid").GetInt32(); int[] children = state.GetProperty("ownedProcessIds").Deserialize<int[]>()!;
        bool visible = state.GetProperty("visible").GetBoolean() && Window(pid, true) != null;
        results.Add(new { operation = "visible default viewer", passed = visible, state });
        var duplicate = Script("demo.ps1", "-SkipBuild", "-StatusFile", Path.Combine(output, "duplicate.json"));
        results.Add(new { operation = "duplicate refused without attaching or terminating", passed = duplicate.Exit != 0 && duplicate.Error.Contains("already running") && CompatibilityVerification.Alive(pid), duplicate.Error });
        bool cleanup = Close(pid, children); results.Add(new { operation = "normal close cleans owned tree", passed = cleanup, children });
        string browserStatus = Path.Combine(output, "private-browser-session.json");
        var browser = Script("private-browser-demo.ps1", "-SkipBuild", "-StatusFile", browserStatus);
        results.Add(new { operation = "private browser script startup without developer port", passed = browser.Exit == 0, browser.Output, browser.Error });
        if (browser.Exit == 0)
        {
            var bs = State(browserStatus); int bp = bs.GetProperty("pid").GetInt32(); var bc = bs.GetProperty("ownedProcessIds").Deserialize<int[]>()!;
            results.Add(new { operation = "private browser viewer ready and visible", passed = bs.GetProperty("visible").GetBoolean() && Window(bp, true) != null, bs });
            results.Add(new { operation = "private browser normal close cleanup", passed = Close(bp, bc) });
        }
        string hookStatus = Path.Combine(output, "wpf-hook-session.json"); var hook = Script("wpf-hook-demo.ps1", "-SkipBuild", "-StatusFile", hookStatus);
        results.Add(new { operation = "unchanged WPF script startup with opt-in render adapter", passed = hook.Exit == 0, hook.Output, hook.Error });
        if (hook.Exit == 0) { var hs = State(hookStatus); int hp = hs.GetProperty("pid").GetInt32(); var hc = hs.GetProperty("ownedProcessIds").Deserialize<int[]>()!; results.Add(new { operation = "WPF hook viewer visible", passed = hs.GetProperty("visible").GetBoolean() && Window(hp, true) != null }); results.Add(new { operation = "WPF hook viewer normal cleanup", passed = Close(hp, hc) }); }
        if (File.Exists(IlSpyVerification.Executable))
        {
            string ilspyStatus = Path.Combine(output, "ilspy-session.json"); var ilspy = Script("ilspy-demo.ps1", "-SkipBuild", "-StatusFile", ilspyStatus);
            results.Add(new { operation = "ordinary ILSpy script startup with isolated config and render adapter", passed = ilspy.Exit == 0, ilspy.Output, ilspy.Error });
            if (ilspy.Exit == 0) { var stateIlSpy = State(ilspyStatus); int ip = stateIlSpy.GetProperty("pid").GetInt32(); var childrenIlSpy = stateIlSpy.GetProperty("ownedProcessIds").Deserialize<int[]>()!; results.Add(new { operation = "ILSpy viewer visible", passed = stateIlSpy.GetProperty("visible").GetBoolean() && Window(ip, true) != null }); results.Add(new { operation = "ILSpy normal viewer close cleanup", passed = Close(ip, childrenIlSpy) }); }
        }
        string hiddenStatus = Path.Combine(output, "hidden-session.json"); var hidden = Script("demo.ps1", "-SkipBuild", "-Hidden", "-StatusFile", hiddenStatus);
        if (hidden.Exit == 0)
        {
            var hs = State(hiddenStatus); int hp = hs.GetProperty("pid").GetInt32(); var hc = hs.GetProperty("ownedProcessIds").Deserialize<int[]>()!;
            results.Add(new { operation = "explicit hidden startup", passed = !hs.GetProperty("visible").GetBoolean() && Window(hp)?.Visible == false, hs });
            var shown = Script("show-demo.ps1", "-SessionFile", hiddenStatus); var watch = Stopwatch.StartNew(); while (Window(hp, true) == null && watch.ElapsedMilliseconds < 2000) Thread.Sleep(50);
            results.Add(new { operation = "reveal same hidden controller", passed = shown.Exit == 0 && Window(hp, true) != null, samePid = hp });
            results.Add(new { operation = "revealed viewer cleanup", passed = Close(hp, hc) });
        }
        else results.Add(new { operation = "hidden startup", passed = false, hidden.Error });
        var invalid = Script("demo.ps1", "-SkipBuild", "-Executable", Path.Combine(Project, "artifacts", "not-an-executable.exe"));
        results.Add(new { operation = "clear missing-executable error", passed = invalid.Exit != 0 && invalid.Error.Contains("missing"), invalid.Error });
        // An intentionally stuck startup capture proves UI responsiveness and startup cleanup.
        string failedStatus = Path.Combine(output, "failed-capture-session.json");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = output };
        foreach (var arg in new[] { "--demo", "--ready-file", failedStatus, "--hidden", Program.FindProbe(), "--hang-print" }) start.ArgumentList.Add(arg);
        using (var host = Process.Start(start)!)
        {
            var latencies = new List<double>(); var watch = Stopwatch.StartNew();
            while (!host.HasExited && watch.ElapsedMilliseconds < 12000)
            {
                var hwnd = Window(host.Id);
                if (hwnd != null) { var ping = Stopwatch.StartNew(); bool ok = Native.SendMessageTimeoutW((nint)hwnd.Handle, 0, 0, 0, 2, 200, out _) != 0; latencies.Add(ok ? ping.Elapsed.TotalMilliseconds : 200); }
                Thread.Sleep(100);
            }
            if (!host.HasExited) host.Kill();
            var fs = State(failedStatus);
            results.Add(new { operation = "hung startup capture leaves responsive UI then closes", passed = fs.GetProperty("state").GetString() == "error" && host.HasExited && latencies.Count > 5 && latencies.Max() < 100, pingSamples = latencies.Count, maxUiPingMs = latencies.Count > 0 ? latencies.Max() : 0, medianUiPingMs = latencies.Count > 0 ? latencies.Order().ElementAt(latencies.Count / 2) : 0, status = fs });
        }
        var serialized = JsonSerializer.SerializeToElement(results);
        bool passed = serialized.EnumerateArray().Where(v => v.TryGetProperty("passed", out _)).All(v => v.GetProperty("passed").GetBoolean());
        File.WriteAllText(Path.Combine(output, "launcher.json"), JsonSerializer.Serialize(new { passed, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, passed })); return passed ? 0 : 1;
    }
    internal static int SaveOpportunity(string output)
    {
        Directory.CreateDirectory(output); using var manager = new LayerManager(); var layer = manager.CreateLayer();
        string exe = Path.Combine(Project, "src/WpfProbe/bin/Release/net10.0-windows/WpfProbe.exe");
        int pid = layer.LaunchApplication(exe, ["--confirm-close"], output);
        var root = CompatibilityVerification.WaitRoot(layer, "Disposable WPF application"); layer.SendKeyboardInput(root.Handle, "M2_SAVE_DOCUMENT"); Thread.Sleep(300);
        bool closedEarly = layer.CloseGracefully(TimeSpan.FromMilliseconds(300));
        var popup = layer.GetWindows().FirstOrDefault(w => w.Text == "WPF save opportunity");
        bool preserved = !closedEarly && CompatibilityVerification.Alive(pid) && layer.Owner == ControlOwner.Human && popup != null;
        if (popup != null) layer.PostKey(popup.Handle, 13, ControlOwner.Human);
        bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3));
        string saved = Path.Combine(output, "saved-document.txt"); bool correct = File.Exists(saved) && File.ReadAllText(saved) == "M2_SAVE_DOCUMENT";
        var result = new { preservedAtSavePrompt = preserved, graceful, savedCorrectDocument = correct, pid, appExited = !CompatibilityVerification.Alive(pid) };
        File.WriteAllText(Path.Combine(output, "save-opportunity.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(result)); return preserved && graceful && correct ? 0 : 1;
    }
}

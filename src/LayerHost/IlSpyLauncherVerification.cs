using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class IlSpyLauncherVerification
{
    internal static int Run(string output, bool trace = false, bool pair = false)
    {
        Directory.CreateDirectory(output); var foreground = Native.GetForegroundWindow(); using var manager = new LayerManager(); using var qa = manager.CreateLayer();
        int pid = qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), [pair ? "--run-ilspy-pair-launcher" : "--run-ilspy-launcher", output], output, trace ? new() { ["VIAVW_WPF_DIAGNOSTIC_TRACE"] = "1" } : null);
        var watch = Stopwatch.StartNew(); while (CompatibilityVerification.Alive(pid) && watch.ElapsedMilliseconds < (pair ? 75000 : 45000)) Thread.Sleep(100);
        bool finished = !CompatibilityVerification.Alive(pid); int[] ids = qa.GetProcessIds(); qa.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
        bool passed = false; string result = Path.Combine(output, "ilspy-launcher.json"); if (File.Exists(result)) { using var doc = JsonDocument.Parse(File.ReadAllText(result)); passed = doc.RootElement.GetProperty("passed").GetBoolean(); }
        File.WriteAllText(Path.Combine(output, "private-ilspy-launcher.json"), JsonSerializer.Serialize(new { passed, finished, cleanup, foregroundEquality = foreground == Native.GetForegroundWindow() }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, finished, cleanup })); return passed && finished && cleanup ? 0 : 1;
    }
    internal static int Script(string output, bool pair = false)
    {
        if (!Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())).StartsWith("VIAVW_", StringComparison.Ordinal)) throw new InvalidOperationException("Launcher verification must run on its private QA desktop.");
        var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = IlSpyVerification.Project };
        string status = Path.Combine(output, "session.json");
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(IlSpyVerification.Project, pair ? "scripts/ilspy-pair-demo.ps1" : "scripts/ilspy-demo.ps1"), "-SkipBuild", pair ? "-GroupFile" : "-StatusFile", status }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(pair ? 55000 : 30000)) { process.Kill(); throw new TimeoutException("Owned ILSpy launcher script deadline."); }
        bool passed = process.ExitCode == 0, visible = false, graceful = false; JsonElement? state = null;
        if (File.Exists(status)) { using var doc = JsonDocument.Parse(File.ReadAllText(status)); state = doc.RootElement.Clone(); }
        var results = new List<object>();
        if (passed && state != null)
        {
            var sessions = new List<JsonElement>();
            if (pair) { foreach (var file in state.Value.GetProperty("sessions").EnumerateArray()) { using var session = JsonDocument.Parse(File.ReadAllText(file.GetString()!)); sessions.Add(session.RootElement.Clone()); } }
            else sessions.Add(state.Value);
            for (int index = 0; index < sessions.Count; index++)
            {
            int viewerPid = sessions[index].GetProperty("pid").GetInt32(); int[] children = sessions[index].GetProperty("ownedProcessIds").Deserialize<int[]>()!; nint window = 0;
            Native.EnumProc find = (hwnd, _) => { Native.GetWindowThreadProcessId(hwnd, out uint id); if (id == viewerPid && Native.WindowText(hwnd) == "VIAVW — private application layer") window = hwnd; return true; };
            Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), find, 0); visible = window != 0 && Native.IsWindowVisible(window);
            if (window != 0) Native.PostMessageW(window, 0x10, 0, 0);
            var until = Stopwatch.StartNew(); while (CompatibilityVerification.Alive(viewerPid) && until.ElapsedMilliseconds < 10000) Thread.Sleep(50);
            graceful = !CompatibilityVerification.Alive(viewerPid) && children.All(id => !CompatibilityVerification.Alive(id));
            bool peerPreserved = sessions.Skip(index + 1).All(session => CompatibilityVerification.Alive(session.GetProperty("pid").GetInt32()) && session.GetProperty("ownedProcessIds").EnumerateArray().All(id => CompatibilityVerification.Alive(id.GetInt32())));
            passed &= visible && graceful && peerPreserved; results.Add(new { index, viewerPid, visible, graceful, peerPreserved, session = sessions[index] });
            }
        }
        File.WriteAllText(Path.Combine(output, "ilspy-launcher.json"), JsonSerializer.Serialize(new { passed, pair, visible, graceful, results, process.ExitCode, stdout = stdout.GetAwaiter().GetResult(), stderr = stderr.GetAwaiter().GetResult(), state }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

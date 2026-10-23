using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class GpuBridgeLauncherVerification
{
    sealed record LaunchResult(int Exit, string Output, string Error);
    internal static int Run(string output, bool qiv = false, bool nativeStartup = false)
    {
        GpuBridgeVerification.PrepareOutput(output); nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName(); uint? inputBefore = Native.LastInputTick(); var clipBefore = QivResearch.CursorClip();
        using var manager = new LayerManager(); using var qa = manager.CreateLayer();
        int pid = qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), [nativeStartup?"--run-qiv-native-launcher":qiv ? "--run-qiv-viewer-launcher" : "--run-mpv-viewer-launcher", output], output);
        var seenOwned = new HashSet<int>(); var watch = Stopwatch.StartNew();
        while (CompatibilityVerification.Alive(pid) && watch.ElapsedMilliseconds < 65000) { foreach (int id in qa.GetProcessIds()) seenOwned.Add(id); Thread.Sleep(100); }
        bool finished = !CompatibilityVerification.Alive(pid); foreach (int id in qa.GetProcessIds()) seenOwned.Add(id); int[] owned = seenOwned.ToArray(); qa.Dispose(); Thread.Sleep(200);
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), passed = false;
        if (File.Exists(Path.Combine(output, "launcher.json"))) { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "launcher.json"))); passed = doc.RootElement.GetProperty("passed").GetBoolean(); }
        Native.GetCursorPos(out var after); uint? inputAfter = Native.LastInputTick(); var clipAfter = QivResearch.CursorClip(); Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
        bool focusEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == after.X && pointer.Y == after.Y, desktopEquality = desktop == Native.InputDesktopName(), clipEquality = clipBefore.Equals(clipAfter), quiet = inputBefore != null && inputBefore == inputAfter;
        bool foregroundOutsideLayer = !owned.Contains((int)foregroundPid), physical = desktopEquality && clipEquality && foregroundOutsideLayer && (!quiet || (focusEquality && pointerEquality));
        File.WriteAllText(Path.Combine(output, "private-launcher.json"), JsonSerializer.Serialize(new { passed, finished, cleanup, physical, owned, focusEquality, pointerEquality, desktopEquality, clipEquality, foregroundOutsideLayer, inputBefore, inputAfter, quiet, note = "Whole-run pointer/focus equality is reported independently when external input activity occurs; its origin is not inferred. Owned jobs remain on private desktops." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { output, passed, finished, cleanup, physical })); return passed && finished && cleanup && physical ? 0 : 1;
    }
    internal static int Script(string output, bool qiv = false, bool nativeStartup = false)
    {
        if (!Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())).StartsWith("VIAVW_", StringComparison.Ordinal)) throw new InvalidOperationException("Launcher checks require a private QA desktop.");
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); var checks = new List<object>(); bool passed = true;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); }
        JsonElement State(string folder) { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, folder, "session.json"))); return doc.RootElement.Clone(); }
        LaunchResult Launch(string folder)
        {
            var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = project };
            foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(project, qiv ? "scripts/demo-qiv.ps1" : "scripts/demo-mpv.ps1"), "-SkipBuild", qiv ? "-StatusFile" : "-DemoDirectory", qiv ? Path.Combine(output, folder, "session.json") : Path.Combine(output, folder) }) start.ArgumentList.Add(argument);
            if(nativeStartup)start.ArgumentList.Add("-NativeStartup");
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Owned mpv launcher script exceeded deadline."); }
            return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        nint Window(int pid)
        {
            nint found = 0; Native.EnumProc find = (window, _) => { Native.GetWindowThreadProcessId(window, out uint owner); if (owner == pid && Native.WindowText(window) == "VIAVW — private application layer") found = window; return true; };
            Native.Check(Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), find, 0), "Find owned QA viewer"); return found;
        }
        bool Closed(int pid, int[] children)
        {
            var watch = Stopwatch.StartNew(); while (CompatibilityVerification.Alive(pid) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(50);
            return !CompatibilityVerification.Alive(pid) && children.All(id => !CompatibilityVerification.Alive(id));
        }
        string? failure = null;
        try
        {
            var launch = Launch("session"); Check(qiv ? "real portable qIV launcher" : "real portable mpv launcher", launch.Exit == 0, launch);
            if (launch.Exit != 0) throw new InvalidOperationException(launch.Error);
            var state = State("session"); int pid = state.GetProperty("pid").GetInt32(); int[] children = state.GetProperty("ownedProcessIds").Deserialize<int[]>()!;
            nint viewer = Window(pid); Check("launcher opens a visible viewer", state.GetProperty("visible").GetBoolean() && viewer != 0 && Native.IsWindowVisible(viewer));
            var duplicate = Launch("duplicate"); Check("duplicate refused while original survives", duplicate.Exit != 0 && duplicate.Error.Contains("already running") && CompatibilityVerification.Alive(pid), duplicate);
            Native.Check(Native.PostMessageW(viewer, 0x10, 0, 0), "Gracefully close owned GPU viewer"); Check("real launcher graceful cleanup", Closed(pid, children));
            var forced = Launch("force"); Check("launcher can reopen after cleanup", forced.Exit == 0, forced);
            if (forced.Exit != 0) throw new InvalidOperationException(forced.Error);
            var second = State("force"); pid = second.GetProperty("pid").GetInt32(); children = second.GetProperty("ownedProcessIds").Deserialize<int[]>()!; viewer = Window(pid);
            nint forceButton = 0; Native.EnumProc button = (window, _) => { if (Native.WindowText(window) == "Force close") forceButton = window; return true; }; Native.EnumChildWindows(viewer, button, 0);
            if (forceButton == 0) throw new InvalidOperationException("Owned GPU viewer Force close button not found.");
            Native.Message(forceButton, Native.WM_LBUTTONDOWN, 1, 10 | (10 << 16)); Native.Message(forceButton, Native.WM_LBUTTONUP, 0, 10 | (10 << 16)); Check("GPU viewer force-close cleanup", Closed(pid, children));
            string invalid = Path.Combine(output, "rejected.json");
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = output };
            foreach (string argument in new[] { "--demo", "--ready-file", invalid, "--experimental-gpu-bridge", qiv ? "qiv" : "mpv", Program.FindProbe() }) start.ArgumentList.Add(argument);
            using var rejected = Process.Start(start)!;
            if (!rejected.WaitForExit(15000)) { rejected.Kill(); throw new TimeoutException("Invalid GPU target startup exceeded deadline."); }
            using var rejection = JsonDocument.Parse(SnapshotFiles.ReadText(invalid));
            var refusal = rejection.RootElement.Clone();
            Check("arbitrary executable rejected by GPU target guard", rejected.ExitCode == 1 && refusal.GetProperty("state").GetString() == "error" && refusal.GetProperty("error").GetString()!.Contains(qiv ? "Use the pinned qIV" : "restricted to the explicitly selected"), refusal);
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); }
        File.WriteAllText(Path.Combine(output, "launcher.json"), JsonSerializer.Serialize(new { passed, failure, checks, note = "Real launcher and actual close handlers tested entirely on a private QA desktop. No visible foreground controller or physical input; physical acceptance pending." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

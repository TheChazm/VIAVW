using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class WorkerInfrastructureVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); var results = new List<object>(); bool passed = true; using var manager = new LayerManager();
        for (int trial = 0; trial < 3; trial++)
        {
            string folder = Path.Combine(output, trial.ToString()); Directory.CreateDirectory(folder); using var layer = manager.CreateLayer();
            int source = layer.LaunchApplication(Program.FindProbe(), ["--spawn-on-request"], folder); var root = CompatibilityVerification.WaitRoot(layer, "Disposable application");layer.EnablePrivateInputSession(); var edit = layer.GetWindows().First(window => window.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase)); layer.SendKeyboardInput(edit.Handle, "RESTART_STATE"); layer.TakeHumanControl();
            int[] beforeMembers = layer.GetProcessIds(); DateTime start = DateTime.UtcNow; var timer = Stopwatch.StartNew(); var restart = Task.Run(layer.RestartWorker); Thread.Sleep(20); File.WriteAllText(Path.Combine(folder, "spawn-child.request"), "Owned fixture may spawn a disposable helper."); restart.GetAwaiter().GetResult(); DateTime end = DateTime.UtcNow; double restartMs = timer.Elapsed.TotalMilliseconds; int[] afterMembers = layer.GetProcessIds();
            string childFile = Path.Combine(folder, "spawned-child.json"); var until = Stopwatch.StartNew(); while (!File.Exists(childFile) && until.ElapsedMilliseconds < 4000) Thread.Sleep(30); using var report = JsonDocument.Parse(SnapshotFiles.ReadText(childFile)); int child = report.RootElement.GetProperty("pid").GetInt32();
            var identities = ProcessCensus.Read(layer.GetProcessIds()); var childIdentity = identities.First(identity => identity.Pid == child); bool overlapped = childIdentity.CreationFileTime >= start.ToFileTimeUtc() && childIdentity.CreationFileTime <= end.ToFileTimeUtc();
            int[] infrastructure = layer.GetInfrastructureProcessIds(); bool childIsApplication = !infrastructure.Contains(child), statePreserved = layer.ApplicationProcessId == source && layer.Owner == ControlOwner.Human && layer.GetWindows().First(window => window.Handle == edit.Handle).Text == "RESTART_STATE";
            bool sessionRecovered=layer.PrivateInputSessionState().GetProperty("Enabled").GetBoolean();
            layer.PostKeyboardInput(edit.Handle,"!",ControlOwner.Human);var resumed=Stopwatch.StartNew();while(layer.GetWindows().First(w=>w.Handle==edit.Handle).Text!="RESTART_STATE!"&&resumed.ElapsedMilliseconds<3000)Thread.Sleep(20);
            bool inputResumed=layer.GetWindows().First(w=>w.Handle==edit.Handle).Text=="RESTART_STATE!";
            bool closed = layer.CloseGracefully(TimeSpan.FromMilliseconds(500)); bool preservedHelper = !closed && !layer.Closed && CompatibilityVerification.Alive(child); int[] ids = layer.GetProcessIds(); layer.Dispose(); Thread.Sleep(200); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
            bool correct = childIsApplication && statePreserved && sessionRecovered&&inputResumed&&preservedHelper && cleanup; passed &= correct;
            results.Add(new { trial, source, child, restartMs, overlapped, naiveNewPidRuleWouldClassifyChildAsInfrastructure = afterMembers.Except(beforeMembers).Contains(child), childIsApplication, statePreserved, sessionRecovered,inputResumed,preservedHelper, cleanup, correct, identities, infrastructure }); Console.WriteLine(JsonSerializer.Serialize(new { trial, overlapped, childIsApplication, statePreserved,sessionRecovered,inputResumed, preservedHelper, cleanup, correct }));
        }
        Native.GetCursorPos(out var after); bool coverage = results.Any(result => JsonSerializer.SerializeToElement(result).GetProperty("overlapped").GetBoolean()); passed &= coverage;
        File.WriteAllText(Path.Combine(output, "worker-infrastructure.json"), JsonSerializer.Serialize(new { passed, coverage, results, foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = cursor.X == after.X && cursor.Y == after.Y, note = "Disposable fixture autonomously spawns a real job-owned helper during worker restart. Helper must remain application-owned, preserve the pending graceful close and require explicit disposal. Source PID/document/owner remain unchanged. Parent/creation/image census is read-only." }, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}

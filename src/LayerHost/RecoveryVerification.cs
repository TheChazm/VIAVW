using System.Text.Json;

namespace LayerHost;

internal static class RecoveryVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); using var manager = new LayerManager(); var layer = manager.CreateLayer();
        int pid = layer.LaunchApplication(Program.FindProbe(), workingDirectory: output);
        var root = CompatibilityVerification.WaitRoot(layer, "Disposable application");
        var editor = layer.GetWindows().First(w => w.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase));
        layer.SendKeyboardInput(editor.Handle, "STATE_BEFORE"); layer.TakeHumanControl(); int oldWorker = layer.WorkerProcessId;
        layer.RestartWorker();
        bool unchanged = CompatibilityVerification.Alive(pid) && layer.ApplicationProcessId == pid && layer.GetWindows().First(w => w.Handle == editor.Handle).Text == "STATE_BEFORE";
        bool owner = layer.Owner == ControlOwner.Human; bool blocked = false;
        try { layer.SendKeyboardInput(editor.Handle, "BAD"); } catch (InvalidOperationException) { blocked = true; }
        layer.SendKeyboardInput(editor.Handle, "_AFTER", ControlOwner.Human);
        bool inputResumed = layer.GetWindows().First(w => w.Handle == editor.Handle).Text == "STATE_BEFORE_AFTER";
        var result = new { oldWorker, newWorker = layer.WorkerProcessId, oldWorkerExited = !CompatibilityVerification.Alive(oldWorker), sameAppPid = pid, unchanged, ownerPreserved = owner, wrongOwnerRejected = blocked, inputResumed, note = "Explicit worker restart. A hung application print handler is not repaired by restarting its caller; timeout still uses emergency layer termination." };
        File.WriteAllText(Path.Combine(output, "recovery.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(result)); return unchanged && owner && blocked && inputResumed ? 0 : 1;
    }
}

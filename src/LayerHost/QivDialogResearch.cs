using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class QivDialogResearch
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        QivCursorGuardSession? guard = null; GpuBridgeResearchSession? gpu = null;
        var checks = new List<object>(); bool passed = true, graceful = false; string? failure = null; int[] owned = []; int pid = 0;
        string registry = QivResearch.RegistryState();
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        void Wait(Func<bool> condition, string message)
        { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(30); } while (clock.ElapsedMilliseconds < 5000); throw new TimeoutException(message); }
        WindowInfo WaitDialog(long root)
        {
            var clock = Stopwatch.StartNew();
            do { var windows = layer.GetWindows(); var dialog = windows.FirstOrDefault(w => w.Parent == 0 && w.Owner == root && w.Visible && w.Enabled && w.Width > 100); if (dialog != null) return dialog; Thread.Sleep(30); } while (clock.ElapsedMilliseconds < 6000);
            throw new TimeoutException("Owned qIV dialog did not appear.");
        }
        try
        {
            string ini = QivResearch.Prepare(output, layer.Id);
            pid = layer.LaunchApplication(QivResearch.Executable, QivResearch.Arguments(output, ini), output);
            long root = QivGuardResearch.WaitRoot(layer, pid); guard = new(layer, root); layer.HideLayer(root); gpu = new(layer, root, ResearchGpuTarget.Qiv);
            gpu.Capture(Path.Combine(output, "main-before.png"), true);
            layer.PostKey(root, 0x71); // F2 opens the ordinary application's file picker.
            var dialog = WaitDialog(root); var windows = layer.GetWindows();
            File.WriteAllText(Path.Combine(output, "dialog-windows.json"), JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true }));
            var frame = layer.CaptureVisualOutput(dialog.Handle, CaptureBackend.PrintWindowFullContent);
            File.WriteAllBytes(Path.Combine(output, "dialog.png"), frame.Png);
            Check("ordinary owned dialog discovered and captured", dialog.ProcessId == pid && !windows.Single(w => w.Handle == root).Enabled, new { dialog, frame.Width, frame.Height, frame.Backend });
            bool disabledRejected = false; try { layer.SendKey(root, 0x23); } catch (LayerOperationException e) when (e.Message.StartsWith("Input destination is disabled")) { disabledRejected = true; }
            Check("disabled main input refused while modal is open", disabledRejected);
            layer.PostKey(dialog.Handle, 0x1B);
            var close = Stopwatch.StartNew(); while (layer.GetWindows().Any(w => w.Handle == dialog.Handle) && close.ElapsedMilliseconds < 4000) Thread.Sleep(30);
            Check("dialog cancelled and same source restored", !layer.GetWindows().Any(w => w.Handle == dialog.Handle) && layer.GetWindows().Single(w => w.Handle == root).Enabled && layer.ApplicationProcessId == pid);
            gpu.Capture(Path.Combine(output, "main-after.png"), true);
            Check("refused main input leaves original image intact", QivResearch.Pixels(Path.Combine(output,"main-after.png")).GetProperty("red").GetInt32() > 1000);
            layer.PostKey(root, 0x71); dialog = WaitDialog(root); windows = layer.GetWindows();
            long editor = layer.GetFocusedWindow(dialog.Handle);
            Check("focused filename edit belongs to dialog subtree", windows.Any(w => w.Handle == editor && w.ClassName == "Edit") && LayerWindowSelection.InSubtree(windows, editor, dialog.Handle));
            if (!windows.Any(w => w.Handle == editor && w.ClassName == "Edit")) throw new InvalidOperationException("Ordinary picker did not focus a filename editor.");
            string missing = Path.Combine(output, "images", "missing-disposable-file.png");
            layer.SendKeyboardInput(editor, missing); layer.PostKey(editor, 0x0D);
            WindowInfo? nested = null;
            Wait(() => { var all = layer.GetWindows(); nested = LayerWindowSelection.Select(all, root); return nested != null && nested.Handle != root && nested.Handle != dialog.Handle; }, "Missing-file validation dialog absent.");
            File.WriteAllText(Path.Combine(output, "nested-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllBytes(Path.Combine(output, "nested-dialog.png"), layer.CaptureVisualOutput(nested!.Handle, CaptureBackend.PrintWindowFullContent).Png);
            Check("nested modal selected through owner chain", nested.Owner == dialog.Handle && !layer.GetWindows().Single(w => w.Handle == dialog.Handle).Enabled, nested);
            layer.PostKey(nested.Handle, 0x1B); Wait(() => layer.GetWindows().All(w => w.Handle != nested.Handle), "Nested dialog did not cancel.");
            Check("outer dialog selected after nested cancellation", LayerWindowSelection.Select(layer.GetWindows(), root)?.Handle == dialog.Handle);
            layer.TakeHumanControl(); bool rejected = false;
            try { layer.SendKeyboardInput(editor, "unauthorized"); } catch (InvalidOperationException) { rejected = true; }
            Check("automation refused while human owns ordinary picker", rejected);
            // Use ordinary character handling to clear the known disposable value.
            layer.SendKey(editor, 0x23, ControlOwner.Human); layer.SendKeyboardInput(editor, new string('\b', missing.Length), ControlOwner.Human);
            string chosen = Path.Combine(output, "images", "02-alternate.png"); layer.SendKeyboardInput(editor, chosen, ControlOwner.Human);
            Check("filename entered with character messages", layer.GetWindows().Single(w => w.Handle == editor).Text == chosen);
            File.WriteAllBytes(Path.Combine(output, "filled-dialog.png"), layer.CaptureVisualOutput(dialog.Handle, CaptureBackend.PrintWindowFullContent).Png);
            layer.PostKey(editor, 0x0D, ControlOwner.Human); Wait(() => layer.GetWindows().All(w => w.Handle != dialog.Handle), "File open did not close ordinary picker.");
            long revision = layer.BeginVisualObservation(ControlOwner.Human);
            var observed = VisualObservation.WaitAsync(() =>
            {
                string path = Path.Combine(output, "opened-image.png"); var clock = Stopwatch.StartNew(); var metrics = gpu.Capture(path, true);
                return new CaptureFrame(File.ReadAllBytes(path), metrics.GetProperty("width").GetInt32(), metrics.GetProperty("height").GetInt32(), clock.Elapsed.TotalMilliseconds);
            }, f => { string path = Path.Combine(output, "observed.png"); File.WriteAllBytes(path, f.Png); return QivResearch.Pixels(path).GetProperty("magenta").GetInt32() > 1000; }, () => layer.ValidateVisualObservation(revision), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult();
            Check("ordinary picker opens selected file in same GPU source", observed.Matched && layer.ApplicationProcessId == pid && layer.Owner == ControlOwner.Human && !layer.GetWindows().Single(w => w.Handle == root).Visible, observed with { Frame = null });
            layer.ReturnToAutomation();
        }
        catch (Exception exception) { passed = false; failure = exception.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { gpu?.Dispose(); if (!layer.Closed) { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } } layer.Dispose(); guard?.Dispose(); Thread.Sleep(200); }
        var physical = monitor.Complete(owned); Check("sampled physical desktop containment", physical.GetProperty("passed").GetBoolean(), physical);
        Check("owned processes and registry cleaned", owned.All(id => !CompatibilityVerification.Alive(id)) && registry == QivResearch.RegistryState(), new { owned, graceful });
        File.WriteAllText(Path.Combine(output, "dialogs.json"), JsonSerializer.Serialize(new { passed, failure, checks, pid, owned, graceful }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

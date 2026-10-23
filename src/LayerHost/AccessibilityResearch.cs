using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class AccessibilityResearch
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        nint foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer); string desktop = Native.InputDesktopName();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer();
        var checks = new List<object>(); bool passed = true; string? failure = null; int[] owned = []; JsonElement? tree = null; bool graceful = false; object? invokeEvidence = null;
        void Check(string name, bool value, object? detail = null) { passed &= value; checks.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
        try
        {
            layer.LaunchApplication(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe"));
            WindowInfo[] windows = []; var watch = Stopwatch.StartNew();
            do { windows = layer.GetWindows(); if (windows.Any(window => window.ClassName == "RICHEDIT50W")) break; Thread.Sleep(40); } while (watch.ElapsedMilliseconds < 5000);
            var root = windows.First(window => window.Parent == 0 && window.ClassName == "#32770"); var editor = windows.First(window => window.ClassName == "RICHEDIT50W");
            // No Copy action, clipboard write, SetFocus, physical input, or source adapter.
            tree = layer.ObserveAccessible(root.Handle, "104"); File.WriteAllBytes(Path.Combine(output, "before.png"), layer.CaptureVisualOutput(root.Handle).Png);
            bool deniedInstrumentation = false; try { using var wrong = new GpuBridgeResearchSession(layer, root.Handle); } catch (InvalidOperationException e) when (e.Message.Contains("restricted")) { deniedInstrumentation = true; }
            Check("GPU instrumentation rejects ordinary executable", deniedInstrumentation);
            layer.ResearchSetAccessibleValue(editor.Handle, "UIA_PRIVATE"); string afterSet = layer.GetWindows().First(window => window.Handle == editor.Handle).Text;
            Check("semantic Value pattern changes ordinary native control", afterSet == "UIA_PRIVATE", new { afterSet });
            bool invoked = false; string? invokeError = null; var invokeWatch = Stopwatch.StartNew();
            try { layer.InvokeAccessibleAction(root.Handle, "Select"); invoked = true; } catch (LayerOperationException exception) { invokeError = exception.Message; }
            Thread.Sleep(100); string selected = layer.GetWindows().First(window => window.Handle == editor.Handle).Text;
            invokeEvidence = new { invoked, changed = selected.Length > afterSet.Length, selected, error = invokeError, ms = invokeWatch.Elapsed.TotalMilliseconds };
            // Keep a negative provider result as evidence, then verify the accepted
            // message path still works. These are explicitly different mechanisms.
            if (selected.Length == afterSet.Length) { var select = layer.GetWindows().First(window => window.Text == "&Select"); layer.SendMouseInput(select.Handle, 30, 10); selected = layer.GetWindows().First(window => window.Handle == editor.Handle).Text; }
            Check("ordinary glyph action remains available", selected.Contains("UIA_PRIVATE") && selected.Length > afterSet.Length, new { selected, invokeEvidence });
            layer.HideLayer(root.Handle); layer.ResearchSetAccessibleValue(editor.Handle, "HIDDEN_UIA");
            Check("semantic value works while native root stays hidden", layer.GetWindows().First(window => window.Handle == editor.Handle).Text == "HIDDEN_UIA" && !layer.GetWindows().First(window => window.Handle == root.Handle).Visible);
            layer.TakeHumanControl(); bool rejected = false; try { layer.ResearchSetAccessibleValue(editor.Handle, "REJECT"); } catch (InvalidOperationException) { rejected = true; }
            layer.ResearchSetAccessibleValue(editor.Handle, "HUMAN_UIA", ControlOwner.Human); layer.RestartWorker();
            Check("semantic adapter preserves ownership and worker recovery", rejected && layer.Owner == ControlOwner.Human && layer.GetWindows().First(window => window.Handle == editor.Handle).Text == "HUMAN_UIA");
            layer.ReturnToAutomation(); layer.ResearchSetAccessibleValue(editor.Handle, "AUTOMATION_UIA"); layer.ShowLayer(root.Handle); File.WriteAllBytes(Path.Combine(output, "after.png"), layer.CaptureVisualOutput(root.Handle).Png);
        }
        catch (Exception exception) { failure = exception.ToString(); passed = false; File.WriteAllText(Path.Combine(output, "failure.txt"), failure); }
        finally { owned = layer.GetProcessIds(); try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(3)); } catch { } layer.Dispose(); Thread.Sleep(250); }
        Native.GetCursorPos(out var afterPointer); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id)), foregroundEquality = foreground == Native.GetForegroundWindow(), pointerEquality = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y, desktopEquality = desktop == Native.InputDesktopName();
        Check("owned cleanup and physical desktop samples", cleanup && foregroundEquality && pointerEquality && desktopEquality, new { cleanup, foregroundEquality, pointerEquality, desktopEquality });
        File.WriteAllText(Path.Combine(output, "accessibility.json"), JsonSerializer.Serialize(new { passed, failure, checks, tree, invokeEvidence, owned, graceful, cleanup, foregroundEquality, pointerEquality, desktopEquality, note = "Unmodified Character Map; explicit semantic Value adapter on an owned private desktop. Invoke is an exploratory result recorded independently; a negative result is followed by the accepted targeted mouse path. SetValue is not keyboard event emulation and need not fire identical application events. Simulated ownership only; physical human acceptance pending. Select glyph does not write the global clipboard." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

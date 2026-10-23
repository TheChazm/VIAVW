using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class Win32Expansion
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); using var manager = new LayerManager(); var layer = manager.CreateLayer();
        int pid = layer.LaunchApplication(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "charmap.exe"), workingDirectory: output);
        var root = CompatibilityVerification.WaitRoot(layer, "Character Map"); var windows = layer.GetWindows();
        var editor = windows.First(w => w.ClassName == "RICHEDIT50W"); var select = windows.First(w => w.Text == "&Select");
        var scrollbar = windows.First(w => w.ClassName == "ScrollBar"); var grid = windows.First(w => w.ClassName == "CharGridWClass");
        var evidence = new List<object>(); var physical = Native.GetForegroundWindow(); Native.GetCursorPos(out var pointer);
        string Text() => layer.GetWindows().First(w => w.Handle == editor.Handle).Text;
        void Selection(string operation, Action action, string semantics)
        {
            var before = layer.GetControlSelection(editor.Handle); string? error = null; try { action(); } catch (LayerOperationException e) { error = e.Message; }
            Thread.Sleep(200); var after = layer.GetControlSelection(editor.Handle);
            evidence.Add(new { operation, semantics, selected = after.End > after.Start, before = new { before.Start, before.End }, after = new { after.Start, after.End }, text = Text(), error });
        }
        layer.SendKeyboardInput(editor.Handle, "M2 WIN32 text");
        layer.SendMouseInput(editor.Handle, 10, 12);
        layer.PostKey(editor.Handle, 9); Thread.Sleep(250);
        long focus = layer.GetFocusedWindow(root.Handle); bool tab = focus == select.Handle; string beforeEnter = Text();
        if (tab) { layer.PostKey(focus, 13); Thread.Sleep(250); }
        evidence.Add(new { operation = "Tab and Enter", tabToSelect = tab, enterSelectAppendedGlyph = tab && Text().Length > beforeEnter.Length, text = Text() });
        layer.SendMouseInput(editor.Handle, 10, 12);
        Selection("Ctrl+A", () => layer.SendKeyChord(editor.Handle, [0x11, 0x41]), "posted chord; no keyboard-state synthesis");
        Selection("drag selection", () => { layer.SendMouseEvent(editor.Handle, MouseMessage.LeftDown, 5, 12, 1); layer.SendMouseEvent(editor.Handle, MouseMessage.Move, 105, 12, 1); layer.SendMouseEvent(editor.Handle, MouseMessage.LeftUp, 105, 12); }, "native mouse messages and held-button flags");
        Selection("double-click selection", () => { layer.SendMouseInput(editor.Handle, 15, 12); layer.SendMouseEvent(editor.Handle, MouseMessage.LeftDoubleClick, 15, 12, 1); layer.SendMouseEvent(editor.Handle, MouseMessage.LeftUp, 15, 12); }, "normal native edit word selection");
        Selection("select-all control adapter", () => layer.SelectControlText(editor.Handle, 0, -1), "explicit EM_SETSEL control command; not modifier-key emulation");
        layer.SendKeyboardInput(editor.Handle, "REPLACED_BY_EDIT_PATH"); evidence.Add(new { operation = "edit selected text", passed = Text() == "REPLACED_BY_EDIT_PATH", text = Text(), semantics = "WM_CHAR replaces the selection created by the explicit control adapter" });
        int scrollBefore = layer.GetScrollPosition(scrollbar.Handle);
        layer.SendMouseEvent(grid.Handle, MouseMessage.Wheel, 100, 100, wheelDelta: -360); Thread.Sleep(250); int scrollAfter = layer.GetScrollPosition(scrollbar.Handle);
        evidence.Add(new { operation = "wheel character grid", working = scrollAfter != scrollBefore, scrollBefore, scrollAfter });
        layer.NotifyScrollControl(root.Handle, scrollbar.Handle, 1); Thread.Sleep(200); int notified = layer.GetScrollPosition(scrollbar.Handle);
        evidence.Add(new { operation = "scrollbar control adapter", working = notified > scrollAfter, before = scrollAfter, after = notified, semantics = "explicit WM_VSCROLL parent/control notification; not mouse-wheel emulation" });
        string? contextError = null;
        try { layer.SendMouseEvent(editor.Handle, MouseMessage.RightDown, 25, 12, 2); layer.SendMouseEvent(editor.Handle, MouseMessage.RightUp, 25, 12); } catch (LayerOperationException e) { contextError = e.Message; }
        Thread.Sleep(200); bool menu = layer.GetWindows().Any(w => w.ClassName == "#32768" && w.Visible);
        evidence.Add(new { operation = "right-click context menu", opened = menu, contextError });
        if (menu) { layer.PostKey(editor.Handle, 27); Thread.Sleep(200); layer.CancelOwnedMenu(root.Handle); }
        var frame = layer.CaptureVisualOutput(root.Handle); File.WriteAllBytes(Path.Combine(output, "charmap-expanded.png"), frame.Png);
        Native.GetCursorPos(out var afterPointer);
        bool pointerEqual = pointer.X == afterPointer.X && pointer.Y == afterPointer.Y;
        var watch = Stopwatch.StartNew(); bool graceful = layer.CloseGracefully(TimeSpan.FromSeconds(2)); if (!graceful) layer.Dispose();
        var report = new { pid, evidence, sampledForegroundEquality = Native.GetForegroundWindow() == physical, sampledPointerEquality = pointerEqual, graceful, shutdownMs = watch.Elapsed.TotalMilliseconds, note = "Unmodified Character Map; no clipboard Copy, font change, user file operation, or global input. Whole-run equality can change with legitimate Layer 0 activity." };
        File.WriteAllText(Path.Combine(output, "win32-expanded.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { output, evidence, graceful }));
        var about = manager.CreateLayer(); int aboutPid = about.LaunchApplication(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "winver.exe"), workingDirectory: output);
        var aboutRoot = CompatibilityVerification.WaitRoot(about, "About Windows"); var ok = about.GetWindows().First(w => w.ClassName == "Button" && w.Text == "OK");
        // Do not duplicate OS-registration personal text in this additional report.
        about.SendMouseInput(ok.Handle, 25, 10); watch.Restart(); while (CompatibilityVerification.Alive(aboutPid) && watch.ElapsedMilliseconds < 2000) Thread.Sleep(50);
        var aboutResult = new { application = "Unmodified winver.exe / About Windows dialog", pid = aboutPid, nativeMouseClosedDialog = !CompatibilityVerification.Alive(aboutPid), elapsedMs = watch.Elapsed.TotalMilliseconds };
        File.WriteAllText(Path.Combine(output, "winver-interaction.json"), JsonSerializer.Serialize(aboutResult, new JsonSerializerOptions { WriteIndented = true })); about.Dispose();
        return 0;
    }
}

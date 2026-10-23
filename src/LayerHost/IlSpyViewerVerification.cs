using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

internal static class IlSpyViewerVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var foreground = Native.GetForegroundWindow(); Native.GetCursorPos(out var cursor); uint? inputBefore = Native.LastInputTick();
        using var manager = new LayerManager(); using var qa = manager.CreateLayer();
        string sourceFolder = Path.Combine(output, "source"); var sourceArgs = IlSpyVerification.Prepare(sourceFolder);
        string readyFile = Path.Combine(output, "session.json");
        qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), ["--demo", "--queue-pointer", "--root-input", "--wpf-software-hook", "--ready-file", readyFile, "--qa-report-directory", sourceFolder, IlSpyVerification.Executable, .. sourceArgs], output);
        var watch = Stopwatch.StartNew(); WindowInfo[] windows = [];
        while (watch.ElapsedMilliseconds < 18000)
        {
            if (File.Exists(readyFile)) { using var status = JsonDocument.Parse(SnapshotFiles.ReadText(readyFile)); string? state = status.RootElement.GetProperty("state").GetString(); if (state == "error") throw new InvalidOperationException(status.RootElement.GetProperty("error").GetString()); if (state == "ready") break; }
            Thread.Sleep(100);
        }
        windows = qa.GetWindows();
        var root = windows.First(window => window.Text == "VIAVW — private application layer"); var picture = windows.First(window => window.Parent == root.Handle && window.Width > 900 && window.Height > 200);
        string previewFile = Path.Combine(sourceFolder, "viewer-preview.json"), observationFile = Path.Combine(sourceFolder, "viewer-accessible.json");
        JsonElement Read(string path) { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(path)); return doc.RootElement.Clone(); }
        void Button(string text) { var button = qa.GetWindows().First(window => window.Text == text); qa.SendMouseInput(button.Handle, 15, 12); }
        JsonElement Refresh()
        {
            long version = Read(previewFile).GetProperty("previewVersion").GetInt64(); Button("Refresh"); var until = Stopwatch.StartNew();
            while (Read(previewFile).GetProperty("previewVersion").GetInt64() <= version && until.ElapsedMilliseconds < 6000) Thread.Sleep(50);
            if (Read(previewFile).GetProperty("previewVersion").GetInt64() <= version) throw new TimeoutException("ILSpy viewer refresh deadline.");
            return Read(observationFile);
        }
        JsonElement Node(JsonElement snapshot, string id) => snapshot.GetProperty("nodes").EnumerateArray().First(node => node.GetProperty("id").GetString() == id);
        string Value(JsonElement snapshot, string id) => Node(snapshot, id).GetProperty("value").GetString() ?? "";
        JsonElement Wait(Func<JsonElement, bool> condition)
        {
            var until = Stopwatch.StartNew(); JsonElement snapshot;
            do { snapshot = Refresh(); if (condition(snapshot)) return snapshot; Thread.Sleep(150); } while (until.ElapsedMilliseconds < 6000);
            throw new TimeoutException("ILSpy viewer state did not match the expected input result.");
        }
        var initial = Refresh(); int sourcePid = initial.GetProperty("nodes")[0].GetProperty("pid").GetInt32();
        bool initialCode = Value(initial, "textEditor").Contains("class Program");
        File.WriteAllBytes(Path.Combine(output, "initial-viewer.png"), qa.CaptureVisualOutput(root.Handle).Png);
        Button("Human control"); Thread.Sleep(100); qa.SendMouseInput(picture.Handle, 610, 38); Thread.Sleep(300); qa.PostKeyboardInput(picture.Handle, "ProbeWindow"); Thread.Sleep(500);
        var typed = Wait(snapshot => Value(snapshot, "searchBox") == "ProbeWindow"); bool searchTyped = true;
        qa.SendQueueStateChord(picture.Handle, [0x11, 0x41]); Thread.Sleep(150); qa.PostKeyboardInput(picture.Handle, "Program"); Thread.Sleep(400);
        Wait(snapshot => Value(snapshot, "searchBox") == "Program"); bool shortcutWorked = true;
        qa.SendQueueStateChord(picture.Handle, [0x11, 0x41]); Thread.Sleep(150); qa.PostKeyboardInput(picture.Handle, "ProbeWindow"); Thread.Sleep(400);
        Wait(snapshot => Value(snapshot, "searchBox") == "ProbeWindow");
        qa.SendKey(picture.Handle, 0x28); Thread.Sleep(150); qa.SendKey(picture.Handle, 0x0D); Thread.Sleep(500); qa.SendMouseInput(picture.Handle, 846, 70); Thread.Sleep(300);
        var navigated = Wait(snapshot => Value(snapshot, "textEditor").Contains("ProbeWindow(bool") && Node(snapshot, "textEditor").GetProperty("height").GetDouble() > 250); bool constructorCode = true;
        double beforeScroll = Node(navigated, "textEditor").GetProperty("verticalScrollPercent").GetDouble();
        qa.SendMouseEvent(picture.Handle, MouseMessage.Wheel, 650, 290, wheelDelta: -720); Thread.Sleep(300); var scrolled = Wait(snapshot => Node(snapshot, "textEditor").GetProperty("verticalScrollPercent").GetDouble() > beforeScroll);
        double afterScroll = Node(scrolled, "textEditor").GetProperty("verticalScrollPercent").GetDouble(); bool wheelWorked = afterScroll > beforeScroll;
        qa.SendQueueMouseSequence(picture.Handle, [new(MouseMessage.Move, 400, 145), new(MouseMessage.LeftDown, 400, 145, 1), new(MouseMessage.Move, 650, 190, 1), new(MouseMessage.LeftUp, 650, 190)], true); Thread.Sleep(300);
        var dragged = Wait(snapshot => string.Join("", Node(snapshot, "textEditor").GetProperty("selection").EnumerateArray().Select(part => part.GetString())).Length > 20); string selectedCode = string.Join("", Node(dragged, "textEditor").GetProperty("selection").EnumerateArray().Select(part => part.GetString())); bool dragWorked = true;
        File.WriteAllBytes(Path.Combine(output, "selected-code-viewer.png"), qa.CaptureVisualOutput(root.Handle).Png);
        Button("Hide app window"); Thread.Sleep(200); long previousPreview = Read(previewFile).GetProperty("previewVersion").GetInt64(); Button("Inspect hidden app"); var inspectWait = Stopwatch.StartNew();
        bool Inspected() { var preview = Read(previewFile); return preview.GetProperty("previewVersion").GetInt64() > previousPreview && preview.GetProperty("Backend").GetString()!.Contains("temporary private show") && !preview.GetProperty("sourceWindowVisible").GetBoolean(); }
        while (!Inspected() && inspectWait.ElapsedMilliseconds < 6000) Thread.Sleep(50);
        var inspectedPreview = Read(previewFile); var inspectedState = Read(observationFile);
        bool hiddenInspection = inspectedPreview.GetProperty("Backend").GetString()!.Contains("temporary private show") && !inspectedPreview.GetProperty("sourceWindowVisible").GetBoolean() && Value(inspectedState, "textEditor") == Value(dragged, "textEditor") && string.Join("", Node(inspectedState, "textEditor").GetProperty("selection").EnumerateArray().Select(part => part.GetString())) == selectedCode;
        File.WriteAllBytes(Path.Combine(output, "hidden-inspection-viewer.png"), qa.CaptureVisualOutput(root.Handle).Png);
        Button("Return to automation"); var ownerWait = Stopwatch.StartNew(); while (Read(previewFile).GetProperty("owner").GetString() != "Automation" && ownerWait.ElapsedMilliseconds < 6000) { Refresh(); Thread.Sleep(100); }
        if (Read(previewFile).GetProperty("owner").GetString() != "Automation") throw new TimeoutException("ILSpy viewer ownership publication deadline.");
        qa.SendMouseInput(picture.Handle, 610, 38); qa.PostKeyboardInput(picture.Handle, "REJECT"); Thread.Sleep(400);
        var rejected = Refresh(); bool humanRejected = Value(rejected, "textEditor") == Value(dragged, "textEditor") && string.Join("", Node(rejected, "textEditor").GetProperty("selection").EnumerateArray().Select(part => part.GetString())) == selectedCode;
        bool sameProcess = rejected.GetProperty("nodes")[0].GetProperty("pid").GetInt32() == sourcePid;
        int[] ids = qa.GetProcessIds(); bool graceful = qa.CloseGracefully(TimeSpan.FromSeconds(10));
        if (!graceful)
        {
            var remaining = qa.GetProcessIds().Select(id => { using var process = Process.GetProcessById(id); return new { id, process.ProcessName }; }).ToArray();
            File.WriteAllText(Path.Combine(output, "close-failure.json"), JsonSerializer.Serialize(new { remaining, windows = qa.GetWindows() }, new JsonSerializerOptions { WriteIndented = true }));
            try { File.WriteAllBytes(Path.Combine(output, "close-failure.png"), qa.CaptureVisualOutput(root.Handle).Png); } catch (LayerOperationException) { }
            qa.Dispose();
        }
        Thread.Sleep(250); bool cleanup = ids.All(id => !CompatibilityVerification.Alive(id));
        Native.GetCursorPos(out var after); bool pointerEquality = cursor.X == after.X && cursor.Y == after.Y, foregroundEquality = foreground == Native.GetForegroundWindow();
        bool passed = initialCode && searchTyped && shortcutWorked && constructorCode && wheelWorked && dragWorked && hiddenInspection && humanRejected && sameProcess && graceful && cleanup && pointerEquality && foregroundEquality;
        File.WriteAllText(Path.Combine(output, "ilspy-viewer.json"), JsonSerializer.Serialize(new { passed, initialCode, searchTyped, shortcutWorked, constructorCode, wheelWorked, beforeScroll, afterScroll, dragWorked, selectedCode, hiddenInspection, inspectedPreview, humanRejected, sameProcess, sourcePid, graceful, cleanup, pointerEquality, foregroundEquality, inputBefore, inputAfter = Native.LastInputTick(), note = "Actual multithreaded viewer on private QA desktop, unmodified ordinary ILSpy on a second private desktop. Human ownership input is simulated through viewer controls; physical manual acceptance pending. Passive owned UIA observes values, scroll percentage and selection, never changes them. QA reporting is explicitly enabled only in this test." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, initialCode, searchTyped, shortcutWorked, constructorCode, wheelWorked, dragWorked, hiddenInspection, humanRejected, sameProcess, graceful, cleanup })); return passed ? 0 : 1;
    }
}

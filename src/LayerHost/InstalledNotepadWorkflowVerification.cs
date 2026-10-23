using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

// An ordinary multi-document editing session, operated through the actual viewer.
// Only setup creates files. Once launched, document changes use picture input.
internal static class InstalledNotepadWorkflowVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); var setup = InstalledNotepadResearch.Prepare(output, true);
        string first = Path.Combine(output, "notes.txt"), second = Path.Combine(output, "checklist.txt"), copy = Path.Combine(output, "notes-reviewed.txt");
        const string notes = "Project notes\r\nStatus: draft\r\nOwner: VIAVW\r\n";
        const string checklist = "Review checklist\r\n[ ] Review notes\r\n[ ] Save revisions\r\n";
        File.WriteAllText(first, notes, new UTF8Encoding(true)); File.WriteAllText(second, checklist, new UTF8Encoding(true));
        string personal = InstalledNotepadResearch.PersonalSettingsDigest();
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable)));
        var existing = Process.GetProcessesByName("notepad++").Select(p => { using (p) return (p.Id, Created: p.StartTime.ToUniversalTime()); }).ToArray();
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var steps = new List<object>(); var frames = new List<object>(); bool passed = true, graceful = false; string? failure = null; string stage = "startup";
        long viewer = 0, canvas = 0, main = 0; int sourcePid = 0; int[] owned = []; var total = Stopwatch.StartNew();
        void Wait(Func<bool> condition, string reason) { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(35); } while (clock.ElapsedMilliseconds < 8000); throw new TimeoutException(reason); }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        JsonElement Accessible() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json"))); return doc.RootElement.Clone(); }
        bool Status(string text) => qa.GetWindows().Any(w => w.Text.Contains(text, StringComparison.Ordinal));
        void Button(string name) { var button = qa.GetWindows().Single(w => w.Text == name); qa.SendMouseInput(button.Handle, 10, 10); }
        void Check(string name, bool value, object? detail = null)
        { steps.Add(new { name, passed = value, detail }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) throw new InvalidOperationException(name); }
        JsonElement Inspect(string name, Func<JsonElement, bool>? condition = null)
        {
            long before = Preview().GetProperty("previewVersion").GetInt64(); Button("Refresh");
            Wait(() => { var p = Preview(); return p.GetProperty("previewVersion").GetInt64() > before && (condition?.Invoke(p) ?? true); }, "Inspection did not reach " + name);
            var info = Preview(); File.WriteAllBytes(Path.Combine(output, name + "-viewer.png"), qa.CaptureVisualOutput(viewer).Png);
            frames.Add(new { name, preview = info }); return info;
        }
        void Chord(params int[] keys) { qa.SendQueueStateChord(canvas, keys); Thread.Sleep(210); }
        void Key(int key) { qa.PostKey(canvas, key); Thread.Sleep(90); }
        void Type(string value) { qa.PostKeyboardInput(canvas, value); Thread.Sleep(150); }
        void Line(string value) { Type(value); Key(0x0D); }
        Point Picture(int x, int y)
        { var p = Preview().GetProperty("presentation"); return new((int)Math.Ceiling((double)x * p.GetProperty("DrawWidth").GetInt32() / p.GetProperty("SourceWidth").GetInt32()), (int)Math.Ceiling((double)y * p.GetProperty("DrawHeight").GetInt32() / p.GetProperty("SourceHeight").GetInt32())); }
        void Click(int x, int y) { Point point = Picture(x, y); qa.SendMouseInput(canvas, point.X, point.Y); Thread.Sleep(140); }
        bool Main(JsonElement p) => p.GetProperty("root").GetInt64() == main && p.GetProperty("activeClass").GetString() == "Notepad++";
        bool Current(string file) => Main(Preview()) && Preview().GetProperty("activeText").GetString()!.Contains(Path.GetFileName(file), StringComparison.Ordinal);
        void Save(string file, string expected, string name)
        {
            stage = name;
            var watch = Stopwatch.StartNew(); Chord(0x11, 0x53); Wait(() => File.ReadAllText(file) == expected, "Saved contents differ: " + name);
            Inspect(name, p => Main(p) && p.GetProperty("activeText").GetString()!.Contains(Path.GetFileName(file), StringComparison.Ordinal));
            Check(name, File.ReadAllText(file) == expected && !Preview().GetProperty("activeText").GetString()!.StartsWith('*'), new { file = Path.GetFileName(file), expected, ms = watch.Elapsed.TotalMilliseconds });
        }
        void FileDialog(string file, bool saveAs, string name)
        {
            // Notepad++ uses Ctrl+Alt+S for Save As; Ctrl+Shift+S is Save All.
            stage = name; if (saveAs) Chord(0x11, 0x12, 0x53); else Chord(0x11, 0x4F);
            var picker = Inspect(name + "-dialog", p => p.GetProperty("activeClass").GetString() == "#32770");
            var presentation = picker.GetProperty("presentation");
            Check(name + " dialog is fully visible and interactive", presentation.GetProperty("DrawHeight").GetInt32() <= presentation.GetProperty("CanvasHeight").GetInt32() && presentation.GetProperty("DrawWidth").GetInt32() <= presentation.GetProperty("CanvasWidth").GetInt32(), presentation);
            var field = picker.GetProperty("inputWindow"); var origin = picker.GetProperty("sourceClientOrigin");
            Check(name + " filename control is identified from ordinary window state", field.GetProperty("ClassName").GetString() == "Edit");
            Click(field.GetProperty("X").GetInt32() - origin.GetProperty("X").GetInt32() + field.GetProperty("Width").GetInt32() / 2,
                field.GetProperty("Y").GetInt32() - origin.GetProperty("Y").GetInt32() + field.GetProperty("Height").GetInt32() / 2);
            Chord(0x11, 0x41); Type(file); Key(0x0D);
            Inspect(name + "-opened", p => Main(p) && p.GetProperty("activeText").GetString()!.Contains(Path.GetFileName(file), StringComparison.Ordinal));
            Check(name + " returns to same editor window and file", Current(file));
        }
        try
        {
            string ready = Path.Combine(output, "session.json"); var args = new List<string> { "--demo", "--full-content", "--queue-pointer", "--posted-shortcuts", "--fit-preview", "--ready-file", ready, "--qa-report-directory", output, setup.Executable };
            args.AddRange(setup.Arguments.SkipLast(1)); qa.LaunchApplication(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"), args.ToArray(), output);
            Wait(() => File.Exists(ready) && SnapshotFiles.ReadText(ready).Contains("\"state\":\"ready\""), "Viewer startup failed.");
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle; canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            main = Preview().GetProperty("root").GetInt64(); sourcePid = Preview().GetProperty("applicationPid").GetInt32();
            Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership was not applied."); Click(120, 60);
            FileDialog(first, false, "01-open-notes");
            stage = "select and replace status line"; Chord(0x11, 0x24); Key(0x28); Key(0x24); Chord(0x10, 0x23); Inspect("02-selected-status");
            Type("Status: reviewed"); string revised = notes.Replace("Status: draft", "Status: reviewed"); Save(first, revised, "03-save-selected-line-edit");
            // Selection replacement creates two observed undo groups in this path:
            // first character plus selection removal, then the remaining typing.
            Chord(0x11, 0x5A); Save(first, notes.Replace("Status: draft", "S"), "04a-undo-remaining-typing");
            Chord(0x11, 0x5A); Save(first, notes, "04b-undo-restores-original-line");
            Chord(0x11, 0x59); Save(first, notes.Replace("Status: draft", "S"), "05a-redo-selection-replacement");
            Chord(0x11, 0x59); Save(first, revised, "05b-redo-restores-edit");
            Chord(0x11, 0x23); Line("Reviewed: café Ω"); string unicode = revised + "Reviewed: café Ω\r\n"; Save(first, unicode, "06-save-unicode-note");
            FileDialog(copy, true, "07-save-as-reviewed-copy"); Wait(() => File.Exists(copy), "Save As did not create a new file."); Check("Save As writes exact copied document", File.ReadAllText(copy) == unicode);
            Chord(0x11, 0x23); Line("Copy only"); string copyText = unicode + "Copy only\r\n"; Save(copy, copyText, "08-edit-reviewed-copy"); Check("editing saved copy preserves original notes", File.ReadAllText(first) == unicode);
            FileDialog(second, false, "09-open-checklist"); Chord(0x11, 0x24); Key(0x28); Key(0x24); Chord(0x10, 0x23); Type("[x] Review notes"); string checkedText = checklist.Replace("[ ] Review notes", "[x] Review notes"); Save(second, checkedText, "10-save-checklist-edit");
            stage = "keyboard file switching"; Chord(0x11, 0x09); Inspect("11-keyboard-switch-to-copy", p => Main(p) && p.GetProperty("activeText").GetString()!.Contains("notes-reviewed.txt", StringComparison.Ordinal)); Check("Ctrl+Tab changes active document", Current(copy));
            Chord(0x11, 0x23); Line("After switching"); copyText += "After switching\r\n"; Save(copy, copyText, "12-save-after-switch"); Check("other document retains its independent saved contents", File.ReadAllText(second) == checkedText);
            stage = "mouse file switching"; var tab = Accessible().GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("type").GetString() == "ControlType.TabItem" && n.GetProperty("name").GetString() == "checklist.txt");
            var origin = Preview().GetProperty("sourceClientOrigin"); Click((int)(tab.GetProperty("x").GetDouble() - origin.GetProperty("X").GetInt32() + tab.GetProperty("width").GetDouble() / 2), (int)(tab.GetProperty("y").GetDouble() - origin.GetProperty("Y").GetInt32() + tab.GetProperty("height").GetDouble() / 2));
            Inspect("13-pointer-switch-to-checklist", p => Main(p) && p.GetProperty("activeText").GetString()!.Contains("checklist.txt", StringComparison.Ordinal)); Check("pointer tab switching preserves both documents", Current(second) && File.ReadAllText(copy) == copyText && File.ReadAllText(second) == checkedText);
            stage = "close and reopen files"; Chord(0x11, 0x57); Inspect("14-checklist-closed", p => Main(p) && p.GetProperty("activeText").GetString()!.Contains("notes-reviewed.txt", StringComparison.Ordinal));
            Chord(0x11, 0x57); Inspect("15-copy-closed", p => Main(p) && !p.GetProperty("activeText").GetString()!.Contains("notes-reviewed.txt", StringComparison.Ordinal));
            FileDialog(copy, false, "16-reopen-saved-copy");
            string loaded = Preview().GetProperty("inputWindow").GetProperty("Text").GetString()!;
            Check("reopened application editor contains exact saved text", loaded == copyText, new { loaded, expected = copyText });
            Chord(0x11, 0x23); Line("Reopened and verified"); copyText += "Reopened and verified\r\n"; Save(copy, copyText, "17-save-after-reopen");
            Button("Return to automation"); Wait(() => Status("Automation owns input"), "Automation ownership missing."); Type("REJECTED");
            Button("Human control"); Wait(() => Status("Human owns input"), "Human ownership missing."); Save(copy, copyText, "18-handoff-preserves-documents");
            Check("whole workflow preserves one installed application", Preview().GetProperty("applicationPid").GetInt32() == sourcePid && Current(copy));
            Check("all final disposable files match expected independent contents", File.ReadAllText(first) == unicode && File.ReadAllText(second) == checkedText && File.ReadAllText(copy) == copyText);
        }
        catch (Exception error)
        {
            passed = false; failure = error.ToString(); Console.Error.WriteLine(failure); File.WriteAllText(Path.Combine(output, "failure.txt"), failure);
            if (!qa.Closed && viewer != 0) try { File.WriteAllBytes(Path.Combine(output, "failure-viewer.png"), qa.CaptureVisualOutput(viewer).Png); } catch { }
        }
        finally
        {
            if (!qa.Closed) { owned = qa.GetProcessIds(); try { graceful = qa.CloseGracefully(TimeSpan.FromSeconds(4)); } catch { } }
            qa.Dispose(); Thread.Sleep(150);
        }
        var physical = monitor.Complete(owned); bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id));
        bool existingPreserved = existing.All(old => { try { using var p = Process.GetProcessById(old.Id); return !p.HasExited && p.StartTime.ToUniversalTime() == old.Created; } catch (ArgumentException) { return false; } });
        bool unchanged = personal == InstalledNotepadResearch.PersonalSettingsDigest() && hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable)));
        passed &= cleanup && physical.GetProperty("passed").GetBoolean() && existingPreserved && unchanged && graceful;
        File.WriteAllText(Path.Combine(output, "notepad-workflow.json"), JsonSerializer.Serialize(new { passed, stage, failure, steps, frames, sourcePid, main, elapsedMs = total.Elapsed.TotalMilliseconds, graceful, cleanup, owned, physical, existingPreserved, unchanged, rendering = "built-in GDI setting", plugins = "disabled", instrumentation = "none", physicalHumanAcceptance = "pending", evidence = "Inputs passed through actual viewer controls and picture; file bytes and ordinary window text inspected read-only. No source-model writes or semantic input adapter." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class VisualStudioWorkflowVerification
{
    internal static int FastViewer(string setupFile,string output,bool timing=false,int followupMilliseconds=16,bool immediatePaint=false,bool preferTextHint=false,bool nativeSubtreeGeometry=false,bool preparePointer=false)=>Viewer(setupFile,output,standard:true,windowDcPreview:true,measurePixels:timing,measureTiming:timing,preciseMouseWaits:true,fastPopupGeometry:true,incrementalPaint:true,deadlinePreview:true,deadlineFirstMilliseconds:4,captureOriginReport:true,asyncStatusReports:true,splitPreviewClips:true,preciseChordHoldMilliseconds:24,postedFunctionKeys:true,deadlineFollowupMilliseconds:followupMilliseconds,immediatePreviewPaint:immediatePaint,preferredTextAutomationId:preferTextHint?"WpfTextView":null,nativeSubtreeGeometry:nativeSubtreeGeometry,preparePointer:preparePointer);
    internal static int Viewer(string setupFile, string output, bool standard = false, bool diagnostic = false, bool copyPreview = false, bool measurePixels = false, bool repaintPreview = false, bool activatePrivate = false, bool nativeModalOnly = false, bool windowDcPreview = false, bool measureTiming = false, bool captureOriginReport = false, bool asyncStatusReports = false, bool preciseMouseWaits = false, bool fastPopupGeometry = false,bool incrementalPaint=false,bool deadlinePreview=false,int deadlineFirstMilliseconds=16,bool splitPreviewClips=false,int preciseChordHoldMilliseconds=0,bool postedFunctionKeys=false,bool postedFunctionTransitions=false,int deadlineFollowupMilliseconds=16,bool immediatePreviewPaint=false,string? preferredTextAutomationId=null,bool nativeSubtreeGeometry=false,bool preparePointer=false)
    {
        var setup = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        // Only the controlled project and installed IDE may use this research entry point.
        VisualStudioResearch.ValidateSetup(setup, setupFile);
        using var profileLease = standard ? VisualStudioFreshProfile.Lease(setup, setupFile) : null;
        string[] arguments = standard ? setup.Arguments.Where(a => a != "/SafeMode").ToArray() : setup.Arguments;
        if (standard) { int log = Array.IndexOf(arguments, "/Log"); arguments[log + 1] = Path.Combine(output, "ActivityLog.xml"); arguments = [..arguments, setup.First]; }
        ApplicationConfiguration.Initialize(); using var form = new DemoForm(setup.Executable, arguments, backend: CaptureBackend.PrintWindowFullContent, queuePointer: true, qaReportDirectory: output, postedShortcuts: true, fitPreview: true, startupTimeoutMilliseconds: 60000, diagnosticInput: standard && diagnostic, releaseStateBeforeKeyUp: standard, humanControlHint: "Human owns input. Click the editor or a menu in the picture. Drag selection is delivered on release. Ctrl+O remains available to reopen code.", nativePopupPointer: standard, responsivePreview: standard && !diagnostic, directPreview: standard && !copyPreview, measurePixels:measurePixels,repaintPreview:repaintPreview,accessibleModalInput:standard&&!nativeModalOnly,activatePrivate:activatePrivate,lowLatencyInput:standard,softwareClientPreview:standard,windowDcPreview:windowDcPreview,measureTiming:measureTiming,captureOriginReport:captureOriginReport,asyncStatusReports:asyncStatusReports,preciseMouseWaits:preciseMouseWaits,fastPopupGeometry:fastPopupGeometry,incrementalPaint:incrementalPaint,deadlinePreview:deadlinePreview,deadlineFirstMilliseconds:deadlineFirstMilliseconds,splitPreviewClips:splitPreviewClips,preciseChordHoldMilliseconds:preciseChordHoldMilliseconds,postedFunctionKeys:postedFunctionKeys,postedFunctionTransitions:postedFunctionTransitions,deadlineFollowupMilliseconds:deadlineFollowupMilliseconds,immediatePreviewPaint:immediatePreviewPaint,preferredTextAutomationId:preferredTextAutomationId,nativeSubtreeGeometry:nativeSubtreeGeometry,initialPointerTitleFragment:preparePointer?Path.GetFileName(setup.First):null,initialPointerAutomationId:preparePointer?"WpfTextView":null);
        if (standard) form.WindowState = FormWindowState.Maximized;
        Application.Run(form); return 0;
    }
    internal static int Run(string output, string setupFile, bool standard = false, bool fullWorkflow = false, bool mouseWorkflow = false,string? viewerExecutable=null,bool windowDcPreview=false,bool asyncStatusReports=false,bool preciseMouseWaits=false,bool incrementalPaint=false,int preciseChordHoldMilliseconds=0,bool fastPreview=false,bool preparePointer=false)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        VisualStudioResearch.ValidateSetup(setup, setupFile);
        viewerExecutable=Path.GetFullPath(viewerExecutable??Path.Combine(AppContext.BaseDirectory,"LayerHost.exe"));
        string projectRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        if(!viewerExecutable.StartsWith(projectRoot,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(viewerExecutable)!="LayerHost.exe"||!File.Exists(viewerExecutable))throw new ArgumentException("Use a preserved VIAVW viewer executable inside this project.");
        if (mouseWorkflow) File.AppendAllText(setup.First, string.Join("\r\n", Enumerable.Range(1, 90).Select(i => "// Disposable scrolling line " + i)) + "\r\n");
        bool isolated=File.Exists(Path.Combine(Path.GetDirectoryName(setupFile)!,"isolated-profile.json"));
        var personal = VisualStudioResearch.PersonalSettings(allowUnavailable:isolated); string original = File.ReadAllText(setup.First);
        using var manager = new LayerManager(); using var qa = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        string stage = "startup"; string? failure = null; bool graceful = false, editing = false, pickerObserved = false, handoff = false, navigation = false, ideBuild = false, postBuildEdit = false, pointerCaret = false, startupDocument = false; int[] owned = []; long viewer = 0, canvas = 0; var frames = new List<object>(); object? buildEvidence = null;
        string expectedFirst = fullWorkflow ? "namespace ViavwDisposable { public static class Greeting { public const string Text = \"Viewer-edited greeting\"; } }" : "// VIAVW disposable editor workflow";
        string expectedSecond = "namespace ViavwDisposable { public static class Details { public const string Status = \"Viewer reviewed\"; } }";
        var mouseChecks = new Dictionary<string, bool>();
        string ObservedText(JsonElement n) { string text=n.GetProperty("text").GetString()??"";return isolated?System.Text.RegularExpressions.Regex.Replace(text,@"(?m)^\d+ ",""):text; }
        JsonElement Preview() { using var doc = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-preview.json"))); return doc.RootElement.Clone(); }
        void Wait(Func<bool> condition, string reason, int timeout = 12000) { var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(80); } while (clock.ElapsedMilliseconds < timeout); throw new TimeoutException(reason); }
        void Button(string name) { var button = qa.GetWindows().Single(w => w.Text == name); qa.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(100); }
        void Inspect(string name, Func<JsonElement, bool>? condition = null)
        {
            string inspectButton = standard ? "Inspect details" : "Refresh";
            long version = Preview().GetProperty("previewVersion").GetInt64(); Button(inspectButton);
            Wait(() =>
            {
                var preview = Preview(); long current = preview.GetProperty("previewVersion").GetInt64();
                if (current <= version) return false;
                if (preview.GetProperty("observationVersion").GetInt64() == current && (condition?.Invoke(preview) ?? true)) return true;
                // Automatic frames intentionally have no full UIA snapshot.
                // Refresh passive evidence when a deferred menu/dialog arrives;
                // never repeat the input to obtain an expected result.
                version = current; Button(inspectButton); return false;
            }, "Refresh did not reach the observed application state: " + name, 20000);
            byte[] screenshot=qa.CaptureVisualOutput(viewer).Png;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), screenshot); frames.Add(new { name, preview = Preview() });
            if(windowDcPreview)VisualStudioFrameChecks.RequireNoSentinel(screenshot);
            if(windowDcPreview&&!Preview().GetProperty("Backend").GetString()!.StartsWith("Window DC",StringComparison.Ordinal))throw new InvalidOperationException("DC mouse workflow used a different root capture backend.");
            File.WriteAllText(Path.Combine(output, name + "-accessible.json"), SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
        }
        void Ack(long before, string action) => Wait(() => Preview().GetProperty("inputSequence").GetInt64() > before, "Viewer did not finish forwarding " + action);
        void Chord(params int[] keys) { long before = Preview().GetProperty("inputSequence").GetInt64(); qa.SendQueueStateChord(canvas, keys); Ack(before, "chord"); Thread.Sleep(150); }
        void Key(int key) { long before = Preview().GetProperty("inputSequence").GetInt64(); qa.PostKey(canvas, key); Ack(before, "key"); Thread.Sleep(100); }
        void Click(int x, int y)
        { long before = Preview().GetProperty("inputSequence").GetInt64(); var p = Preview().GetProperty("presentation"); qa.SendMouseInput(canvas, (int)Math.Ceiling((double)x * p.GetProperty("DrawWidth").GetInt32() / p.GetProperty("SourceWidth").GetInt32()), (int)Math.Ceiling((double)y * p.GetProperty("DrawHeight").GetInt32() / p.GetProperty("SourceHeight").GetInt32())); Ack(before, "click"); }
        (int X, int Y) CanvasPoint(int x, int y)
        { var p = Preview().GetProperty("presentation"); return ((int)Math.Ceiling((double)x * p.GetProperty("DrawWidth").GetInt32() / p.GetProperty("SourceWidth").GetInt32()), (int)Math.Ceiling((double)y * p.GetProperty("DrawHeight").GetInt32() / p.GetProperty("SourceHeight").GetInt32())); }
        JsonElement Node(Func<JsonElement, bool> condition)
        { using var state = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json"))); return state.RootElement.GetProperty("nodes").EnumerateArray().First(condition).Clone(); }
        (int X, int Y) NodePoint(JsonElement node)
        { var origin = Preview().GetProperty("sourceClientOrigin"); return ((int)(node.GetProperty("x").GetDouble() + node.GetProperty("width").GetDouble() / 2) - origin.GetProperty("X").GetInt32(), (int)(node.GetProperty("y").GetDouble() + node.GetProperty("height").GetDouble() / 2) - origin.GetProperty("Y").GetInt32()); }
        void ClickNode(JsonElement node) { var point = NodePoint(node); Click(point.X, point.Y); }
        void MouseCheck(bool condition, string name, bool requiredNow = true) { mouseChecks[name] = condition; if (!condition && requiredNow) throw new InvalidOperationException(name); Console.WriteLine((condition ? "PASS " : "FAIL ") + name); }
        int PopupCount(JsonElement p) => p.GetProperty("popupSurfaces").GetArrayLength();
        bool EditorMatches(string text)
        {
            using var observation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
            return observation.RootElement.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == text);
        }
        bool FocusedCode(string text)
        {
            using var observation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
            return observation.RootElement.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("id").GetString() == "WpfTextView" && n.GetProperty("focused").GetBoolean() && ObservedText(n) == text);
        }
        void OpenDocument(string path, string name)
        {
            Chord(0x11, 0x4F); Inspect(name + "-dialog", p => p.GetProperty("activeClass").GetString() == "#32770" && p.GetProperty("activeText").GetString() == "Open File");
            var p = Preview(); var field = p.GetProperty("inputWindow"); var origin = p.GetProperty("sourceClientOrigin");
            if (!field.GetProperty("ClassName").GetString()!.Contains("Edit", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Expected the ordinary filename field.");
            Click(field.GetProperty("X").GetInt32() - origin.GetProperty("X").GetInt32() + field.GetProperty("Width").GetInt32() / 2, field.GetProperty("Y").GetInt32() - origin.GetProperty("Y").GetInt32() + field.GetProperty("Height").GetInt32() / 2);
            Chord(0x11, 0x41); qa.PostKeyboardInput(canvas, path + "x"); Thread.Sleep(250);
            Inspect(name + "-typed-filename");
            JsonElement Filename() => Node(n => n.GetProperty("type").GetString() == "ControlType.Edit" && n.GetProperty("id").GetString() == "1148");
            if (Filename().GetProperty("value").GetString() != path + "x") throw new InvalidOperationException("Filename typing did not preserve the requested path plus deletion suffix.");
            // Native filename completion may select a suffix. Collapse it before
            // testing single-character deletion, as a user pressing End would.
            Key(0x23); Inspect(name + "-before-backspace");
            if (Filename().GetProperty("selection").EnumerateArray().Any(s => !string.IsNullOrEmpty(s.GetString()))) throw new InvalidOperationException("End did not collapse the filename selection.");
            Key(0x08); Inspect(name + "-after-backspace");
            if (Filename().GetProperty("value").GetString() != path) throw new InvalidOperationException("Backspace did not delete exactly the filename suffix.");
            Key(0x0D);
            Inspect(name + "-opened", p => p.GetProperty("activeClass").GetString() != "#32770" && p.GetProperty("activeText").GetString()!.Contains(Path.GetFileName(path), StringComparison.Ordinal));
            if (!EditorMatches(File.ReadAllText(path))) throw new InvalidOperationException("Application-loaded document did not match disk: " + path);
        }
        void SaveDocument(string path, string text, string name)
        {
            Click(350, 160); Chord(0x11, 0x41);
            long before = Preview().GetProperty("inputSequence").GetInt64(); qa.PostKeyboardInput(canvas, text);
            // A synchronous test shortcut can jump ahead of posted canvas
            // characters. Wait for forwarding instead of guessing with 250 ms.
            Wait(() => Preview().GetProperty("inputSequence").GetInt64() >= before + text.Length, "Viewer did not forward the complete document before Save.");
            // Forwarding is not application acknowledgement. Verify the IDE's
            // actual edited text before the dependent Save shortcut.
            Inspect(name + "-typed", _ => EditorMatches(text));
            Chord(0x11, 0x53);
            Wait(() => File.ReadAllText(path) == text, "Saved file did not match ordinary viewer input: " + path); Inspect(name);
        }
        try
        {
            string[] launchArguments=[incrementalPaint?"--run-vs-damage-viewer":preciseMouseWaits?"--run-vs-precise-viewer":asyncStatusReports?"--run-vs-async-report-viewer":windowDcPreview?"--run-vs-dc-viewer":standard ? "--run-vs-private-viewer" : "--run-vs-research-viewer", Path.GetFullPath(setupFile), output];
            if(preciseChordHoldMilliseconds>0)launchArguments=["--run-vs-chord-viewer",setupFile,output,preciseChordHoldMilliseconds.ToString()];
            if(fastPreview)launchArguments=[preparePointer?"--run-vs-prepared-viewer":"--run-vs-fast-viewer",setupFile,output];
            qa.LaunchApplication(viewerExecutable,launchArguments,output,standard?null:setup.Environment);
            Wait(() => File.Exists(Path.Combine(output, "viewer-preview.json")), "Research viewer did not publish a frame.", 90000);
            var windows = qa.GetWindows(); viewer = windows.Single(w => w.Text == "VIAVW — private application layer").Handle; canvas = windows.Single(w => w.Parent == viewer && w.Width > 900 && w.Height > 200).Handle;
            if(standard&&isolated)qa.EnablePrivateInputSession();Button("Human control"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Human owns input", StringComparison.Ordinal) || w.Text.StartsWith("Human | PID ", StringComparison.Ordinal))), "Human ownership was not applied."); Inspect("01-initial-viewer");
            if (standard) Thread.Sleep(2500); // Let restored documents and package startup settle before the single shortcut attempt.
            if (standard) { Inspect("02-startup-code"); startupDocument = FocusedCode(original); if (!startupDocument) throw new InvalidOperationException("The demo must initially open and focus its disposable source file."); }
            if (mouseWorkflow)
            {
                stage = "Tools menu through viewer";
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "Tools"));
                Inspect("mouse-01-tools", p => PopupCount(p) > 0);
                MouseCheck(PopupCount(Preview()) > 0, "Tools popup is visible in composed preview");
                stage = "ownership while menu is open";
                var blockedOption = NodePoint(Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && (n.GetProperty("name").GetString() ?? "").StartsWith("Options", StringComparison.Ordinal)));
                Button("Return to automation"); Inspect("mouse-01a-automation-menu", p => p.GetProperty("owner").GetString() == "Automation" && PopupCount(p) > 0);
                long rejectedSequence = Preview().GetProperty("inputSequence").GetInt64(); var blockedCanvas = CanvasPoint(blockedOption.X, blockedOption.Y); qa.SendMouseInput(canvas, blockedCanvas.X, blockedCanvas.Y);
                Inspect("mouse-01b-rejected-menu-click", p => p.GetProperty("owner").GetString() == "Automation" && PopupCount(p) > 0);
                MouseCheck(Preview().GetProperty("inputSequence").GetInt64() == rejectedSequence && Preview().GetProperty("activeText").GetString() != "Options", "Automation ownership rejects mouse command while preserving open menu");
                Button("Human control"); Inspect("mouse-01c-human-menu", p => p.GetProperty("owner").GetString() == "Human" && PopupCount(p) > 0);
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && (n.GetProperty("name").GetString() ?? "").StartsWith("Options", StringComparison.Ordinal)));
                Inspect("mouse-02-options", p => p.GetProperty("activeText").GetString() == "Options");
                MouseCheck(Preview().GetProperty("activeText").GetString() == "Options", "Mouse menu command opens Options");
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.Button" && n.GetProperty("name").GetString() == "Cancel"));
                Inspect("mouse-03-options-cancel", p => p.GetProperty("root").GetInt64() == p.GetProperty("mainRoot").GetInt64());
                MouseCheck(Preview().GetProperty("surfaceKind").GetString() == "main", "Mouse cancels dialog and returns to main window");
                stage = "File Open submenu through hover";
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "File"));
                Inspect("mouse-04-file", p => PopupCount(p) > 0);
                var open = Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "Open");
                var openPoint = NodePoint(open); var hover = CanvasPoint(openPoint.X, openPoint.Y); qa.SendMouseEvent(canvas, MouseMessage.Move, hover.X, hover.Y);
                Inspect("mouse-05-open-submenu", p => PopupCount(p) >= 2);
                MouseCheck(PopupCount(Preview()) >= 2, "Hover opens and displays nested submenu");
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && (n.GetProperty("name").GetString() ?? "").StartsWith("File...", StringComparison.Ordinal)));
                Inspect("mouse-06-open-dialog", p => p.GetProperty("activeClass").GetString() == "#32770" && p.GetProperty("activeText").GetString() == "Open File");
                MouseCheck(Preview().GetProperty("activeText").GetString() == "Open File", "Nested mouse menu opens ordinary file picker");
                Chord(0x11, 0x41); qa.PostKeyboardInput(canvas, setup.Second); Thread.Sleep(250); Key(0x0D);
                Inspect("mouse-07-second-opened", p => p.GetProperty("activeText").GetString()!.Contains("Details.cs", StringComparison.Ordinal));
                ClickNode(Node(n => n.GetProperty("type").GetString() == "ControlType.TabItem" && n.GetProperty("name").GetString() == "Greeting.cs"));
                Inspect("mouse-08-tab-first", p => p.GetProperty("activeText").GetString()!.Contains("Greeting.cs", StringComparison.Ordinal));
                MouseCheck(EditorMatches(original), "Pointer document tab switching preserves source");
                stage = "drag source selection";
                var code = Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original);
                var codeOrigin = Preview().GetProperty("sourceClientOrigin"); int codeX = (int)code.GetProperty("x").GetDouble() - codeOrigin.GetProperty("X").GetInt32(), codeY = (int)code.GetProperty("y").GetDouble() - codeOrigin.GetProperty("Y").GetInt32();
                var dragStart = CanvasPoint(codeX + 2, codeY + 8); var dragEnd = CanvasPoint(codeX + 110, codeY + 8);
                long dragSequence = Preview().GetProperty("inputSequence").GetInt64();
                qa.SendMouseEvent(canvas, MouseMessage.LeftDown, dragStart.X, dragStart.Y, 1); qa.SendMouseEvent(canvas, MouseMessage.Move, dragEnd.X, dragEnd.Y, 1); qa.SendMouseEvent(canvas, MouseMessage.LeftUp, dragEnd.X, dragEnd.Y); Ack(dragSequence, "text drag");
                Inspect("mouse-09-drag-selection", _ => Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original).GetProperty("selection").EnumerateArray().Any(s => (s.GetString() ?? "").Length > 0));
                var selected = Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original).GetProperty("selection").EnumerateArray().Select(s => isolated?System.Text.RegularExpressions.Regex.Replace(s.GetString()??"",@"(?m)^\d+ ",""):s.GetString()??"").ToArray();
                string? substring=selected.FirstOrDefault(s=>s.Length>0&&s.Length<original.Length&&original.Contains(s,StringComparison.Ordinal));
                MouseCheck(substring!=null, "Dragging selects a proper source substring");
                Key(0x2E);string removed=original.Remove(original.IndexOf(substring!,StringComparison.Ordinal),substring!.Length);
                Inspect("mouse-09c-delete-selected-substring",_=>EditorMatches(removed));Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)==removed,"Drag deletion did not save the selected substring change");MouseCheck(File.ReadAllText(setup.First)==removed,"Dragging then Delete changes the actual saved substring");
                Chord(0x11,0x5A);Inspect("mouse-09d-undo-drag-deletion",_=>EditorMatches(original));Chord(0x11,0x53);Wait(()=>File.ReadAllText(setup.First)==original,"Undo did not restore the selected substring in the saved file");MouseCheck(File.ReadAllText(setup.First)==original,"Undo restores the exact saved source after drag deletion");
                stage = "mouse wheel scrolling";
                string visibleBefore = string.Join("", code.GetProperty("visibleText").EnumerateArray().Select(s => s.GetString()));
                var wheelPoint = CanvasPoint(codeX + 200, codeY + 180); long wheelSequence = Preview().GetProperty("inputSequence").GetInt64(); qa.SendMouseEvent(canvas, MouseMessage.Wheel, wheelPoint.X, wheelPoint.Y, wheelDelta: -600); Ack(wheelSequence, "wheel down");
                Thread.Sleep(400); Inspect("mouse-09a-wheel-down");
                var scrolled = Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original);
                string visibleAfter = string.Join("", scrolled.GetProperty("visibleText").EnumerateArray().Select(s => s.GetString()));
                MouseCheck(visibleBefore.Length > 0 && visibleAfter.Length > 0 && visibleBefore != visibleAfter, "Mouse wheel changes actual visible source text", false);
                wheelSequence = Preview().GetProperty("inputSequence").GetInt64(); qa.SendMouseEvent(canvas, MouseMessage.Wheel, wheelPoint.X, wheelPoint.Y, wheelDelta: 600); Ack(wheelSequence, "wheel up"); Thread.Sleep(400); Inspect("mouse-09b-wheel-up");
                MouseCheck(string.Join("", Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original).GetProperty("visibleText").EnumerateArray().Select(s => s.GetString())) == visibleBefore, "Mouse wheel returns to original visible source", false);
                stage = "editor context menu";
                var context = CanvasPoint(codeX + 80, codeY + 8); long contextSequence = Preview().GetProperty("inputSequence").GetInt64(); qa.SendMouseEvent(canvas, MouseMessage.RightDown, context.X, context.Y, 2); qa.SendMouseEvent(canvas, MouseMessage.RightUp, context.X, context.Y); Ack(contextSequence, "right click");
                Inspect("mouse-10-context", p => p.GetProperty("popupSurfaces").EnumerateArray().Any(s => s.GetProperty("Height").GetInt32() > 100));
                var viewCode = Node(n => n.GetProperty("type").GetString() == "ControlType.MenuItem" && n.GetProperty("name").GetString() == "View Code");
                MouseCheck(viewCode.GetProperty("width").GetDouble() > 0, "Right-click context menu exposes its actual command");
                ClickNode(viewCode);
                Inspect("mouse-11-context-command", p => !p.GetProperty("popupSurfaces").EnumerateArray().Any(s => s.GetProperty("Height").GetInt32() > 100));
                MouseCheck(FocusedCode(original), "Context menu View Code closes menu and focuses unchanged source");
            }
            if (Preview().GetProperty("activeClass").GetString() == "#32770")
            {
                // Acknowledge the ordinary project-load warning, not an account or consent dialog.
                using var observation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
                if (!observation.RootElement.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("name").GetString()!.Contains("projects in the solution were not loaded correctly", StringComparison.Ordinal))) throw new InvalidOperationException("Unexpected startup dialog; no automatic acceptance.");
                Key(0x0D); Inspect("02-project-warning-dismissed");
            }
            if (!startupDocument)
            {
            stage = "open code file through dialog"; Click(350, 130); Chord(0x11, 0x4F); Inspect("03-open-file-attempt", standard ? p => p.GetProperty("activeClass").GetString() == "#32770" && p.GetProperty("activeText").GetString()!.Contains("Open", StringComparison.OrdinalIgnoreCase) : null);
            pickerObserved = Preview().GetProperty("activeClass").GetString() == "#32770" && Preview().GetProperty("activeText").GetString()!.Contains("Open", StringComparison.OrdinalIgnoreCase);
            if (!pickerObserved) throw new InvalidOperationException("Ctrl+O did not produce the ordinary Open File dialog in the wrapper.");
            var preview = Preview(); var field = preview.GetProperty("inputWindow"); var origin = preview.GetProperty("sourceClientOrigin");
            if (!field.GetProperty("ClassName").GetString()!.Contains("Edit", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Open dialog did not expose a focused filename edit control.");
            Click(field.GetProperty("X").GetInt32() - origin.GetProperty("X").GetInt32() + field.GetProperty("Width").GetInt32() / 2, field.GetProperty("Y").GetInt32() - origin.GetProperty("Y").GetInt32() + field.GetProperty("Height").GetInt32() / 2);
            Chord(0x11, 0x41); qa.PostKeyboardInput(canvas, setup.First); Thread.Sleep(250); Key(0x0D); Inspect("04-code-file-open-attempt", standard ? p => p.GetProperty("activeClass").GetString() != "#32770" && p.GetProperty("activeText").GetString()!.Contains("Greeting.cs", StringComparison.Ordinal) : null);
            if (Preview().GetProperty("activeClass").GetString() == "#32770") throw new InvalidOperationException("A dialog remained after opening the code file; inspect evidence before further input.");
            }
            if (fullWorkflow)
            {
                stage = "pointer caret placement in opened editor"; Chord(0x11, 0x41); Inspect("04a-pointer-select-all", _ => Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original).GetProperty("selectionCoversDocument").EnumerateArray().Any(s => s.GetBoolean()));
                using var pointerObservation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
                var editor = pointerObservation.RootElement.GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == original);
                if (!editor.GetProperty("selectionCoversDocument").EnumerateArray().Any(s => s.GetBoolean())) throw new InvalidOperationException("Pointer test requires a verified whole-document selection.");
                var pointerOrigin = Preview().GetProperty("sourceClientOrigin");
                Click((int)editor.GetProperty("x").GetDouble() + 120 - pointerOrigin.GetProperty("X").GetInt32(), (int)editor.GetProperty("y").GetDouble() + 8 - pointerOrigin.GetProperty("Y").GetInt32());
                Inspect("04b-pointer-caret");
                const string marker = "/* viewer pointer */";
                long beforeMarker=Preview().GetProperty("inputSequence").GetInt64();
                qa.PostKeyboardInput(canvas, marker);
                Wait(()=>Preview().GetProperty("inputSequence").GetInt64()>=beforeMarker+marker.Length,"Pointer marker did not finish forwarding.");
                // A synchronous test shortcut can overtake posted WM_CHARs.
                // Observe the real editor before the dependent Save, as in the
                // ordinary document-editing case; 250 ms is not an acknowledgement.
                Inspect("04bb-pointer-typed",_=>Node(n=>n.GetProperty("id").GetString()=="WpfTextView"&&ObservedText(n).Contains(marker,StringComparison.Ordinal)).ValueKind!=JsonValueKind.Undefined);
                Chord(0x11, 0x53);
                Wait(() => File.ReadAllText(setup.First).Contains(marker, StringComparison.Ordinal), "Pointer test text was not saved.");
                string clicked = File.ReadAllText(setup.First); int offset = clicked.IndexOf(marker, StringComparison.Ordinal);
                pointerCaret = offset > 0 && clicked.Remove(offset, marker.Length) == original;
                if (!pointerCaret) throw new InvalidOperationException("The viewer click did not place the caret while preserving the original source.");
                Inspect("04c-pointer-saved");
            }
            stage = "edit and save code"; SaveDocument(setup.First, expectedFirst, "05-saved-code"); editing = true;
            if (fullWorkflow)
            {
                stage = "second file editing"; OpenDocument(setup.Second, "06-second-file"); SaveDocument(setup.Second, expectedSecond, "07-second-file-saved");
                stage = "Ctrl+Tab document switch"; Chord(0x11, 0x09);
                Inspect("08-ctrl-tab-first", p => p.GetProperty("activeText").GetString()!.Contains("Greeting.cs", StringComparison.Ordinal));
                if (!EditorMatches(expectedFirst)) throw new InvalidOperationException("Ctrl+Tab did not reveal the independently saved first document.");
                stage = "close and reopen"; Chord(0x11, 0x73); Thread.Sleep(400); OpenDocument(setup.First, "09-reopen-first"); navigation = true;
                stage = "IDE build through viewer";
                string outputType=System.Xml.Linq.XDocument.Load(Path.Combine(Path.GetDirectoryName(setup.First)!,"DisposableProject.csproj")).Descendants().Single(e=>e.Name.LocalName=="OutputType").Value;
                string dll = Path.Combine(Path.GetDirectoryName(setup.First)!, "bin", "Debug", "ViavwDisposable"+(outputType is "Exe" or "WinExe"?".exe":".dll"));
                if (File.Exists(dll)) throw new InvalidOperationException("Use a fresh solution with no build output for the IDE build test.");
                var watch = Stopwatch.StartNew(); Chord(0x11, 0x10, 0x42); Wait(() => File.Exists(dll), "Viewer build shortcut did not produce an assembly.", 45000);
                var constants = VisualStudioResearch.ReadCompiledConstants(dll);
                ideBuild = constants.GetValueOrDefault("Text") == "Viewer-edited greeting" && constants.GetValueOrDefault("Status") == "Viewer reviewed";
                if (!ideBuild) throw new InvalidOperationException("IDE build did not contain both edited source constants.");
                buildEvidence = new { shortcut = "Ctrl+Shift+B", milliseconds = watch.Elapsed.TotalMilliseconds, dll, constants, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))), externalBuildProcessInvokedByHarness = false };
                Inspect("10-ide-built-code");
                using var buildObservation = JsonDocument.Parse(SnapshotFiles.ReadText(Path.Combine(output, "viewer-accessible.json")));
                if (!buildObservation.RootElement.GetProperty("nodes").EnumerateArray().Any(n => (n.GetProperty("text").GetString() ?? "").Contains("1 succeeded, 0 failed, 0 up-to-date, 0 skipped", StringComparison.Ordinal))) throw new InvalidOperationException("IDE Output did not verify one successful project build.");
                Inspect("11-build-output-complete");
            }
            if (standard)
            {
                if (fullWorkflow)
                {
                    stage = "return from Output through Open File";
                    if (mouseWorkflow)
                    {
                        stage = "mouse return from Output to code";
                        var code = Node(n => n.GetProperty("id").GetString() == "WpfTextView" && ObservedText(n) == expectedFirst);
                        var codeOrigin = Preview().GetProperty("sourceClientOrigin");
                        Click((int)code.GetProperty("x").GetDouble() + 120 - codeOrigin.GetProperty("X").GetInt32(), (int)code.GetProperty("y").GetDouble() + 8 - codeOrigin.GetProperty("Y").GetInt32());
                        Inspect("12-mouse-return-to-code"); MouseCheck(FocusedCode(expectedFirst), "Mouse restores code focus after IDE build");
                    }
                    else OpenDocument(setup.First, "12-return-to-code");
                    if (!FocusedCode(expectedFirst)) throw new InvalidOperationException("Code editor must have private focus before testing rejected picture input.");
                    stage = "edit after IDE build";
                    expectedFirst += " // Edited after IDE build";
                    SaveDocument(setup.First, expectedFirst, "13-post-build-edit"); postBuildEdit = EditorMatches(expectedFirst);
                    if (!postBuildEdit) throw new InvalidOperationException("Post-build code editor state differs from its saved content.");
                }
                stage = "ownership handoff"; int sourcePid = Preview().GetProperty("applicationPid").GetInt32();
                Button("Return to automation"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Automation owns input", StringComparison.Ordinal) || w.Text.StartsWith("Automation | PID ", StringComparison.Ordinal))), "Automation ownership was not applied.");
                qa.PostKeyboardInput(canvas, "REJECTED_HUMAN"); Thread.Sleep(250);
                if(standard&&isolated)qa.EnablePrivateInputSession();Button("Human control"); Wait(() => qa.GetWindows().Any(w => (w.Text.Contains("Human owns input", StringComparison.Ordinal) || w.Text.StartsWith("Human | PID ", StringComparison.Ordinal))), "Human ownership was not restored.");
                Chord(0x11, 0x53); Thread.Sleep(300); Inspect("06-handoff-preserved");
                handoff = File.ReadAllText(setup.First) == expectedFirst && Preview().GetProperty("applicationPid").GetInt32() == sourcePid;
                if (!handoff) throw new InvalidOperationException("Ownership handoff did not preserve the same editor and saved document.");
                Button("Return to automation"); stage = "complete";
            }
        }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine(failure); }
        finally
        {
            if (!qa.Closed) { owned = qa.GetProcessIds(); if (viewer != 0) try { File.WriteAllBytes(Path.Combine(output, "final-viewer.png"), qa.CaptureVisualOutput(viewer).Png); } catch { } try { graceful = qa.CloseGracefully(TimeSpan.FromSeconds(6)); } catch { } }
            qa.Dispose(); Thread.Sleep(250);
        }
        var physical = monitor.Complete(owned); var after = VisualStudioResearch.PersonalSettings(allowUnavailable:isolated); bool personalUnchanged = personal.Count == after.Count && personal.All(kv => after.TryGetValue(kv.Key, out var value) && value == kv.Value);
        bool readablePersonalSettingsUnchanged=personal.Where(kv=>!kv.Value.StartsWith("unavailable:")).All(kv=>after.GetValueOrDefault(kv.Key)==kv.Value);
        bool personalSettingsFullyObserved=!personal.Values.Concat(after.Values).Any(v=>v.StartsWith("unavailable:"));
        personalUnchanged&=personalSettingsFullyObserved;
        bool cleanup = owned.All(id => !CompatibilityVerification.Alive(id));
        bool workflowPassed = editing && failure == null && (!fullWorkflow || navigation && ideBuild && postBuildEdit && pointerCaret) && (!standard || handoff && startupDocument) && (!mouseWorkflow || mouseChecks.Values.All(passed => passed));
        File.WriteAllText(Path.Combine(output, "vs-workflow.json"), JsonSerializer.Serialize(new { workflowPassed, fullWorkflow, mouseWorkflow,windowDcPreview,asyncStatusReports,preciseMouseWaits,viewerExecutable, mouseChecks, startupDocument, pointerCaret, navigation, ideBuild, postBuildEdit, buildEvidence, releaseStateBeforeKeyUp = standard, standardPackages = standard, inheritedEnvironment = standard, handoff, stage, failure, pickerObserved, editing, sourceChanged = File.ReadAllText(setup.First) != original, frames, graceful, cleanup, owned, physical, personalUnchanged,readablePersonalSettingsUnchanged,personalSettingsFullyObserved, personalBefore = personal, personalAfter = after, unavailableSettings=personal.Where(kv=>kv.Value.StartsWith("unavailable:")).Select(kv=>kv.Key), setupFile, instrumentation = "none", physicalHumanAcceptance = "pending; user reported prior mouse interaction unusable" }, new JsonSerializerOptions { WriteIndented = true }));
        return workflowPassed && cleanup && readablePersonalSettingsUnchanged && physical.GetProperty("passed").GetBoolean() ? 0 : 1;
    }
}

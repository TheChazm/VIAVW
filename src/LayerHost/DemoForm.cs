namespace LayerHost;

internal sealed partial class DemoForm : Form
{
    readonly LayerManager manager = new();
    ApplicationLayer layer = null!;
    readonly SemaphoreSlim commands = new(1, 1);
    readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly ComboBox surfaces = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(900, 0) };
    readonly InputCanvas canvas = new() { BackColor = Color.FromArgb(40, 40, 40), TabStop = true };
    readonly TextBox text = new() { Text = "Hello from virtual input", Width = 240 };
    readonly System.Windows.Forms.Timer automation = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 150 };
    readonly bool hidden;
    readonly CaptureBackend backend;
    readonly string? showFile;
    readonly string? reportDirectory;
    readonly bool observeAccessible;
    readonly int inspectionSettle;
    readonly int qivNativeSettle;
    readonly ResearchGpuTarget? gpuTarget;
    readonly bool nativePopupPointer;
    readonly bool responsivePreview;
    readonly bool directPreview,repaintPreview,accessibleModalInput;
    readonly bool independentPreview,softwareClientPreview,windowDcPreview,captureOriginReport;
    bool automaticPreviewBusy;
    long nextWindowObservation;
    PreviewPixelProbe? pixelProbe;
    ViewerTimingTrace? timing;
    LatestSnapshotPublisher? statusPublisher;
    long paintedFrame;
    int? paintedChangedPixels;
    readonly bool incrementalPaint;
    readonly bool deadlinePreview;
    readonly int deadlineFirstMilliseconds;
    readonly int deadlineFollowupMilliseconds;
    readonly bool immediatePreviewPaint;
    readonly string? preferredTextAutomationId;
    readonly bool nativeSubtreeGeometry;
    readonly string? initialPointerTitleFragment;
    readonly string? initialPointerAutomationId;
    long? initialPointerTicket;
    long initialPointerReadRoot,initialPointerRetryDue;
    int initialPointerReadAttempts;
    bool initialPointerPreparationAttempted;
    object? initialPointerPreparation;
    PreviewDeadlineDispatcher? deadlineDispatcher;
    string? deadlineFault;
    readonly string functionKeyPolicy;
    readonly int chordHoldPolicy;
    long deadlineRefreshDue,deadlineInputUntil,deadlineNextInput;
    static long PreviewQpc=>System.Diagnostics.Stopwatch.GetTimestamp();
    static long PreviewTicks(int milliseconds)=>(long)(milliseconds*System.Diagnostics.Stopwatch.Frequency/1000.0);
    object? previewDamage;
    long observationVersion;
    long startupPreviewUntil, startupNextPreview;
    long contentWatchUntil, nextContentPreview;
    long inputPreviewUntil,nextInputPreview;
    readonly System.Windows.Forms.Timer hoverTimer = new() { Interval = 100 };
    PopupSurface[] popupSurfaces = [];
    string publishedPopupSignature = "";
    long nativeMainRoot;
    readonly List<RectangleF> textRegions = [];
    readonly List<RectangleF> pressRegions = [];
    object? lastPointerMetrics;
    long textRegionRoot;
    Point textRegionOrigin;
    (Point Point, long Epoch)? pendingHover;
    bool hoverQueued;
    GpuBridgeResearchSession? gpu;
    QivCursorGuardSession? qivGuard;
    bool qivNativeInspection;
    string gpuCapturePath = "";
    long gpuRoot;
    long preferredSurface, activeMenuOwner, nativeSurfaceRoot;
    object? lastForwardedInput;
    object? lastForwardedChord;
    sealed class TextBatch(long destination,string first)
    {
        internal readonly long Destination=destination;
        internal readonly System.Text.StringBuilder Text=new(first);
    }
    TextBatch? pendingTextBatch;
    long textBatchCommands,textBatchCharacters;
    int largestTextBatch;
    bool updatingSurfaces;
    string surfaceKind = "main";
    string publishedWindowText = "";
    string publishedSourceClass = "";
    System.Text.Json.JsonElement? gpuMetrics;
    long root, inputTarget, previewVersion, inputSequence, refreshNotBefore, windowWatchUntil;
    int sequence;
    bool preview = true, refreshQueued, disposed, allowClose, closePending;
    ControlOwner desiredOwner = ControlOwner.Automation;
    readonly List<Point> pointerGesture = [];
    long ownershipEpoch, gestureEpoch;
    int gestureModifiers;
    MouseButtons gestureButton;
    int gestureClicks;
    (long Root, long Window, int X, int Y)? privateTextPoint;
    public event Action<string?>? StartupCompleted;
    internal int[] OwnedProcesses() => layer == null || layer.Closed ? [] : layer.GetProcessIds();

    public DemoForm(string exe, string[] args, bool hidden = false, CaptureBackend backend = CaptureBackend.PrintWindow, string? showFile = null, bool qtSoftware = false, bool queuePointer = false, bool rootInput = false, bool wpfSoftwareHook = false, string? qaReportDirectory = null, bool hiddenNotifications = false, int inspectionSettleMilliseconds = 150, ResearchGpuTarget? experimentalGpuTarget = null, bool qivNativeStartup = false, bool postedShortcuts = false, bool fitPreview = false, int startupTimeoutMilliseconds = 8000, bool diagnosticInput = false, bool releaseStateBeforeKeyUp = false, string? humanControlHint = null, bool nativePopupPointer = false, bool responsivePreview = false, bool directPreview = false, bool measurePixels = false, bool repaintPreview = false, bool accessibleModalInput = false, bool activatePrivate = false, bool lowLatencyInput = false, bool softwareClientPreview = false, bool windowDcPreview = false, bool measureTiming = false, bool captureOriginReport = false, bool asyncStatusReports = false, bool preciseMouseWaits = false, bool fastPopupGeometry = false, bool incrementalPaint = false, bool deadlinePreview = false, int deadlineFirstMilliseconds = 16, bool splitPreviewClips = false,int preciseChordHoldMilliseconds=0,bool postedFunctionKeys=false,bool postedFunctionTransitions=false,int deadlineFollowupMilliseconds=16,bool immediatePreviewPaint=false,string? preferredTextAutomationId=null,bool nativeSubtreeGeometry=false,string? initialPointerTitleFragment=null,string? initialPointerAutomationId=null)
    {
        if(deadlineFollowupMilliseconds is <4 or >16)throw new ArgumentOutOfRangeException(nameof(deadlineFollowupMilliseconds));
        this.deadlineFollowupMilliseconds=deadlineFollowupMilliseconds;this.immediatePreviewPaint=immediatePreviewPaint;this.preferredTextAutomationId=preferredTextAutomationId;this.nativeSubtreeGeometry=nativeSubtreeGeometry;this.initialPointerTitleFragment=initialPointerTitleFragment;this.initialPointerAutomationId=initialPointerAutomationId;
        if(deadlineFirstMilliseconds is <0 or >32)throw new ArgumentOutOfRangeException(nameof(deadlineFirstMilliseconds));
        if(preciseChordHoldMilliseconds!=0&&preciseChordHoldMilliseconds is <16 or >40)throw new ArgumentOutOfRangeException(nameof(preciseChordHoldMilliseconds));
        functionKeyPolicy=!postedFunctionKeys?"stateful paced":postedFunctionTransitions?"experimental posted function keys":"posted F9/F10; paced transitions";chordHoldPolicy=preciseChordHoldMilliseconds;
        canvas.SplitPreviewClips=splitPreviewClips;
        this.deadlinePreview=deadlinePreview;this.deadlineFirstMilliseconds=deadlineFirstMilliseconds;
        this.incrementalPaint=incrementalPaint;
        if (startupTimeoutMilliseconds is < 1000 or > 120000) throw new ArgumentException("Bound application startup to 1–120 seconds.");
        if (inspectionSettleMilliseconds is < 0 or > 500) throw new ArgumentException("Bound private inspection settle to 0–500 ms.");
        inspectionSettle = inspectionSettleMilliseconds;
        if(qivNativeStartup&&experimentalGpuTarget!=ResearchGpuTarget.Qiv)throw new ArgumentException("Guard-only startup requires the pinned qIV option.");
        qivNativeSettle=qivNativeStartup?inspectionSettleMilliseconds:0;
        this.hidden = hidden; this.backend = backend; this.showFile = showFile;
        gpuTarget = experimentalGpuTarget;
        this.nativePopupPointer = nativePopupPointer;
        this.responsivePreview = responsivePreview;
        this.directPreview = directPreview;this.repaintPreview=repaintPreview;this.accessibleModalInput=accessibleModalInput;
        // The older explicit redraw experiment must keep its serialized
        // repaint operation; automatic copy requests do not perform redraw.
        this.captureOriginReport=captureOriginReport;
        independentPreview=lowLatencyInput&&directPreview&&!repaintPreview;this.softwareClientPreview=softwareClientPreview;this.windowDcPreview=windowDcPreview;
        canvas.IgnoreSystemCharacters = responsivePreview;
        if (responsivePreview && !nativePopupPointer) throw new ArgumentException("Responsive preview currently requires the private native popup backend.");
        if (responsivePreview) previewTimer.Interval = 10;
        if (nativePopupPointer && (!queuePointer || experimentalGpuTarget != null)) throw new ArgumentException("Native popup pointer mode requires the private queue-pointer backend.");
        canvas.FitOversizedImages = fitPreview || gpuTarget == ResearchGpuTarget.Qiv;
        bool UseRootInput() => rootInput && (gpuTarget == null || root == gpuRoot);
        Task<(long Window, int X, int Y)> PointerHit(Point point) => Task.Run(() => UseRootInput() ? (root, point.X, point.Y) : nativePopupPointer ? layer.HitTestPointer(root, point.X, point.Y) : layer.HitTest(root, point.X, point.Y));
        async Task ForwardHoverAsync()
        {
            if (hoverQueued || pendingHover == null || popupSurfaces.Length == 0 || desiredOwner != ControlOwner.Human || pointerGesture.Count != 0 || !preview) return;
            hoverQueued = true;
            try
            {
                await Execute(async () =>
                {
                    var hover = pendingHover; pendingHover = null;
                    if (hover == null || hover.Value.Epoch != ownershipEpoch || disposed || desiredOwner != ControlOwner.Human || pointerGesture.Count != 0) return;
                    var hit = await PointerHit(hover.Value.Point);
                    await Task.Run(() => layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y)], true, ControlOwner.Human, warmupCapture: true));
                    ScheduleRefresh(true);
                });
            }
            finally { hoverQueued = false; }
        }
        async Task TraceInput(string kind, long target, object detail)
        {
            if (!diagnosticInput || qaReportDirectory == null) return;
            var thread = await Task.Run(() => layer.ObserveWindowThread(root));
            var roots = await Task.Run(() => layer.GetWindows().Where(w => w.Parent == 0 && w.Visible).ToArray());
            await File.AppendAllTextAsync(Path.Combine(qaReportDirectory, "input-trace.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { kind, root, target, thread, roots, detail }) + Environment.NewLine);
        }
        void RequireMainWindow()
        {
            if (gpuTarget != null && root != gpuRoot) throw new InvalidOperationException("Close the owned dialog before controlling the main window or changing its capture backend.");
        }
        if(measureTiming){if(!measurePixels)throw new ArgumentException("Stage timing requires the QA pixel probe.");timing=new(qaReportDirectory!);canvas.PaintTiming=(ending,message)=>timing.Record((message==0xF?"paint-":"print-")+(ending?"end":"start"),paintedFrame,inputSequence,paintedChangedPixels);}
        if(measurePixels) pixelProbe=new(qaReportDirectory??throw new ArgumentException("Pixel telemetry requires a QA directory."));
        reportDirectory = qaReportDirectory ?? (args.Contains("--report") ? Environment.CurrentDirectory : null); observeAccessible = qaReportDirectory != null;
        if(asyncStatusReports){if(!responsivePreview||reportDirectory==null)throw new ArgumentException("Asynchronous reports require a responsive diagnostic viewer.");statusPublisher=new(async(version,snapshot)=>{timing?.Record("background-report-start",version,-1);try{await SnapshotFiles.ReplaceTextAsync(Path.Combine(reportDirectory,"viewer-preview.json"),snapshot).ConfigureAwait(false);}finally{timing?.Record("background-report-end",version,-1);}});}
        Text = "VIAVW — private application layer"; ClientSize = gpuTarget == ResearchGpuTarget.Qiv ? new Size(1050, 850) : new Size(950, 570);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        AddButton(toolbar, "Refresh", RefreshAsync);
        if (responsivePreview) AddButton(toolbar, "Inspect details", () => RefreshCoreAsync(false, detailed: true));
        if (gpuTarget == ResearchGpuTarget.Qiv || nativePopupPointer)
        {
            toolbar.Controls.Add(new Label { Text = "Window", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); toolbar.Controls.Add(surfaces);
            surfaces.SelectedIndexChanged += async (_, _) =>
            {
                if (updatingSurfaces || surfaces.SelectedItem is not WindowChoice choice) return;
                preferredSurface = choice.Window.Handle;
                if (nativePopupPointer) nativeSurfaceRoot = choice.Window.Handle;
                await Execute(RefreshAsync);
            };
            if (nativePopupPointer) AddButton(toolbar, "Next window", async () =>
            {
                var choices = surfaces.Items.Cast<WindowChoice>().ToArray();
                if (choices.Length < 2) return;
                int index = Array.FindIndex(choices, c => c.Window.Handle == (nativeSurfaceRoot != 0 ? nativeSurfaceRoot : nativeMainRoot));
                nativeSurfaceRoot = preferredSurface = choices[(index + 1) % choices.Length].Window.Handle;
                await RefreshAsync(); canvas.Focus();
            });
        }
        toolbar.Controls.Add(windows);
        if (gpuTarget != ResearchGpuTarget.Qiv)
        {
            toolbar.Controls.Add(text);
            AddButton(toolbar, "Automation: send text", async () => { string value = text.Text; long target = inputTarget; await Task.Run(() => layer.SendKeyboardInput(target, value)); await RefreshAsync(); });
        }
        if (hiddenNotifications) AddButton(toolbar, "Replace hidden field", async () =>
        {
            refreshQueued = false; string value = text.Text; long target = inputTarget;
            var result = await Task.Run(() => layer.ResearchHiddenInteraction(root, [new(PresentedInputKind.KeyChord, Window: target, Keys: [0x11, 0x41]), new(PresentedInputKind.Text, Window: target, Text: value)], settleMilliseconds: 0));
            if (preview) await RefreshCoreAsync(true);
            else SetStatus($"Hidden keyboard input delivered in {result.Milliseconds:F1} ms. Use Inspect hidden app to view the result.");
        });
        if (gpuTarget == null) AddButton(toolbar, "Send without clicking", async () =>
        {
            refreshQueued = false; string value = text.Text; long target = inputTarget;
            var result = await Task.Run(() => preview ? layer.PresentForInput(root, [new(PresentedInputKind.Text, Window: target, Text: value)], fullContent: backend == CaptureBackend.PrintWindowFullContent, notifyActivation: true) : layer.SendActivatedInput(root, [new(PresentedInputKind.Text, Window: target, Text: value)]));
            if (result.Frame != null)
            {
                var source = (await Task.Run(layer.GetWindows)).FirstOrDefault(window => window.Handle == root);
                if (source != null) await PublishFrameAsync(result.Frame, source);
            }
            else SetStatus($"Private input delivered at the retained selection in {result.Milliseconds:F1} ms. Preview is hidden; no source presentation or capture requested.");
        });
        if (gpuTarget == null) AddButton(toolbar, "Click and send privately", async () =>
        {
            if (privateTextPoint is not { } point || point.Root != root) throw new InvalidOperationException("Take Human control and click the desired text field in the picture first. This action repeats that click and sends the toolbar text; return ownership to automation to use it.");
            refreshQueued = false; string value = text.Text;
            var result = await Task.Run(() => layer.PresentForInput(root, [new(PresentedInputKind.MouseSequence, Window: point.Window, Steps: [new(MouseMessage.Move, point.X, point.Y), new(MouseMessage.LeftDown, point.X, point.Y, 1), new(MouseMessage.LeftUp, point.X, point.Y)]), new(PresentedInputKind.Text, Window: point.Window, Text: value)], captureAfter: preview, fullContent: backend == CaptureBackend.PrintWindowFullContent));
            if (result.Frame != null)
            {
                var source = (await Task.Run(layer.GetWindows)).FirstOrDefault(window => window.Handle == root);
                if (source != null) await PublishFrameAsync(result.Frame, source);
            }
            else SetStatus($"Native private input burst delivered in {result.Milliseconds:F1} ms; source visibility restored. Preview is hidden.");
        });
        if (gpuTarget == null)
        {
            AddButton(toolbar, "Run typing test", () => { automation.Start(); SetStatus("Automation sends one character every 250 ms. Type in another foreground application; return to stop and inspect."); return Task.CompletedTask; });
            AddButton(toolbar, "Stop typing test", async () => { automation.Stop(); await RefreshAsync(); });
        }
        else
        {
            if (gpuTarget == ResearchGpuTarget.Mpv)
            {
                AddButton(toolbar, "Automation: step frame", async () => { await Task.Run(() => layer.SendKeyboardInput(root, ".")); ScheduleRefresh(); });
                AddButton(toolbar, "Automation: play / pause", async () => { await Task.Run(() => layer.SendKey(root, 0x20)); ScheduleRefresh(); });
            }
            if (gpuTarget == ResearchGpuTarget.Qiv)
            {
                AddButton(toolbar, "Automation: toggle help", async () => { await Task.Run(() => layer.PostKey(gpuRoot, 0x70)); ScheduleRefresh(true); });
                AddButton(toolbar, "Automation: toggle directory", async () => { await Task.Run(() => layer.PostKey(gpuRoot, 0x75)); ScheduleRefresh(true); });
                AddButton(toolbar, "Automation: window menu", async () => { if(surfaceKind != "modal" || activeMenuOwner != 0) throw new InvalidOperationException("Open an owned file dialog with a window menu first."); await Task.Run(() => layer.SendQueueStateChord(root, [0x12, 0x20], posted: true)); ScheduleRefresh(true); });
                AddButton(toolbar, "Automation: next image", async () => { RequireMainWindow(); await Task.Run(() => QivResearch.Wheel(layer, root, -120)); ScheduleRefresh(); });
                AddButton(toolbar, "Automation: zoom in", async () => { RequireMainWindow(); await Task.Run(() => QivResearch.Wheel(layer, root, 120, 8)); ScheduleRefresh(); });
                AddButton(toolbar, "Automation: reset zoom", async () => { RequireMainWindow(); await Task.Run(() => layer.SendKey(root, 0x6A)); ScheduleRefresh(); });
                AddButton(toolbar, "Automation: open file dialog", async () => { RequireMainWindow(); await Task.Run(() => layer.PostKey(root, 0x71)); ScheduleRefresh(true); });
                AddButton(toolbar, "Automation: cancel dialog", async () => { if(root == gpuRoot) throw new InvalidOperationException("No owned dialog is selected."); await Task.Run(() => layer.PostKey(root, 0x1B)); ScheduleRefresh(true); });
                AddButton(toolbar, "Use native inspection", async () =>
                {
                    RequireMainWindow();
                    if (!qivNativeInspection)
                    {
                        if (gpu == null) throw new InvalidOperationException("GPU capture is unavailable. Recover this session before selecting native inspection.");
                        await Task.Run(gpu.StopAdapter);
                        qivNativeInspection = true; gpuMetrics = null; await DisposeGpuAsync();
                    }
                    if (preview) await RefreshAsync(); else SetStatus("Native inspection selected. GPU reader closed; preview remains hidden.");
                });
                AddButton(toolbar, "Use hidden GPU capture", async () =>
                {
                    RequireMainWindow();
                    if (qivNativeInspection)
                    {
                        await Task.Run(() => gpu = new(layer, root, ResearchGpuTarget.Qiv)); gpuRoot = root; qivNativeInspection = false;
                    }
                    if (preview) await RefreshAsync(); else SetStatus("Hidden GPU capture selected. Preview remains hidden.");
                });
            }
            foreach (var size in new[] { new Size(480, 270), new Size(960, 540), new Size(640, 360) })
                AddButton(toolbar, $"GPU size: {size.Width} × {size.Height}", async () => { RequireMainWindow(); if (gpu == null) throw new InvalidOperationException("Select hidden GPU capture before resizing."); await Task.Run(() => gpu.ResizeClient(size.Width, size.Height)); await RefreshAsync(); });
            AddButton(toolbar, "Recover GPU reader", async () => { if (gpu == null) throw new InvalidOperationException("Native inspection has no GPU reader. Select hidden GPU capture to create one."); await Task.Run(() => gpu.RestartHelper()); if (preview) await RefreshAsync(); else SetStatus("GPU reader recovered. Preview remains hidden."); });
            AddButton(toolbar, "Recover input worker", async () => { await Task.Run(layer.RestartWorker); if (preview) await RefreshAsync(); else SetStatus("Input worker recovered. Preview remains hidden."); });
        }
        void CancelGesture() { ownershipEpoch++; pointerGesture.Clear(); pendingHover = null; canvas.Capture = false; }
        canvas.Resize += (_, _) => { CancelGesture(); canvas.Invalidate(); };
        AddButton(toolbar, "Human control", async () => { automation.Stop(); CancelGesture(); desiredOwner = ControlOwner.Human; await Task.Run(layer.TakeHumanControl); SetStatus(activeMenuOwner != 0 ? "Human owns input. Use arrow keys and Escape for this menu." : humanControlHint ?? "Human owns input. Click or drag the picture, then type. Drags are delivered when released; framework compatibility varies."); });
        AddButton(toolbar, "Return to automation", async () => { CancelGesture(); desiredOwner = ControlOwner.Automation; await Task.Run(layer.ReturnToAutomation); SetStatus("Automation owns input; picture is read-only."); });
        AddButton(toolbar, "Hide preview", () => { CancelGesture(); preview = false; canvas.Image?.Dispose(); canvas.Image = null; canvas.Invalidate(); SetStatus("Preview hidden; no captures. Application continues on private desktop."); return Task.CompletedTask; });
        AddButton(toolbar, "Show preview", async () => { preview = true; await RefreshAsync(); });
        AddButton(toolbar, "Hide app window", async () => { await Task.Run(() => layer.HideLayer(root)); SetStatus(qivNativeInspection ? "Private window hidden. Native inspection temporarily presents it on its private desktop." : gpu == null ? "Private window hidden. Printing may produce blank pixels; workload may continue." : "Private window hidden. GPU inspection remains available on demand."); });
        AddButton(toolbar, "Inspect hidden app", async () => { refreshQueued = false; preview = true; await RefreshCoreAsync(true); });
        AddButton(toolbar, "Show app window", async () => { await Task.Run(() => layer.ShowLayer(root)); await RefreshAsync(); });
        AddButton(toolbar, "Request attention", async () => { await Task.Run(() => layer.RequestAttention("Please inspect this layer's configuration.")); SetStatus(layer.AttentionReason!); });
        AddButton(toolbar, "Force close", async () => { automation.Stop(); await DisposeGpuAsync(); await Task.Run(layer.Destroy); allowClose = true; Close(); });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(10) }; footer.Controls.Add(status);
        canvas.Dock = DockStyle.Fill; Controls.Add(canvas); Controls.Add(footer); Controls.Add(toolbar);
        windows.SelectedIndexChanged += (_, _) => { if (windows.SelectedItem is WindowChoice choice) inputTarget = choice.Window.Handle; };
        canvas.MouseDown += async (_, e) =>
        {
            if (activeMenuOwner != 0) { SetStatus("Use arrow keys and Escape for this menu."); return; }
            if (desiredOwner != ControlOwner.Human || (e.Button != MouseButtons.Left && (!nativePopupPointer || e.Button != MouseButtons.Right)) || !canvas.TryImagePoint(e.Location, out var imagePoint)) return;
            canvas.Focus();
            pendingHover = null;
            if (queuePointer)
            {
                pointerGesture.Clear(); pointerGesture.Add(imagePoint); gestureEpoch = ownershipEpoch;
                gestureButton = e.Button; gestureClicks = e.Clicks;
                gestureModifiers = (ModifierKeys.HasFlag(Keys.Shift) ? 4 : 0) | (ModifierKeys.HasFlag(Keys.Control) ? 8 : 0);
                canvas.Capture = true; return;
            }
            await Execute(async () =>
            {
                var hit = UseRootInput() ? (Window: root, X: imagePoint.X, Y: imagePoint.Y) : await Task.Run(() => layer.HitTest(root, imagePoint.X, imagePoint.Y)); inputTarget = hit.Window;
                privateTextPoint = (root, hit.Window, hit.X, hit.Y);
                if (queuePointer) await Task.Run(() => layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y)], true, ControlOwner.Human));
                else await Task.Run(() => layer.SendMouseInput(hit.Window, hit.X, hit.Y, ControlOwner.Human));
                inputSequence++;
                ScheduleRefresh();
            });
        };
        canvas.MouseMove += async (_, e) =>
        {
            if (!queuePointer || canvas.Image == null || desiredOwner != ControlOwner.Human) return;
            if (pointerGesture.Count == 0)
            {
                if (nativePopupPointer && canvas.TryImagePoint(e.Location, out var hover))
                {
                    pendingHover = (hover, ownershipEpoch);
                    if (responsivePreview) await ForwardHoverAsync();
                }
                return;
            }
            var point = canvas.ClampedImagePoint(e.Location);
            if (point == pointerGesture[^1]) return;
            if (pointerGesture.Count == 29) pointerGesture.RemoveAt(1); // Preserve origin and bounded recent path.
            pointerGesture.Add(point);
        };
        canvas.MouseCaptureChanged += (_, _) => { if (!canvas.Capture) pointerGesture.Clear(); };
        canvas.MouseUp += async (_, e) =>
        {
            if (!queuePointer || e.Button != gestureButton || pointerGesture.Count == 0 || canvas.Image == null) return;
            Point first = pointerGesture[0], last = canvas.ClampedImagePoint(e.Location);
            Point[] path = pointerGesture.Skip(1).ToArray(); long epoch = gestureEpoch; bool right = gestureButton == MouseButtons.Right; int modifiers = gestureModifiers, buttons = (right ? 2 : 1) | modifiers;
            bool doubleClick = nativePopupPointer && gestureClicks >= 2;
            pointerGesture.Clear(); canvas.Capture = false;
            var pointerClock = System.Diagnostics.Stopwatch.StartNew();
            await Execute(async () =>
            {
                if (disposed || desiredOwner != ControlOwner.Human || epoch != ownershipEpoch) return;
                double queuedMilliseconds = pointerClock.Elapsed.TotalMilliseconds;
                double geometryMilliseconds = 0;
                int? geometryVisited = null;
                bool geometryCacheHit = false,geometryHintUsed=false;
                // A click already classified as an owned popup always uses the
                // existing warmup path below; text/press geometry cannot change
                // its routing decision. PointerHit still revalidates live HWNDs.
                bool onPublishedPopup=popupSurfaces.Any(p=>new Rectangle(p.FrameX,p.FrameY,p.Width,p.Height).Contains(first)||new Rectangle(p.FrameX,p.FrameY,p.Width,p.Height).Contains(last));
                if (responsivePreview && !right && !(fastPopupGeometry&&onPublishedPopup))
                {
                    // Resolve current geometry only along owned branches under
                    // the pointer; do not scan the entire IDE for every click.
                    long geometryScope=nativeSubtreeGeometry?(await PointerHit(first)).Window:0;
                    var geometry = await Task.Run(() => layer.ObservePointerGeometry(root, first, last,root==nativeMainRoot?preferredTextAutomationId:null,geometryScope));
                    if (geometry.TryGetProperty("visited", out var visited)) geometryVisited = visited.GetInt32();
                    if (geometry.TryGetProperty("cached", out var cached)) geometryCacheHit = cached.GetBoolean();
                    if(geometry.TryGetProperty("hintUsed",out var hintUsed))geometryHintUsed=hintUsed.GetBoolean();
                    geometryMilliseconds = geometry.GetProperty("milliseconds").GetDouble();
                    var origin = geometry.GetProperty("origin"); textRegionOrigin = new(origin.GetProperty("X").GetInt32(), origin.GetProperty("Y").GetInt32()); textRegionRoot = root; textRegions.Clear();
                    foreach (var region in geometry.GetProperty("regions").EnumerateArray()) textRegions.Add(new((float)region.GetProperty("X").GetDouble(), (float)region.GetProperty("Y").GetDouble(), (float)region.GetProperty("Width").GetDouble(), (float)region.GetProperty("Height").GetDouble()));
                    pressRegions.Clear();
                    foreach (var region in geometry.GetProperty("pressRegions").EnumerateArray()) pressRegions.Add(new((float)region.GetProperty("X").GetDouble(), (float)region.GetProperty("Y").GetDouble(), (float)region.GetProperty("Width").GetDouble(), (float)region.GetProperty("Height").GetDouble()));
                }
                var hit = await PointerHit(first); inputTarget = hit.Window;
                if (!right && Math.Abs(last.X - first.X) < 4 && Math.Abs(last.Y - first.Y) < 4) privateTextPoint = (root, hit.Window, hit.X, hit.Y);
                int offsetX = hit.X - first.X, offsetY = hit.Y - first.Y;
                var down = right ? (doubleClick ? MouseMessage.RightDoubleClick : MouseMessage.RightDown) : (doubleClick ? MouseMessage.LeftDoubleClick : MouseMessage.LeftDown);
                var steps = new List<MouseStep> { new(MouseMessage.Move, hit.X, hit.Y), new(down, hit.X, hit.Y, buttons) };
                steps.AddRange(path.Select(point => new MouseStep(MouseMessage.Move, point.X + offsetX, point.Y + offsetY, buttons)));
                steps.Add(new(right ? MouseMessage.RightUp : MouseMessage.LeftUp, last.X + offsetX, last.Y + offsetY));
                System.Text.Json.JsonElement? textTarget = null;
                bool dragging = Math.Abs(last.X - first.X) >= 4 || Math.Abs(last.Y - first.Y) >= 4;
                bool overPopup = onPublishedPopup;
                bool overText = nativePopupPointer && textRegionRoot == root && textRegions.Any(rect => rect.Contains(first.X + textRegionOrigin.X, first.Y + textRegionOrigin.Y) && rect.Contains(last.X + textRegionOrigin.X, last.Y + textRegionOrigin.Y));
                bool overPressTarget = responsivePreview && textRegionRoot == root && pressRegions.Any(rect => rect.Contains(first.X + textRegionOrigin.X, first.Y + textRegionOrigin.Y) && rect.Contains(last.X + textRegionOrigin.X, last.Y + textRegionOrigin.Y));
                if (nativePopupPointer && !right && modifiers == 0 && !overPopup && dragging)
                {
                    textTarget = overText ? System.Text.Json.JsonSerializer.SerializeToElement(new { supported = true, note = "Read-only text bounds from the published owned frame; native anchor and Shift-click translation." }) : await Task.Run(() => layer.ObserveTextDragTarget(root, first, last));
                    if (textTarget.Value.GetProperty("supported").GetBoolean())
                    {
                        // Completed text drags translate to ordinary anchor and
                        // Shift-click input. UIA is used only to identify text
                        // bounds; no selection/model write or injected code.
                        int endX = last.X + offsetX, endY = last.Y + offsetY;
                        steps = [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y), new(MouseMessage.Move, endX, endY, 4), new(MouseMessage.LeftDown, endX, endY, 5), new(MouseMessage.LeftUp, endX, endY, 4), new(MouseMessage.Move, endX, endY)];
                    }
                }
                // Keep the virtual pointer source pinned for the tested text,
                // button and list-item presses; menus need early release.
                // qIV acquires capture itself. Pre-acquiring it on the attached
                // worker causes its WM_CAPTURECHANGED handler to cancel the drag.
                bool warmup = nativePopupPointer && (right || dragging || (!overText && !overPressTarget) || overPopup);
                bool deferredPress=nativePopupPointer&&root!=nativeSurfaceRoot&&overPressTarget&&!warmup&&publishedSourceClass.StartsWith("HwndWrapper[",StringComparison.Ordinal);
                if(accessibleModalInput&&deferredPress&&!right&&!dragging&&modifiers==0&&!doubleClick)
                {
                    var assisted=await Task.Run(()=>layer.AccessiblePointPress(root,first.X,first.Y,ControlOwner.Human));
                    if(assisted.GetProperty("performed").GetBoolean())
                    {
                        inputSequence++;ScheduleRefresh(true);
                        lastPointerMetrics=new{queuedMilliseconds,geometryMilliseconds,geometryVisited,geometryCacheHit,geometryHintUsed,totalMilliseconds=pointerClock.Elapsed.TotalMilliseconds,backend="standard accessibility modal press",assisted};return;
                    }
                }
                double beforeDelivery = pointerClock.Elapsed.TotalMilliseconds;
                var result = await Task.Run(() => layer.SendQueueMouseSequence(hit.Window, steps.ToArray(), gpuTarget != ResearchGpuTarget.Qiv || root != gpuRoot, ControlOwner.Human, warmupCapture: warmup,pacedMouse:deferredPress)); inputSequence++; ScheduleRefresh(postedShortcuts || gpuTarget != null && root != gpuRoot);
                lastPointerMetrics = new { queuedMilliseconds, geometryMilliseconds, geometryVisited, geometryCacheHit,geometryHintUsed,geometrySkipped=fastPopupGeometry&&onPublishedPopup&&!right, deliveryMilliseconds = pointerClock.Elapsed.TotalMilliseconds - beforeDelivery, totalMilliseconds = pointerClock.Elapsed.TotalMilliseconds, overText, overPressTarget, overPopup, warmup,deferredPress };
                await TraceInput("pointer", hit.Window, new { hit.X, hit.Y, steps, result, textTarget, overText, overPopup });
            });
        };
        canvas.MouseWheel += async (_, e) =>
        {
            if (!queuePointer || desiredOwner != ControlOwner.Human || activeMenuOwner != 0 || !canvas.TryImagePoint(e.Location, out var imagePoint)) return;
            int modifiers = (ModifierKeys.HasFlag(Keys.Shift) ? 4 : 0) | (ModifierKeys.HasFlag(Keys.Control) ? 8 : 0);
            await Execute(async () =>
            {
                var hit = await PointerHit(imagePoint);
                var result = await Task.Run(() => layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y, modifiers), new(MouseMessage.Wheel, hit.X, hit.Y, modifiers, e.Delta), new(MouseMessage.Move, hit.X, hit.Y)], gpuTarget != ResearchGpuTarget.Qiv || root != gpuRoot, ControlOwner.Human)); inputSequence++; ScheduleRefresh();
                await TraceInput("wheel", hit.Window, new { hit.X, hit.Y, e.Delta, result });
            });
        };
        canvas.KeyPress += async (_, e) =>
        {
            if (activeMenuOwner != 0) { e.Handled = true; return; }
            // SuppressKeyPress drains every queued WM_CHAR in WinForms, which
            // can discard legitimate typing already queued behind Enter. Ignore
            // just the translated editing/control character instead.
            if (responsivePreview && e.KeyChar < 32) { e.Handled = true; return; }
            if (desiredOwner != ControlOwner.Human || (e.KeyChar < 32 && e.KeyChar is not '\b' and not '\r')) return;
            e.Handled = true; string value = e.KeyChar.ToString();
            timing?.Record("character-received",previewVersion,inputSequence);
            if(responsivePreview)
            {
                // Coalesce only adjacent text already waiting behind another
                // command. Every non-text Execute call is an ordering boundary.
                if(pendingTextBatch is {} waiting&&waiting.Destination==nativeSurfaceRoot&&waiting.Text.Length<4096)
                {waiting.Text.Append(value);return;}
                var batch=new TextBatch(nativeSurfaceRoot,value);pendingTextBatch=batch;
                await Execute(async()=>
                {
                    // Let text messages already in this UI queue join one
                    // ordered batch before starting a worker round trip. This
                    // adds no fixed typing delay and preserves command barriers.
                    timing?.Record("text-command-acquired",previewVersion,inputSequence);
                    await Task.Yield();
                    if(ReferenceEquals(pendingTextBatch,batch))pendingTextBatch=null;
                    string literal=batch.Text.ToString();
                    timing?.Record("text-rpc-start",previewVersion,inputSequence);
                    await Task.Run(()=>layer.PostKeyboardInput(batch.Destination,literal,ControlOwner.Human,followPrivateFocus:true));
                    inputSequence+=literal.Length;textBatchCommands++;textBatchCharacters+=literal.Length;largestTextBatch=Math.Max(largestTextBatch,literal.Length);
                    pixelProbe?.InputForwarded(inputSequence);timing?.Record("text-forwarded",previewVersion,inputSequence);ScheduleRefresh();
                },inputTextBatch:true);return;
            }
            await Execute(async () =>
            {
                long target = inputTarget;
                // Match posted navigation: synchronous WM_CHAR can overtake a
                // previously posted Enter in the target application's queue.
                await Task.Run(() => { if (responsivePreview) layer.PostKeyboardInput(nativeSurfaceRoot, value, ControlOwner.Human, followPrivateFocus:true); else layer.SendKeyboardInput(target, value, ControlOwner.Human); });
                inputSequence++; pixelProbe?.InputForwarded(inputSequence); ScheduleRefresh();
            });
        };
        canvas.KeyDown += async (_, e) =>
        {
            if (desiredOwner != ControlOwner.Human || e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            if (activeMenuOwner != 0)
            {
                e.Handled = true; if (!responsivePreview) e.SuppressKeyPress = true; long menuOwner = activeMenuOwner; int menuKey = e.KeyValue;
                if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Escape)
                    await Execute(async () => { await Task.Run(() => { if(menuKey == 0x1B) layer.CancelOwnedMenu(menuOwner, ControlOwner.Human); else layer.PostKey(menuOwner, menuKey, ControlOwner.Human); }); ScheduleRefresh(true); });
                return;
            }
            bool navigation = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Back or Keys.Delete or Keys.Tab or Keys.Enter or Keys.Escape || e.KeyCode is >= Keys.F1 and <= Keys.F24;
            if (nativePopupPointer && e.Modifiers == Keys.None && e.KeyCode is >= Keys.F1 and <= Keys.F24)
            {
                e.Handled=true;int functionKey=e.KeyValue;
                await Execute(async()=>
                {
                    bool immediateFunction=postedFunctionKeys&&(postedFunctionTransitions||functionKey is 0x78 or 0x79);
                    lastForwardedInput=new{kind=immediateFunction?"function-key-posted":"function-key-state",key=functionKey,target=nativeSurfaceRoot};
                    if(immediateFunction)await Task.Run(()=>layer.PostKey(nativeSurfaceRoot,functionKey,ControlOwner.Human,followPrivateFocus:true));
                    else await Task.Run(()=>layer.SendQueueStateChord(nativeSurfaceRoot,[functionKey],posted:true,sender:ControlOwner.Human,followFocusOnKeyUp:true,releaseStateBeforeKeyUp:releaseStateBeforeKeyUp,followPrivateFocus:true));
                    inputSequence++;ScheduleRefresh(true);
                });return;
            }
            if (gpuTarget != null && e.Modifiers == Keys.None && (e.KeyCode == Keys.Space || e.KeyCode is >= Keys.F1 and <= Keys.F12))
            {
                e.Handled = true; if (!responsivePreview) e.SuppressKeyPress = true; int gpuKey = e.KeyValue;
                await Execute(async () => { await Task.Run(() => layer.PostKey(inputTarget, gpuKey, ControlOwner.Human)); ScheduleRefresh(gpuTarget == ResearchGpuTarget.Qiv && gpuKey == 0x71); }); return;
            }
            bool chord = e.Control || e.Alt || (e.Shift && navigation);
            if (chord)
            {
                e.Handled = true; if (!responsivePreview) e.SuppressKeyPress = true;
                var keys = new List<int>(); if (e.Control) keys.Add(0x11); if (e.Shift) keys.Add(0x10); if (e.Alt) keys.Add(0x12); keys.Add(e.KeyValue);
                await Execute(async () =>
                {
                    long target = inputTarget;
                    lastForwardedInput = new { kind = "chord", keys, target };
                    lastForwardedChord=lastForwardedInput;
                    await TraceInput("chord", target, new { keys });
                    bool windowMenu = gpuTarget == ResearchGpuTarget.Qiv && e.Alt && e.KeyCode == Keys.Space;
                    var chordClock=System.Diagnostics.Stopwatch.StartNew();
                    await Task.Run(() => { if (gpuTarget == ResearchGpuTarget.DynamicProbe && gpu != null) gpu.Chord(keys.ToArray(), ControlOwner.Human); else layer.SendQueueStateChord(windowMenu ? root : nativePopupPointer ? nativeSurfaceRoot : target, keys.ToArray(), posted: windowMenu || postedShortcuts, sender: ControlOwner.Human, followFocusOnKeyUp: postedShortcuts, releaseStateBeforeKeyUp: releaseStateBeforeKeyUp, followPrivateFocus:nativePopupPointer,preciseChordHoldMilliseconds:preciseChordHoldMilliseconds); });
                    lastForwardedChord=new{kind="chord",keys,target,forwardMilliseconds=chordClock.Elapsed.TotalMilliseconds,preciseMouseWaits,preciseChordHoldMilliseconds};
                    inputSequence++;
                    await TraceInput("chord-completed", target, new { keys });
                    // Posted shortcuts can return before a modern application's
                    // dialog is visible. Observe subsequent window changes without
                    // replaying the command or continuously capturing unchanged UI.
                    ScheduleRefresh(windowMenu || postedShortcuts);
                });
                return;
            }
            if (e.Modifiers != Keys.None || !navigation) return;
            // Backspace is an editing key, not just WM_CHAR '\b'. Suppress the
            // viewer's translated character so native controls delete once.
            e.Handled = true; if (!responsivePreview && (e.KeyCode is Keys.Back or Keys.Tab or Keys.Enter or Keys.Escape)) e.SuppressKeyPress = true; int key = e.KeyValue;
            await Execute(async () => { long target = inputTarget; lastForwardedInput = new { kind = "key", key, target }; await Task.Run(() => layer.PostKey(nativePopupPointer ? nativeSurfaceRoot : target, key, ControlOwner.Human, followPrivateFocus:nativePopupPointer)); inputSequence++; ScheduleRefresh(postedShortcuts || gpuTarget != null && root != gpuRoot && key is 0x0D or 0x1B); });
        };
        automation.Tick += async (_, _) =>
        {
            if (desiredOwner != ControlOwner.Automation || commands.CurrentCount == 0) return;
            string value = ((char)('a' + sequence++ % 26)).ToString(); long target = inputTarget;
            await Execute(() => Task.Run(() => layer.SendKeyboardInput(target, value)));
        };
        previewTimer.Tick += async (_, _) => await PreviewTickAsync();
        hoverTimer.Tick += async (_, _) =>
        {
            if (nativePopupPointer && commands.CurrentCount != 0) await ForwardHoverAsync();
        };
        Shown += async (_, _) =>
        {
            if (hidden) Hide(); previewTimer.Start(); if (nativePopupPointer) hoverTimer.Start(); SetStatus("Starting application…");
            await Execute(async () =>
            {
                if(deadlinePreview)try{deadlineDispatcher=new PreviewDeadlineDispatcher(action=>BeginInvoke(action),async()=>await PreviewTickAsync());}catch(System.ComponentModel.Win32Exception error){RecordDeadlineFault(error.ToString());}
                string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
                string working = reportDirectory ?? Path.Combine(project, "artifacts", "demo-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(working);
                gpuCapturePath = Path.Combine(working, "gpu-capture.png");
                  if (qtSoftware && wpfSoftwareHook) throw new ArgumentException("Choose one rendering compatibility option.");
                  var environment = wpfSoftwareHook ? WpfHookEnvironment.Create(exe, working) : qtSoftware ? new Dictionary<string, string> { ["QT_QUICK_BACKEND"] = "software" } : null;
                layer = await Task.Run(()=>AgentRecoveredKeeper!=null?manager.AdoptLayer(AgentRecoveredKeeper):AgentDurableLayer?manager.CreateDurableLayer():manager.CreateLayer());
                layer.PreserveOnWorkerFault=AgentDurableLayer;desiredOwner=layer.Owner;
                if(AgentDurableLayer&&AgentRecoveredKeeper==null)await Task.Run(()=>layer.EnableKeeper(Path.Combine(working,"guardian")));
                string[] launchArgs = args;
                if (gpuTarget == ResearchGpuTarget.Qiv) launchArgs = await Task.Run(() => QivResearch.Arguments(working, QivResearch.Prepare(working, layer.Id)));
                  if(AgentRecoveredKeeper==null)await Task.Run(() => layer.LaunchApplication(exe, launchArgs, working, environment));
                  if (wpfSoftwareHook) await WpfHookEnvironment.VerifyAsync(working, layer.ApplicationProcessId);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (root == 0 && watch.ElapsedMilliseconds < startupTimeoutMilliseconds)
                {
                    var list = await Task.Run(() => responsivePreview ? layer.GetPresentationWindows() : layer.GetWindows()); var found = list.FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Width > 100); if (found != null) root = found.Handle;
                    else await Task.Delay(100);
                }
                if (root == 0) { const string error = "Application produced no owned window within the startup deadline. Check executable and arguments."; StartupCompleted?.Invoke(error); allowClose = true; Close(); return; }
                nativeMainRoot = root;
                if(nativePopupPointer){var session=await Task.Run(()=>AgentRecoveredKeeper!=null?layer.ConfigureRecoveredInput(lowLatencyInput,preciseMouseWaits):layer.EnablePrivateInputSession(lowLatencyInput,preciseMouseWaits));if(reportDirectory!=null)await File.WriteAllTextAsync(Path.Combine(reportDirectory,"private-input-session.json"),session.GetRawText());}
                if(activatePrivate){var activation=await Task.Run(()=>layer.ResearchActivation(root,"active-focus"));if(reportDirectory!=null)await File.WriteAllTextAsync(Path.Combine(reportDirectory,"private-activation.json"),activation.GetRawText());}
                if (gpuTarget != null)
                {
                    gpuRoot = root;
                    await Task.Run(() => { layer.HideLayer(root); if(qivNativeStartup){qivGuard=new(layer,root);qivNativeInspection=true;}else gpu = new(layer, root, gpuTarget.Value); });
                }
                if (responsivePreview)
                {
                    await RefreshCoreAsync(false);
                    // The shell HWND precedes asynchronous solution/document
                    // loading. Publish a bounded startup burst without UIA scans,
                    // then watch title/window changes. Never render continuously.
                    startupPreviewUntil = Environment.TickCount64 + 5000;
                    windowWatchUntil = Environment.TickCount64 + 15000;
                }
                else await RefreshAsync();
                StartupCompleted?.Invoke(null);
            }, startup: true);
        };
    }
    void RecordDeadlineFault(string error)
    {
        if(deadlineFault!=null)return;deadlineFault=error;
        var failed=deadlineDispatcher;deadlineDispatcher=null;
        try{failed?.Dispose();}catch(Exception disposal){deadlineFault+="; "+disposal.Message;}
        if(reportDirectory!=null)try{File.AppendAllText(Path.Combine(reportDirectory,"viewer-errors.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,kind="preview-deadline-fallback",error=deadlineFault})+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}
        SetStatus("Preview scheduling returned to the standard UI timer.");
    }
    void ArmPreviewDeadline()
    {
        if(!deadlinePreview||deadlineDispatcher==null||disposed||automaticPreviewBusy||commands.CurrentCount==0||layer==null||layer.Closed||root==0||inputTarget==0||!preview||!Visible||WindowState==FormWindowState.Minimized)return;
        if(deadlineDispatcher.Error is {} error){RecordDeadlineFault(error);return;}
        long now=PreviewQpc;
        if(now>=deadlineInputUntil)return;
        long due=refreshQueued?deadlineRefreshDue:deadlineNextInput;
        deadlineDispatcher.Schedule(Math.Max(now,due));
    }
    async Task PreviewTickAsync()
    {
        if(disposed)return;
            if(timing!=null)await timing.FlushIfRequestedAsync();
            if(pixelProbe!=null&&reportDirectory!=null){string reset=Path.Combine(reportDirectory,"pixel-probe-reset.request");if(File.Exists(reset)){File.Delete(reset);pixelProbe.Reset();refreshQueued=true;refreshNotBefore=0;}}
            if(statusPublisher?.TakeError() is {} reportError)SetStatus("Passive report publication failed: "+reportError.Message);
            if (showFile != null && File.Exists(showFile)) { File.Delete(showFile); Show(); Activate(); }
            long now = Environment.TickCount64;
            bool startupFrame = responsivePreview && preview && now < startupPreviewUntil && now >= startupNextPreview;
            // A WPF dialog can finish loading without changing HWND, caption or
            // popup bounds. Follow deferred content after commands, then retain
            // a low-rate picture while a human is viewing the owned application.
            // Main-window async work can also finish without a title change.
            bool liveView = responsivePreview && preview && Visible && WindowState != FormWindowState.Minimized && desiredOwner == ControlOwner.Human && root != 0 && inputTarget != 0;
            bool contentFrame = responsivePreview && preview && now >= nextContentPreview && (now < contentWatchUntil || liveView);
            long preciseNow=PreviewQpc;
            bool inputFrame=responsivePreview&&directPreview&&preview&&Visible&&WindowState!=FormWindowState.Minimized&&(deadlinePreview?preciseNow<deadlineInputUntil&&preciseNow>=deadlineNextInput:now<inputPreviewUntil&&now>=nextInputPreview);
            if (automaticPreviewBusy || commands.CurrentCount == 0 || (deadlinePreview?refreshQueued&&preciseNow<deadlineRefreshDue:now < refreshNotBefore) || (!refreshQueued && !startupFrame && !contentFrame && !inputFrame && (!preview || now >= windowWatchUntil))) {ArmPreviewDeadline();return;}
            bool onlyWindowChange = !refreshQueued && !startupFrame && !contentFrame && !inputFrame;
            if(onlyWindowChange&&now<nextWindowObservation){ArmPreviewDeadline();return;}
            if(onlyWindowChange)nextWindowObservation=now+50;
            if (startupFrame) startupNextPreview = now + 150;
            if (contentFrame) nextContentPreview = now + (now < contentWatchUntil ? 150 : 500);
            long beforeFrame=previewVersion,startedQpc=PreviewQpc;
            refreshQueued = false;automaticPreviewBusy=true;
            try{await Execute(() => RefreshCoreAsync(false, onlyWindowChange, yieldDuringCapture:independentPreview));}
            finally{automaticPreviewBusy=false;}
            // An unchanged-window check did not render anything. Moving the
            // deadline after those checks would postpone the input frame forever
            // when the Windows timer fires just before the 16 ms boundary.
            if(previewVersion!=beforeFrame){nextInputPreview=Environment.TickCount64+(independentPreview?0:16);deadlineNextInput=startedQpc+PreviewTicks(deadlineFollowupMilliseconds);}
            ArmPreviewDeadline();
    }
    void AddButton(Control toolbar, string label, Func<Task> action)
    { var button = new Button { Text = label, AutoSize = true }; button.Click += async (_, _) => await Execute(action); toolbar.Controls.Add(button); }
    async Task Execute(Func<Task> action, bool startup = false, bool inputTextBatch = false)
    {
        if(!inputTextBatch)pendingTextBatch=null;
        await commands.WaitAsync();
        try { if (!disposed) await action(); }
        catch (Exception e) { automation.Stop(); SetStatus(e.Message); if(reportDirectory!=null)try{await File.AppendAllTextAsync(Path.Combine(reportDirectory,"viewer-errors.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,error=e.ToString(),workerDetails=e.Data["workerDetails"],root,inputTarget})+Environment.NewLine);}catch{} if (startup) { StartupCompleted?.Invoke(e.Message); allowClose = true; Close(); } }
        finally { commands.Release(); ArmPreviewDeadline(); }
    }
    void ScheduleRefresh(bool watchWindows = false)
    {
        if (!preview) return;
        // mpv accepts native input before its media thread publishes the result.
        // Coalesce a single delayed inspection; this is not a frame-ready guarantee.
        timing?.Record("refresh-requested",previewVersion,inputSequence);
        long scheduled=Environment.TickCount64;
        int inputDelay=16;
        if(deadlinePreview)
        {
            long qpc=PreviewQpc,dueQpc=qpc+PreviewTicks(deadlineFirstMilliseconds);
            deadlineRefreshDue=refreshQueued?Math.Min(deadlineRefreshDue,dueQpc):dueQpc;
            deadlineInputUntil=qpc+PreviewTicks(250);deadlineNextInput=Math.Min(deadlineNextInput,dueQpc);
        }
        long due=scheduled+(gpuTarget == ResearchGpuTarget.Mpv ? 250 : responsivePreview&&directPreview ? inputDelay : 0);
        // Continuous typing must not move an already pending frame farther
        // into the future on every character.
        refreshNotBefore=refreshQueued?Math.Min(refreshNotBefore,due):due;
        if(responsivePreview&&directPreview)
        {
            // PrintWindow can overtake posted input and WPF's later render.
            // Allow an input/render turn, then briefly follow actual pixels.
            // Leave a gap after each completed capture for the source queue.
            inputPreviewUntil=scheduled+250;nextInputPreview=Math.Min(nextInputPreview,scheduled+inputDelay);
        }
        if (watchWindows)
        {
            windowWatchUntil = Environment.TickCount64 + 3000;
            if (responsivePreview) contentWatchUntil = Math.Max(contentWatchUntil, windowWatchUntil);
        }
        refreshQueued = true;
    }
    Task RefreshAsync() => RefreshCoreAsync(false);
    async Task RefreshCoreAsync(bool inspectHidden, bool onlyWindowChange = false, bool detailed = false,bool yieldDuringCapture=false)
    {
        if (layer == null || layer.Closed || root == 0) return;
        try
        {
        timing?.Record("refresh-start",previewVersion+1,inputSequence);
        var refreshClock = System.Diagnostics.Stopwatch.StartNew();
        var list = await Task.Run(() => responsivePreview ? layer.GetPresentationWindows() : layer.GetWindows());
        double enumerationMilliseconds = refreshClock.Elapsed.TotalMilliseconds;
        System.Text.Json.JsonElement? threadState = gpuTarget == ResearchGpuTarget.Qiv ? await Task.Run(() => layer.ObserveWindowThread(gpuRoot)) : null;
        long active = threadState?.GetProperty("active").GetInt64() ?? 0;
        if (nativePopupPointer && gpuTarget == null && !list.Any(w => w.Handle == nativeMainRoot))
        {
            var replacement = LayerWindowSelection.ReplacementMain(list,nativeMainRoot,layer.ApplicationProcessId);
            if (replacement == null)
            {
                automation.Stop();inputTarget=0;pointerGesture.Clear();canvas.Capture=false;
                canvas.Image?.Dispose();canvas.Image=null;canvas.Invalidate();
                SetStatus("The original application window closed; waiting for one unambiguous replacement in the same application process.");return;
            }
            nativeMainRoot=nativeSurfaceRoot=replacement.Handle;preferredSurface=0;
        }
        // An owned debuggee is another top-level process in this layer's job,
        // rather than an HWND owned by the IDE. Selection is explicit and every
        // enumeration/input RPC is still restricted to this job and desktop.
        if (nativePopupPointer && !list.Any(w => w.Handle == nativeSurfaceRoot && w.Parent == 0 && w.Visible)) nativeSurfaceRoot = nativeMainRoot;
        var activeRoot = LayerWindowSelection.Select(list, gpuTarget != null ? gpuRoot : nativePopupPointer ? nativeSurfaceRoot : root, preferredSurface, active);
        activeMenuOwner = 0;
        if (nativePopupPointer)
        {
            var choices = list.Where(w => w.Parent == 0 && w.ClassName != "#32768" && w.Width > 100 && w.Height > 50 && (w.Handle == nativeMainRoot || w.Visible && !string.IsNullOrWhiteSpace(w.Text))).ToArray();
            if (!surfaces.Items.Cast<WindowChoice>().Select(c => (c.Window.Handle, c.Window.Text)).SequenceEqual(choices.Select(w => (w.Handle,w.Text))))
            {
                updatingSurfaces = true;
                try { surfaces.Items.Clear(); foreach (var choice in choices) surfaces.Items.Add(new WindowChoice(choice,true)); }
                finally { updatingSurfaces = false; }
            }
            updatingSurfaces = true;
            try { surfaces.SelectedIndex = Array.FindIndex(choices, w => w.Handle == nativeSurfaceRoot); }
            finally { updatingSurfaces = false; }
        }
        if (gpuTarget == ResearchGpuTarget.Qiv)
        {
            long menuOwner = threadState!.Value.GetProperty("menuOwner").GetInt64();
            var ownerWindow = list.FirstOrDefault(w => w.Handle == menuOwner);
            if ((threadState.Value.GetProperty("flags").GetUInt32() & 4) != 0 && ownerWindow != null && LayerWindowSelection.OwnedBy(list, ownerWindow, gpuRoot))
            {
                var menus = list.Where(w => w.Visible && w.ClassName == "#32768" && w.Owner == menuOwner).ToArray();
                if (menus.Length == 1) { activeRoot = menus[0]; activeMenuOwner = menuOwner; }
                else if (menus.Length > 1) activeRoot = null;
            }
            var choices = list.Where(w => w.Parent == 0 && w.ClassName != "#32768" && w.Width > 100 && (w.Handle == gpuRoot || w.Visible) && LayerWindowSelection.OwnedBy(list, w, gpuRoot)).ToArray();
            var oldChoices = surfaces.Items.Cast<WindowChoice>().Select(c => (c.Window.Handle, c.Window.Text)).ToArray();
            if (!oldChoices.SequenceEqual(choices.Select(w => (w.Handle, w.Text))))
            {
                updatingSurfaces = true; try { surfaces.Items.Clear(); foreach(var choice in choices) surfaces.Items.Add(new WindowChoice(choice, true)); } finally { updatingSurfaces = false; }
            }
            updatingSurfaces = true; try { surfaces.SelectedIndex = choices.ToList().FindIndex(w => w.Handle == activeRoot?.Handle); } finally { updatingSurfaces = false; }
            surfaces.Enabled = list.First(w => w.Handle == gpuRoot).Enabled && activeMenuOwner == 0;
        }
        if (activeRoot == null && gpuTarget == null)
            activeRoot = list.FirstOrDefault(w => w.Parent == 0 && w.Owner == 0 && w.Visible && w.Enabled && w.Width > 100);
        if (activeRoot == null) { automation.Stop(); inputTarget = 0; windows.Items.Clear(); pointerGesture.Clear(); canvas.Capture = false; canvas.Image?.Dispose(); canvas.Image = null; canvas.Invalidate(); SetStatus("Application has no unambiguous usable owned window. Refresh after its state changes, or choose Force close to terminate this layer's remaining children."); return; }
        bool changed = root != activeRoot.Handle;
        var observedPopups = nativePopupPointer ? await Task.Run(() => layer.GetPopupSurfaces(activeRoot.Handle)) : [];
        string popupSignature = string.Join(";", observedPopups.Select(p => $"{p.Handle}:{p.X}:{p.Y}:{p.Width}:{p.Height}"));
        if (onlyWindowChange && !changed && activeRoot.Text == publishedWindowText && popupSignature == publishedPopupSignature) return;
        popupSurfaces = observedPopups;
        root = activeRoot.Handle;
        surfaceKind = activeMenuOwner != 0 ? "menu" : gpuTarget != null && root != gpuRoot ? (list.Single(w => w.Handle == gpuRoot).Enabled ? "modeless" : "modal") : nativePopupPointer && root != nativeMainRoot ? (list.Single(w => w.Handle == nativeMainRoot).Enabled ? "modeless" : "modal") : "main";
        if (changed)
        {
            ownershipEpoch++; pointerGesture.Clear(); canvas.Capture = false; privateTextPoint = null;
            if (responsivePreview) { contentWatchUntil = Environment.TickCount64 + 3000; nextContentPreview = Environment.TickCount64 + 150; }
        }
        var targets = list.Where(w => LayerWindowSelection.InSubtree(list, w.Handle, root)).ToArray();
        long previous = inputTarget;
        if (changed || (gpuTarget != null && root != gpuRoot))
        {
            long focused = await Task.Run(() => layer.GetFocusedWindow(root));
            previous = LayerWindowSelection.InSubtree(list, focused, root) ? focused : 0;
        }
        if (!responsivePreview)
        {
            windows.Items.Clear(); foreach (var w in targets) windows.Items.Add(new WindowChoice(w));
        }
        else if (!windows.Items.Cast<WindowChoice>().Select(c => c.Window).SequenceEqual(targets))
        {
            windows.BeginUpdate(); try { windows.Items.Clear(); foreach (var w in targets) windows.Items.Add(new WindowChoice(w)); } finally { windows.EndUpdate(); }
        }
        windows.SelectedIndex = targets.ToList().FindIndex(w => w.Handle == previous && w.Enabled);
        if (windows.SelectedIndex < 0)
        {
            int editorIndex = targets.ToList().FindIndex(w => w.ClassName.Contains("EDIT", StringComparison.OrdinalIgnoreCase) && w.Visible && w.Enabled);
            windows.SelectedIndex = editorIndex >= 0 ? editorIndex : targets.ToList().FindIndex(w => w.Handle == root);
        }
        if (activeMenuOwner != 0) inputTarget = activeMenuOwner;
        if (!preview) return;
        CaptureFrame frame; Bitmap? previewImage = null;
        if (gpuTarget != null && root != gpuRoot)
        {
            gpuMetrics = null;
            frame = await Task.Run(() => layer.CaptureVisualOutput(root, CaptureBackend.PrintWindowFullContent));
            frame = frame with { Backend = (surfaceKind == "modal" ? "Owned modal dialog" : surfaceKind == "menu" ? "Native menu" : "Owned modeless window") + " / " + frame.Backend };
        }
        else if (gpu != null)
        {
            if (root != gpuRoot) throw new InvalidOperationException("The experimental GPU source window changed. Close and reopen this demo to bind its new root.");
            var result = await Task.Run(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew(); var metrics = gpu.Capture(gpuCapturePath, true);
                var png = File.ReadAllBytes(gpuCapturePath);
                return (Frame: new CaptureFrame(png, metrics.GetProperty("width").GetInt32(), metrics.GetProperty("height").GetInt32(), clock.Elapsed.TotalMilliseconds, "Experimental GPU bridge / D3D11", gpu.HelperPid), Metrics: metrics);
            });
            frame = result.Frame; gpuMetrics = result.Metrics;
        }
        else if (qivNativeInspection)
        {
            if (root != gpuRoot) throw new InvalidOperationException("The experimental source window changed. Close and reopen this demo to bind its new root.");
            frame = await Task.Run(() => layer.InspectHiddenWindow(root, true, qivNativeSettle));
        }
        else if (nativePopupPointer && !inspectHidden)
        {
            if (responsivePreview)
            {
                (CaptureFrame Frame,PopupSurface[] Surfaces,Bitmap Image) presentation;
                if(yieldDuringCapture)
                {
                    long selectedRoot=root,epoch=ownershipEpoch,version=previewVersion;
                    // Input keeps its existing serialized queue. Only automatic
                    // rendering releases it while the bounded worker capture
                    // runs on a separate, unattached private-desktop thread.
                    commands.Release();
                    try{timing?.Record("capture-request",previewVersion+1,inputSequence);presentation=await layer.CaptureSharedPreviewAsync(selectedRoot,softwareClientPreview,windowDcPreview);timing?.Record("capture-return",previewVersion+1,inputSequence,durationMilliseconds:presentation.Frame.CaptureMilliseconds);}
                    finally{await commands.WaitAsync();}
                    if(disposed||!preview||root!=selectedRoot||ownershipEpoch!=epoch||previewVersion!=version)
                    {presentation.Image.Dispose();refreshQueued=preview;return;}
                }
                else presentation=await Task.Run(() => layer.CaptureSharedPreview(root,directPreview,repaintPreview,windowDcPreview));
                frame = presentation.Frame; popupSurfaces = presentation.Surfaces; previewImage = presentation.Image;
            }
            else { var presentation = await Task.Run(() => layer.CaptureWithPopups(root)); frame = presentation.Frame; popupSurfaces = presentation.Surfaces; }
        }
        else frame = await Task.Run(() => inspectHidden ? layer.InspectHiddenWindow(root, backend == CaptureBackend.PrintWindowFullContent, inspectionSettle) : layer.CaptureVisualOutput(root, backend));
        await PublishFrameAsync(frame, activeRoot, !responsivePreview || detailed || inspectHidden, refreshClock, enumerationMilliseconds, previewImage);
        }
        catch(LayerOperationException e) when(nativePopupPointer&&e.Message=="Window no longer exists.")
        {
            // A dialog/debuggee can close between enumeration, capture and report
            // publication. Re-enumerate presentation only; never replay input.
            inputTarget=0;refreshQueued=true;windowWatchUntil=Environment.TickCount64+1000;
        }
    }
    async Task PublishFrameAsync(CaptureFrame frame, WindowInfo activeRoot, bool detailed = true, System.Diagnostics.Stopwatch? refreshClock = null, double? enumerationMilliseconds = null, Bitmap? previewImage = null)
    {
        var decodeClock = System.Diagnostics.Stopwatch.StartNew();
        Bitmap image;
        if (previewImage != null) image = previewImage;
        else { using var stream = new MemoryStream(frame.Png); using var decoded = Image.FromStream(stream); image = new Bitmap(decoded); }
        double decodeMilliseconds = decodeClock.Elapsed.TotalMilliseconds;
        if (disposed) { image.Dispose(); return; }
        object? earlyPixels=timing!=null?pixelProbe?.Sample(image,activeRoot.Handle):null;
        timing?.Record("bitmap-ready",previewVersion+1,inputSequence,pixelProbe?.ChangedPixels,frame.NativeRenderingMilliseconds);
        var old = canvas.Image;
        var damageClock=System.Diagnostics.Stopwatch.StartNew();
        var damage=incrementalPaint?PreviewDamage.Compare(old as Bitmap,image):null;
        double compareMilliseconds=damageClock.Elapsed.TotalMilliseconds;
        canvas.Image = image; old?.Dispose();
        previewVersion++;paintedFrame=previewVersion;paintedChangedPixels=pixelProbe?.ChangedPixels;
        if(damage==null)canvas.Invalidate();
        else if(damage.Length!=0)
        {
            var p=canvas.Presentation;using var region=new Region();region.MakeEmpty();
            foreach(var rectangle in damage)region.Union(PreviewDamage.Scale(rectangle,image.Size,new Size(p.DrawWidth,p.DrawHeight)));
            canvas.Invalidate(region);
        }
        previewDamage=new{enabled=incrementalPaint,full=damage==null,rectangles=damage?.Length,sourcePixels=damage?.Sum(r=>(long)r.Width*r.Height),compareMilliseconds};
        timing?.Record("image-assigned",previewVersion,inputSequence,paintedChangedPixels);
        // Paint only our visible canvas and only after invalidating changed
        // pixels. Update sends its normal WM_PAINT; it does not pump input.
        if(immediatePreviewPaint&&Visible&&preview&&WindowState!=FormWindowState.Minimized&&desiredOwner==ControlOwner.Human&&(damage==null||damage.Length!=0))canvas.Update();
        publishedWindowText = activeRoot.Text;
        publishedSourceClass = activeRoot.ClassName;
        publishedPopupSignature = string.Join(";", popupSurfaces.Select(p => $"{p.Handle}:{p.X}:{p.Y}:{p.Width}:{p.Height}"));
        SetStatus($"{layer.Owner} | PID {layer.ApplicationProcessId} | {frame.Width} × {frame.Height} | {frame.Backend} {frame.CaptureMilliseconds:F1} ms | {layer.Id}");
        await MaybePrepareInitialPointerAsync(frame,activeRoot);
        if (reportDirectory != null)
        {
            System.Text.Json.JsonElement? menuSnapshot = activeMenuOwner != 0 ? await Task.Run(() => layer.ObserveMenu(root)) : null;
            if (observeAccessible && detailed && gpu == null && !qivNativeInspection)
            {
                var observation = await ReadDetailedInspectionAsync(root); string evidence = Path.Combine(reportDirectory, "viewer-accessible.json");
                if (nativePopupPointer && activeRoot.ClassName=="#32770" && !observation.GetProperty("nodes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()=="1148"&&n.GetProperty("type").GetString()=="ControlType.Edit"))
                {
                    var filename = await ReadDetailedInspectionAsync(root,"1148");
                    var combined=observation.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).Concat(filename.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone())).ToArray();
                    observation=System.Text.Json.JsonSerializer.SerializeToElement(new{nodes=combined,truncated=true,generalObservation=observation,filenameObservation=filename});
                }
                if (nativePopupPointer && activeRoot.Text.Contains("Microsoft Visual Studio",StringComparison.Ordinal) && observation.GetProperty("truncated").GetBoolean())
                {
                    // Debugger toolbars can fill the bounded general snapshot.
                    // Only explicit inspection makes this extra passive query.
                    var editor = await ReadDetailedInspectionAsync(root,"WpfTextView");
                    var combined=observation.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()).Concat(editor.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone())).ToArray();
                    observation=System.Text.Json.JsonSerializer.SerializeToElement(new{nodes=combined,truncated=true,generalObservation=observation,editorObservation=editor});
                }
                if (nativePopupPointer)
                {
                    var textOrigin = await Task.Run(() => layer.ClientOrigin(root)); textRegionOrigin = new(textOrigin.X, textOrigin.Y); textRegionRoot = root; textRegions.Clear();
                    foreach (var node in observation.GetProperty("nodes").EnumerateArray())
                        if (node.GetProperty("type").GetString() == "ControlType.Edit" && node.GetProperty("text").ValueKind == System.Text.Json.JsonValueKind.String && !node.GetProperty("offscreen").GetBoolean() && node.GetProperty("x").ValueKind == System.Text.Json.JsonValueKind.Number && node.GetProperty("y").ValueKind == System.Text.Json.JsonValueKind.Number && node.GetProperty("width").ValueKind == System.Text.Json.JsonValueKind.Number && node.GetProperty("height").ValueKind == System.Text.Json.JsonValueKind.Number)
                            textRegions.Add(new((float)node.GetProperty("x").GetDouble(), (float)node.GetProperty("y").GetDouble(), (float)node.GetProperty("width").GetDouble(), (float)node.GetProperty("height").GetDouble()));
                }
                object publication = observation;
                if (nativePopupPointer && popupSurfaces.Length > 0)
                {
                    var nodes = observation.GetProperty("nodes").EnumerateArray().Select(n => n.Clone()).ToList(); var popupObservations = new List<object>();
                    var menuObservation=await ReadDetailedInspectionAsync(root,menus:true);
                    nodes.AddRange(menuObservation.GetProperty("nodes").EnumerateArray().Select(n=>n.Clone()));
                    foreach (var popup in popupSurfaces)
                    {
                        try { var observed = await ReadDetailedInspectionAsync(popup.Handle); popupObservations.Add(new { popup.Handle, observation = observed }); nodes.AddRange(observed.GetProperty("nodes").EnumerateArray().Select(n => n.Clone())); }
                        catch (LayerOperationException) { } // A transient popup can close between capture and passive observation.
                    }
                    publication = new { nodes, observation,menuObservation, popupObservations, note = "Read-only main, targeted visible menu and owned popup observations; pointer input backends are recorded separately." };
                }
                await File.WriteAllTextAsync(evidence + ".tmp", System.Text.Json.JsonSerializer.Serialize(publication)); File.Move(evidence + ".tmp", evidence, true);
                observationVersion = previewVersion;
            }
            string report = Path.Combine(reportDirectory, "viewer-preview.json");
            try
            {
                timing?.Record("report-start",previewVersion,inputSequence,paintedChangedPixels);
                if (gpu != null || qivNativeInspection) { string pixels = Path.Combine(reportDirectory, "viewer-preview.png"); await SnapshotFiles.ReplaceAsync(pixels, frame.Png); }
                timing?.Record("origin-rpc-start",previewVersion,inputSequence);
                var sourceOrigin = captureOriginReport&&frame.SourceClientX is int sx&&frame.SourceClientY is int sy ? (X:sx,Y:sy) : await Task.Run(() => layer.ClientOrigin(root));
                timing?.Record("origin-rpc-end",previewVersion,inputSequence);
                timing?.Record("report-io-start",previewVersion,inputSequence);
                string reportSnapshot=System.Text.Json.JsonSerializer.Serialize(new { initialPointerPreparation,deadlinePreview,deadlineFirstMilliseconds,deadlineFollowupMilliseconds,immediatePreviewPaint,deadlineFault,chordHoldPolicy,functionKeyPolicy,paintClipRectangles=canvas.LastClipRectangles,splitPreviewClips=canvas.SplitPreviewClips,previewDamage, root, mainRoot = gpuTarget != null ? gpuRoot : nativePopupPointer ? nativeMainRoot : root, ownedModal = surfaceKind == "modal", surfaceKind, activeClass = activeRoot.ClassName, activeText = activeRoot.Text, sourceClientOrigin = new { sourceOrigin.X, sourceOrigin.Y }, inputWindow = (windows.SelectedItem as WindowChoice)?.Window, activeMenuOwner, menuSnapshot, nativePopupPointer, responsivePreview, observationVersion, enumerationMilliseconds, decodeMilliseconds, lastPointerMetrics, nativeRenderingMilliseconds = frame.NativeRenderingMilliseconds, encodingMilliseconds = frame.EncodingMilliseconds, refreshMilliseconds = refreshClock?.Elapsed.TotalMilliseconds, popupSurfaces, textBatchCommands,textBatchCharacters,largestTextBatch,lastForwardedChord, pixelProbe = earlyPixels??pixelProbe?.Sample(image,root), lastForwardedInput, availableSurfaces = surfaces.Items.Cast<WindowChoice>().Select(c => c.Window), presentation = canvas.Presentation, inputTarget, previewVersion, inputSequence, frame.Width, frame.Height, frame.Backend, frame.CaptureMilliseconds, sourceWindowVisible = activeRoot.Visible, owner = layer.Owner.ToString(), applicationPid = layer.ApplicationProcessId, desktopId = layer.Id, helperPid = gpu?.HelperPid, gpuMapping = gpu?.MappingName, gpuMetrics, gpuState = gpu?.State, gpuStartup = gpu?.StartupMetrics, qivNativeInspection, qivGuard=qivGuard?.State, qivGuardSetup=qivGuard?.Setup });
                if(statusPublisher==null)await SnapshotFiles.ReplaceTextAsync(report,reportSnapshot);
                else if(detailed)await statusPublisher.WriteRequiredAsync(previewVersion,reportSnapshot);
                else statusPublisher.Queue(previewVersion,reportSnapshot);
                timing?.Record("report-io-end",previewVersion,inputSequence);
                timing?.Record("report-end",previewVersion,inputSequence,paintedChangedPixels);
                if (responsivePreview && detailed)
                {
                    long now = Environment.TickCount64;
                    windowWatchUntil = Math.Max(windowWatchUntil, now + 1000);
                    // Leave the explicitly inspected snapshot available for the
                    // reader before another automatic frame is published.
                    nextContentPreview = Math.Max(nextContentPreview, now + 500);
                    startupNextPreview = Math.Max(startupNextPreview, now + 500);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Passive evidence failure must be visible and bounded. Do not
                // replay input or silently leave a stale success report.
                try { await File.AppendAllTextAsync(Path.Combine(reportDirectory, "viewer-report-errors.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, previewVersion, root, error = error.Message, error.HResult }) + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
    void SetStatus(string value) { if (!disposed) status.Text = value+(deadlineFault==null?"":" | Preview timing fallback"); }
    async Task DisposeGpuAsync()
    {
        var session = gpu; gpu = null;
        if (session != null) await Task.Run(session.Dispose);
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if(AgentDurableLayer&&!allowClose&&e.CloseReason==CloseReason.UserClosing)
        {
            e.Cancel=true;ownershipEpoch++;pointerGesture.Clear();pendingHover=null;canvas.Capture=false;
            preview=false;Hide();canvas.Image?.Dispose();canvas.Image=null;canvas.Invalidate();
            SetStatus("Viewer hidden; this application layer remains running. Close it explicitly when work is finished.");
            base.OnFormClosing(e);return;
        }
        if (!allowClose && (layer == null || !layer.Closed))
        {
            e.Cancel = true;
            if (!closePending)
            {
                closePending = true; automation.Stop(); desiredOwner = ControlOwner.Human;
                await Execute(async () =>
                {
                    if (layer == null) { allowClose = true; BeginInvoke(Close); return; }
                    // Stop instrumentation while the owned source and reader still exist.
                    // If a save prompt keeps the source alive, fall back to native capture.
                    await DisposeGpuAsync();
                    bool closed = await Task.Run(() => layer.CloseGracefully(TimeSpan.FromSeconds(2)));
                    if (closed) { allowClose = true; BeginInvoke(Close); }
                    else { await RefreshAsync(); SetStatus("Application is still running. Human input owns the layer: inspect any save prompt, then close again. Force close discards unsaved state."); closePending = false; }
                });
            }
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true;deadlineDispatcher?.Dispose();statusPublisher?.Dispose(); hoverTimer.Dispose(); previewTimer.Dispose(); automation.Dispose(); manager.Dispose(); gpu?.Dispose(); gpu = null; qivGuard?.Dispose();qivGuard=null;canvas.Image?.Dispose(); }
        base.Dispose(disposing);
    }
    sealed record WindowChoice(WindowInfo Window, bool Friendly = false) { public override string ToString() => Friendly ? (Window.ClassName == "QIV_HelpWindow" ? "Help" : string.IsNullOrWhiteSpace(Window.Text) ? "Application window" : Window.Text.Replace("\r", " ").Replace("\n", " ")).Truncate(50) : $"{Window.ClassName}: {Window.Text.Replace("\r", " ").Replace("\n", " ").Truncate(45)}"; }
}
internal sealed class InputCanvas : Control
{
    internal bool IgnoreSystemCharacters;
    internal Action<bool,int>? PaintTiming;
    int paintMessage;
    internal bool SplitPreviewClips;
    internal int LastClipRectangles;
    Region? paintRegion;
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]static extern bool DeleteObject(nint handle);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int GetUpdateRgn(nint hwnd,nint region,bool erase);
    Region? ReadPaintRegion()
    {
        nint native=CreateRectRgn(0,0,0,0);if(native==0)throw new InvalidOperationException("Cannot allocate owned paint region");
        try{int result=GetUpdateRgn(Handle,native,false);if(result==0)throw new InvalidOperationException("Cannot read owned paint region");return result==1?null:Region.FromHrgn(native);}
        finally{DeleteObject(native);}
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Image? Image { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool FitOversizedImages { get; set; }
    internal PreviewPresentation Presentation
    {
        get
        {
            if (Image == null) return new(0, 0, 0, 0, ClientSize.Width, ClientSize.Height);
            double scale = FitOversizedImages ? Math.Min(1, Math.Min((double)Math.Max(1, ClientSize.Width) / Image.Width, (double)Math.Max(1, ClientSize.Height) / Image.Height)) : 1;
            return new(Image.Width, Image.Height, Math.Max(1, (int)(Image.Width * scale)), Math.Max(1, (int)(Image.Height * scale)), ClientSize.Width, ClientSize.Height);
        }
    }
    internal bool TryImagePoint(Point point, out Point imagePoint)
    {
        var p = Presentation; imagePoint = Point.Empty;
        if (p.SourceWidth == 0 || point.X < 0 || point.Y < 0 || point.X >= p.DrawWidth || point.Y >= p.DrawHeight) return false;
        imagePoint = Map(p, point); return true;
    }
    internal Point ClampedImagePoint(Point point)
    { var p = Presentation; return p.SourceWidth == 0 ? Point.Empty : Map(p, new(Math.Clamp(point.X, 0, p.DrawWidth - 1), Math.Clamp(point.Y, 0, p.DrawHeight - 1))); }
    static Point Map(PreviewPresentation p, Point point) => new(Math.Min(p.SourceWidth - 1, (int)((long)point.X * p.SourceWidth / p.DrawWidth)), Math.Min(p.SourceHeight - 1, (int)((long)point.Y * p.SourceHeight / p.DrawHeight)));
    public InputCanvas() { SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); }
    protected override bool IsInputKey(Keys keyData) => true;
    protected override void WndProc(ref Message message)
    {
        // Alt shortcuts are forwarded by KeyDown. Discard their WM_SYSCHAR
        // individually, without draining earlier ordinary WM_CHAR messages.
        if (IgnoreSystemCharacters && message.Msg == 0x106) { message.Result = 0; return; }
        int previous=paintMessage;
        if(message.Msg is 0xF or 0x317 or 0x318)paintMessage=message.Msg;
        Region? priorRegion=paintRegion;
        paintRegion=SplitPreviewClips&&message.Msg==0xF?ReadPaintRegion():null;
        try{base.WndProc(ref message);}finally{paintRegion?.Dispose();paintRegion=priorRegion;paintMessage=previous;}
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        PaintTiming?.Invoke(false,paintMessage);base.OnPaint(e);LastClipRectangles=1;
        if(Image!=null)
        {
            var p=Presentation;var destination=new Rectangle(0,0,p.DrawWidth,p.DrawHeight);
            if(p.SourceWidth==p.DrawWidth&&p.SourceHeight==p.DrawHeight)e.Graphics.DrawImageUnscaled(Image,0,0);
            else
            {
                using var matrix=new System.Drawing.Drawing2D.Matrix();
                var scans=SplitPreviewClips?paintRegion?.GetRegionScans(matrix):null;
                if(scans is {Length:>1 and <=64})
                {
                    LastClipRectangles=scans.Length;
                    foreach(var clip in scans)
                    {
                        var state=e.Graphics.Save();try{e.Graphics.SetClip(clip,System.Drawing.Drawing2D.CombineMode.Intersect);e.Graphics.DrawImage(Image,destination);}finally{e.Graphics.Restore(state);}
                    }
                }
                else e.Graphics.DrawImage(Image,destination);
            }
        }
        PaintTiming?.Invoke(true,paintMessage);
    }
}
internal record PreviewPresentation(int SourceWidth, int SourceHeight, int DrawWidth, int DrawHeight, int CanvasWidth, int CanvasHeight);
internal static class TextExtensions { public static string Truncate(this string value, int limit) => value.Length > limit ? value[..limit] + "…" : value; }

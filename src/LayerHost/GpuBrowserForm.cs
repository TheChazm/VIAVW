namespace LayerHost;

internal sealed class GpuBrowserForm : Form
{
    readonly SemaphoreSlim commands = new(1, 1);
    readonly InputCanvas canvas = new() { Dock = DockStyle.Fill, TabStop = true };
    readonly Label status = new() { Dock = DockStyle.Fill };
    readonly TextBox text = new() { Text = "Automation on the GPU page", Width = 260 };
    readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 180 };
    OffscreenBrowserSession? session;
    ControlOwner desiredOwner = ControlOwner.Automation;
    bool preview = true, refreshQueued, closing, allowClose;
    readonly bool qa;
    internal event Action<Exception?>? StartupCompleted;
    internal OffscreenBrowserSession? CurrentSession => session;
    internal nint InputHandle => canvas.Handle;
    internal async Task WaitForCommands() { await commands.WaitAsync(); commands.Release(); }
    internal GpuBrowserForm(bool qa = false)
    {
        this.qa = qa;
        if (qa) { Opacity = 0; ShowInTaskbar = false; }
        Text = "VIAVW — experimental GPU browser"; ClientSize = new Size(900, 850);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        void Button(string label, Func<Task> action) { var button = new Button { Text = label, AutoSize = true }; button.Click += async (_, _) => await Execute(action); bar.Controls.Add(button); }
        Button("Refresh", RefreshPreviewAsync);
        Button("Restart capture worker", async () => { await Task.Run(() => session!.RestartCaptureWorker()); await RefreshPreviewAsync(); });
        bar.Controls.Add(text);
        Button("Automation: type", async () => { string value = text.Text; await Task.Run(() => { session!.ClickElement("editor"); session.SendKeyboardInput(value); }); await RefreshPreviewAsync(); });
        Button("Automation: change color", async () => { await Task.Run(() => session!.ClickElement("toggle")); await RefreshPreviewAsync(); });
        Button("Human control", async () => { await Task.Run(() => session!.TakeHumanControl()); desiredOwner = ControlOwner.Human; status.Text = "Human owns input. Click the page in the picture, then type or use Ctrl+A. Chrome's toolbar is shown for inspection."; });
        Button("Return to automation", async () => { await Task.Run(() => session!.ReturnToAutomation()); desiredOwner = ControlOwner.Automation; status.Text = "Automation owns input. The picture is read-only."; });
        Button("Request attention", () => { session!.RequestAttention("Please inspect this GPU page, then return control to automation."); status.Text = session.AttentionReason; return Task.CompletedTask; });
        Button("Hide preview", () => { preview = false; canvas.Image?.Dispose(); canvas.Image = null; canvas.Invalidate(); status.Text = "Preview hidden. No captures run; the browser remains offscreen."; return Task.CompletedTask; });
        Button("Show preview", async () => { preview = true; await RefreshPreviewAsync(); });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(10) }; footer.Controls.Add(status);
        Controls.Add(canvas); Controls.Add(footer); Controls.Add(bar);
        canvas.MouseDown += async (_, e) =>
        {
            if (desiredOwner != ControlOwner.Human || session == null || canvas.Image == null || e.Button != MouseButtons.Left || e.X < 0 || e.X >= canvas.Image.Width || e.Y < session.ViewportOffsetY || e.Y >= canvas.Image.Height) return;
            canvas.Focus(); int x = e.X, y = e.Y - session.ViewportOffsetY;
            await Execute(async () => { await Task.Run(() => session.SendMouseInput(x, y, ControlOwner.Human)); refreshQueued = true; });
        };
        canvas.KeyPress += async (_, e) =>
        {
            if (desiredOwner != ControlOwner.Human || session == null || e.KeyChar < 32) return;
            e.Handled = true; string value = e.KeyChar.ToString();
            await Execute(async () => { await Task.Run(() => session.SendKeyboardInput(value, ControlOwner.Human)); refreshQueued = true; });
        };
        canvas.KeyDown += async (_, e) =>
        {
            if (desiredOwner != ControlOwner.Human || session == null || e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            bool navigation = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Delete or Keys.Back or Keys.Tab or Keys.Enter or Keys.Escape;
            if (!navigation && !e.Control && !e.Alt) return;
            e.Handled = true; e.SuppressKeyPress = true;
            string key = e.KeyCode switch { Keys.Left => "ArrowLeft", Keys.Right => "ArrowRight", Keys.Up => "ArrowUp", Keys.Down => "ArrowDown", Keys.Back => "Backspace", Keys.Return => "Enter", _ => e.KeyCode.ToString() };
            string code = e.KeyCode is >= Keys.A and <= Keys.Z ? "Key" + e.KeyCode : key;
            int virtualKey = e.KeyValue, modifiers = (e.Alt ? 1 : 0) | (e.Control ? 2 : 0) | (e.Shift ? 8 : 0);
            await Execute(async () => { await Task.Run(() => session.SendKey(key, code, virtualKey, modifiers, ControlOwner.Human)); refreshQueued = true; });
        };
        previewTimer.Tick += async (_, _) => { if (!refreshQueued || commands.CurrentCount == 0) return; refreshQueued = false; await Execute(RefreshPreviewAsync); };
        Shown += async (_, _) =>
        {
            status.Text = "Starting the disposable offscreen browser…";
            Exception? failure = null;
            await Execute(async () =>
            {
                try
                {
                    string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
                    session = await Task.Run(() => new OffscreenBrowserSession(Path.Combine(project, "artifacts/gpu-demo", Guid.NewGuid().ToString("N"))));
                    await RefreshPreviewAsync(); previewTimer.Start();
                }
                catch (Exception e) { failure = e; throw; }
            });
            StartupCompleted?.Invoke(failure);
        };
    }
    protected override bool ShowWithoutActivation => qa;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; if (qa) parameters.ExStyle |= 0x08000080; return parameters; }
    }
    async Task Execute(Func<Task> action)
    {
        await commands.WaitAsync();
        try { await action(); }
        catch (Exception e) { status.Text = e.Message; }
        finally { commands.Release(); }
    }
    async Task RefreshPreviewAsync()
    {
        if (!preview || session == null) return;
        var frame = await Task.Run(session.CaptureVisualOutput);
        using var stream = new MemoryStream(frame.Png); using var decoded = Image.FromStream(stream);
        canvas.Image?.Dispose(); canvas.Image = new Bitmap(decoded); canvas.Invalidate();
        status.Text = $"{session.Owner} | PID {session.ApplicationProcessId} | native WGC {frame.CaptureMilliseconds:F0} ms\n{session.Renderer}\nExperimental offscreen presentation on Default; browser-adapted input. Refresh is on demand.";
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (allowClose) { base.OnFormClosing(e); return; }
        e.Cancel = true; base.OnFormClosing(e); if (closing) return; closing = true; previewTimer.Stop();
        await Execute(() => Task.Run(() => session?.Dispose()));
        allowClose = true; BeginInvoke(Close);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { previewTimer.Dispose(); canvas.Image?.Dispose(); session?.Dispose(); }
        base.Dispose(disposing);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LayerProbe;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--child")) { Thread.Sleep(Timeout.Infinite); return; }
        if (args.Contains("--spawn-child"))
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false });
        Application.Run(new ProbeForm(args.Contains("--foreground"), args.Contains("--hang-print"), args.Contains("--file-probe"), args.Contains("--report"), args.Contains("--spawn-on-request")));
    }
}

// A normal disposable WinForms application. It has no LayerHost reference or input API.
// Event counters observe the standard controls' usual event path; they do not handle input.
internal sealed class ProbeForm : Form
{
    readonly TextBox editor = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Name = "editor" };
    readonly Label status = new() { Name = "status" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    readonly bool hangPrint;
    readonly bool report;
    int ticks, clicks, chars, keys;

    public ProbeForm(bool foreground, bool hangPrint, bool fileProbe, bool report, bool spawnOnRequest = false)
    {
        this.hangPrint = hangPrint;
        this.report = report;
        Text = foreground ? "Layer 0 typing witness" : "Disposable application";
        ClientSize = new Size(620, 330);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 40);
        Font = new Font("Segoe UI", 10);
        var heading = new Label { Text = foreground ? "Type here while the layers run. This is the physical desktop witness." : "Normal EDIT / BUTTON controls, normal process, private desktop.", AutoSize = false, Bounds = new Rectangle(18, 15, 585, 40) };
        editor.Bounds = new Rectangle(18, 60, 580, 145);
        var button = new Button { Name = "counter", Text = "Increment counter", Bounds = new Rectangle(18, 218, 180, 36) };
        var environment = new Label { Bounds = new Rectangle(210, 218, 388, 44), Text = $"PID {Environment.ProcessId} | {Environment.OSVersion}\nOS files: {File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini"))}" };
        status.Bounds = new Rectangle(18, 278, 580, 36);
        editor.KeyPress += (_, _) => { chars++; UpdateStatus(); };
        editor.KeyDown += (_, _) => { keys++; UpdateStatus(); };
        button.Click += (_, _) => { clicks++; UpdateStatus(); };
        timer.Tick += (_, _) => { ticks++; UpdateStatus(); };
        if (spawnOnRequest)
        {
            string artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Environment.CurrentDirectory).StartsWith(artifacts, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Disposable child requests must stay inside project artifacts.");
            bool spawned = false; timer.Interval = 10;
            timer.Tick += (_, _) =>
            {
                if (spawned || !File.Exists(Path.Combine(Environment.CurrentDirectory, "spawn-child.request"))) return;
                spawned = true;
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false, CreateNoWindow = true })!;
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "spawned-child.json"), System.Text.Json.JsonSerializer.Serialize(new { pid = child.Id, utc = DateTime.UtcNow, parent = Environment.ProcessId }));
            };
        }
        Controls.AddRange([heading, editor, button, environment, status]);
        if (fileProbe)
        {
            // Only the explicitly supplied project working directory is used.
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "probe-os-access.txt"), $"PID={Environment.ProcessId}; normal filesystem write/read");
            environment.Text += " | scratch file: " + File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "probe-os-access.txt")).Contains("normal filesystem");
        }
        Shown += (_, _) => editor.Focus();
        timer.Start();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        status.Text = $"ticks={ticks}; clicks={clicks}; chars={chars}; keys={keys}; pid={Environment.ProcessId}";
        if (report)
        {
            string path = Path.Combine(Environment.CurrentDirectory, "probe-observed.json");
            File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(new { ticks, clicks, chars, keys, pid = Environment.ProcessId, text = editor.Text }));
            try { File.Move(path + ".tmp", path, true); }
            catch (IOException) { } // Passive telemetry must not interrupt ordinary control behavior.
            catch (UnauthorizedAccessException) { }
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (hangPrint && m.Msg is 0x0317 or 0x0318) Thread.Sleep(Timeout.Infinite);
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

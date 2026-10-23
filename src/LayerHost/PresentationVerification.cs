using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

// Separate bounded experiment: never a silent fallback from a private layer.
internal static class PresentationVerification
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        if (Native.InputDesktopName() != "Default") throw new InvalidOperationException("Presentation experiment requires the ordinary Default input desktop.");
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var results = new List<object>(); var foreground = Native.GetForegroundWindow();
        using var manager = new LayerManager(); using var isolated = manager.CreateLayer();
        string privateFolder = Path.Combine(output, "private"); Directory.CreateDirectory(privateFolder);
        isolated.LaunchApplication(Path.Combine(project, "artifacts/native-build/D3DProbe.exe"), workingDirectory: privateFolder);
        var privateRoot = CompatibilityVerification.WaitRoot(isolated, "Disposable Direct3D application");
        using var destination = new Form { ShowInTaskbar = false };
        int registration = Native.DwmRegisterThumbnail(destination.Handle, (nint)privateRoot.Handle, out nint thumbnail);
        Size size = default; int? query = registration == 0 ? Native.DwmQueryThumbnailSourceSize(thumbnail, out size) : null;
        if (registration == 0) Native.DwmUnregisterThumbnail(thumbnail);
        int cloak = 1; int cloakResult = Native.DwmSetWindowAttribute((nint)privateRoot.Handle, 13, ref cloak, 4);
        results.Add(new { mode = "private source to default destination thumbnail; cross-process cloak", registrationHresult = $"0x{registration:X8}", queryHresult = query == null ? null : $"0x{query:X8}", sourceSize = new { size.Width, size.Height }, cloakHresult = $"0x{cloakResult:X8}" });

        nint job = Native.CreateJobObjectW(0, null); Native.Check(job != 0, "Presentation job");
        var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
        Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Presentation job limits");
        int[] pids = [];
        try
        {
            string folder = Path.Combine(output, "offscreen"); Directory.CreateDirectory(folder);
            var app = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/D3DProbe.exe"), ["--noactivate"], "WinSta0\\Default", folder, startupShow: 0);
            try
            {
                Native.Check(Native.AssignProcessToJobObject(job, app.hProcess), "Own presentation target"); Native.ResumeThread(app.hThread);
                nint hwnd = 0; var wait = Stopwatch.StartNew();
                while (hwnd == 0 && wait.ElapsedMilliseconds < 5000)
                {
                    Native.EnumProc callback = (window, _) => { Native.GetWindowThreadProcessId(window, out uint pid); if (pid == app.dwProcessId && Native.ClassName(window) == "VIAVW_D3DProbe") hwnd = window; return true; };
                    Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), callback, 0); Thread.Sleep(25);
                }
                if (hwnd == 0) throw new InvalidOperationException("No presentation target.");
                bool initiallyHidden = !Native.IsWindowVisible(hwnd);
                if (!initiallyHidden) throw new InvalidOperationException("Target did not respect hidden startup; refusing offscreen interaction.");
                Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd, -20) | 0x08000080); // NOACTIVATE + TOOLWINDOW
                var bounds = SystemInformation.VirtualScreen;
                Native.Check(Native.SetWindowPos(hwnd, 0, bounds.Right + 200, bounds.Top, 0, 0, 0x0010 | 0x0004 | 0x0001 | 0x0020), "Move owned hidden target outside all monitors");
                Native.ShowWindow(hwnd, 4); Thread.Sleep(300);
                int foreignCloak = Native.DwmSetWindowAttribute(hwnd, 13, ref cloak, 4);
                foreach (string phase in new[] { "before", "after-key", "hidden" })
                {
                    if (phase == "after-key") { Native.Message(hwnd, Native.WM_KEYDOWN, 0x20, 1); Native.Message(hwnd, Native.WM_KEYUP, 0x20, unchecked((nint)0xC0000001)); Thread.Sleep(250); }
                    if (phase == "hidden") { Native.ShowWindow(hwnd, 0); Thread.Sleep(250); }
                    string png = Path.Combine(folder, phase + ".png");
                    var helper = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/WgcCapture.exe"), [hwnd.ToString(), png], "WinSta0\\Default", folder);
                    uint exit;
                    try { Native.Check(Native.AssignProcessToJobObject(job, helper.hProcess), "Own WGC helper"); Native.ResumeThread(helper.hThread); if (Native.WaitForSingleObject(helper.hProcess, 4500) != 0) throw new TimeoutException("WGC helper deadline"); Native.Check(Native.GetExitCodeProcess(helper.hProcess, out exit), "Read WGC result"); }
                    finally { Native.CloseHandle(helper.hThread); Native.CloseHandle(helper.hProcess); }
                    object? pixels = null;
                    if (File.Exists(png)) { using var image = new Bitmap(png); pixels = CompatibilityVerification.PixelEvidence(new(File.ReadAllBytes(png), image.Width, image.Height, 0, "WGC offscreen Default desktop"), Path.Combine(folder, phase + "-inspected.png"), image.Width / 2, image.Height / 2, phase != "before"); }
                    results.Add(new { mode = "owned offscreen nonactivating D3D source", phase, initiallyHidden, foreignCloakHresult = $"0x{foreignCloak:X8}", exit, pixels, error = File.Exists(Path.ChangeExtension(png, ".error.txt")) ? File.ReadAllText(Path.ChangeExtension(png, ".error.txt")) : null, observed = CompatibilityVerification.Read(folder), foregroundEquality = Native.GetForegroundWindow() == foreground });
                }
                pids = Native.JobProcessIds(job);
            }
            finally { Native.CloseHandle(app.hThread); Native.CloseHandle(app.hProcess); }
        }
        catch (Exception e) { results.Add(new { error = e.ToString() }); }
        finally { Native.CloseHandle(job); }
        isolated.Dispose(); Thread.Sleep(200);
        var report = new { results, cleanup = pids.All(pid => !CompatibilityVerification.Alive(pid)), foregroundEquality = Native.GetForegroundWindow() == foreground, inputDesktopUnchanged = Native.InputDesktopName() == "Default", note = "No physical input, focus restoration, cursor motion or desktop switch. Offscreen mode has weaker isolation and only a startup-compliant disposable fixture was launched. Native Space toggles its normal shader state." };
        File.WriteAllText(Path.Combine(output, "presentation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(report));
        return 0;
    }
}

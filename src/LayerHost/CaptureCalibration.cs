using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class CaptureCalibration
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var before = Native.GetForegroundWindow(); string inputDesktop = Native.InputDesktopName();
        nint job = Native.CreateJobObjectW(0, null);
        var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
        Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Calibration job");
        object? report = null;
        try
        {
            var app = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/D3DProbe.exe"), ["--noactivate"], "WinSta0\\" + inputDesktop, output);
            try
            {
                Native.Check(Native.AssignProcessToJobObject(job, app.hProcess), "Own calibration app"); Native.ResumeThread(app.hThread);
                WindowInfo? root = null; var watch = Stopwatch.StartNew();
                while (root == null && watch.ElapsedMilliseconds < 5000)
                {
                    Native.EnumProc callback = (hwnd, _) => { Native.GetWindowThreadProcessId(hwnd, out uint pid); if (pid == app.dwProcessId && Native.IsWindowVisible(hwnd)) root = new((long)hwnd, 0, (int)pid, Native.ClassName(hwnd), Native.WindowText(hwnd), true, true, 0, 0, 0, 0); return true; };
                    Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), callback, 0); Thread.Sleep(50);
                }
                if (root == null) throw new InvalidOperationException("Calibration fixture did not appear.");
                Thread.Sleep(350);
                string png = Path.Combine(output, "control-wgc.png");
                var helper = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/WgcCapture.exe"), [root.Handle.ToString(), png], "WinSta0\\" + inputDesktop, output);
                uint code;
                try { Native.Check(Native.AssignProcessToJobObject(job, helper.hProcess), "Own calibration helper"); Native.ResumeThread(helper.hThread); if (Native.WaitForSingleObject(helper.hProcess, 5000) != 0) throw new TimeoutException("Calibration WGC timeout."); Native.Check(Native.GetExitCodeProcess(helper.hProcess, out code), "Calibration result"); }
                finally { Native.CloseHandle(helper.hThread); Native.CloseHandle(helper.hProcess); }
                object? pixels = null; if (File.Exists(png)) { using var bitmap = new Bitmap(png); pixels = CompatibilityVerification.PixelEvidence(new(File.ReadAllBytes(png), bitmap.Width, bitmap.Height, watch.Elapsed.TotalMilliseconds, "WGC full window on existing input desktop"), Path.Combine(output, "control-inspected.png"), bitmap.Width / 2, bitmap.Height / 2, false); }
                report = new { mode = "bounded nonactivating capture calibration on existing input desktop; no input sent", code, pixels, error = File.Exists(Path.ChangeExtension(png, ".error.txt")) ? File.ReadAllText(Path.ChangeExtension(png, ".error.txt")) : null, foregroundUnchanged = Native.GetForegroundWindow() == before, inputDesktopUnchanged = Native.InputDesktopName() == inputDesktop, pids = Native.JobProcessIds(job) };
            }
            finally { Native.CloseHandle(app.hThread); Native.CloseHandle(app.hProcess); }
        }
        catch (Exception e) { report = new { error = e.ToString() }; }
        finally { Native.CloseHandle(job); }
        File.WriteAllText(Path.Combine(output, "calibration.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report)); return 0;
    }
}

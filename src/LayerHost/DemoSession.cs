using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

internal static class DemoSession
{
    internal static int Run(string[] arguments)
    {
        string? readyFile = null, qaReportDirectory = null; bool hidden = false, duplicate = false, qtSoftware = false, queuePointer = false, rootInput = false, wpfSoftwareHook = false; CaptureBackend backend = CaptureBackend.PrintWindow;
        int i = 0;
        bool hiddenNotifications = false, postedShortcuts = false, fitPreview = false;
        int inspectionSettle = 150;
        bool qivNativeStartup = false;
        ResearchGpuTarget? gpuTarget = null;
        while (i < arguments.Length)
        {
            if (arguments[i] == "--ready-file" && i + 1 < arguments.Length) { readyFile = Path.GetFullPath(arguments[i + 1]); i += 2; }
            else if (arguments[i] == "--qa-report-directory" && i + 1 < arguments.Length) { qaReportDirectory = Path.GetFullPath(arguments[i + 1]); i += 2; }
            else if (arguments[i] == "--hidden") { hidden = true; i++; }
            else if (arguments[i] == "--allow-duplicate") { duplicate = true; i++; }
            else if (arguments[i] == "--cooperative-fixture") { backend = CaptureBackend.CooperativeFixture; i++; }
            else if (arguments[i] == "--full-content") { backend = CaptureBackend.PrintWindowFullContent; i++; }
            else if (arguments[i] == "--queue-pointer") { queuePointer = true; i++; }
            else if (arguments[i] == "--root-input") { rootInput = true; i++; }
            else if (arguments[i] == "--posted-shortcuts") { postedShortcuts = true; i++; }
            else if (arguments[i] == "--fit-preview") { fitPreview = true; i++; }
            else if (arguments[i] == "--experimental-hidden-input") { hiddenNotifications = true; i++; }
            else if (arguments[i] == "--qiv-native-startup") { qivNativeStartup = true; i++; }
            else if (arguments[i] == "--experimental-gpu-bridge")
            {
                if (i + 1 >= arguments.Length) throw new ArgumentException("Select the exact research GPU target: mpv, dynamic or qiv.");
                gpuTarget = arguments[i + 1] switch { "mpv" => ResearchGpuTarget.Mpv, "dynamic" => ResearchGpuTarget.DynamicProbe, "qiv" => ResearchGpuTarget.Qiv, _ => throw new ArgumentException("Select the exact research GPU target: mpv, dynamic or qiv.") };
                i += 2;
            }
            else if (arguments[i] == "--inspection-settle")
            {
                if (i + 1 >= arguments.Length || !int.TryParse(arguments[i + 1], out inspectionSettle) || inspectionSettle is < 0 or > 500) throw new ArgumentException("Use --inspection-settle with 0–500 milliseconds.");
                i += 2;
            }
            else if (arguments[i] == "--wpf-software-hook") { wpfSoftwareHook = true; i++; }
            else if (arguments[i] == "--qt-software") { qtSoftware = true; i++; }
            else break;
        }
        string executable = i < arguments.Length ? Path.GetFullPath(arguments[i++]) : Program.FindProbe();
        if (readyFile != null || qaReportDirectory != null)
        {
            string artifactRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts")) + Path.DirectorySeparatorChar;
            if ((readyFile != null && !readyFile.StartsWith(artifactRoot, StringComparison.OrdinalIgnoreCase)) || (qaReportDirectory != null && !qaReportDirectory.StartsWith(artifactRoot, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Demo status and QA reports must stay inside this project's artifacts directory.");
        }
        string[] appArgs = arguments.Skip(i).ToArray();
        if (gpuTarget != null && (backend != CaptureBackend.PrintWindow || qtSoftware || wpfSoftwareHook || hiddenNotifications)) throw new ArgumentException("Choose the experimental GPU bridge separately from other rendering adapters.");
        void Status(string state, string? error = null) => WriteStatus(readyFile, new { state, pid = Environment.ProcessId, executable, hidden, error });
        string identity = AppContext.BaseDirectory + "|" + Environment.UserName + "|" + Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId()));
        string name = "Local\\VIAVW_Demo_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        using var mutex = new Mutex(false, name);
        bool held = false;
        try
        {
            if (gpuTarget == ResearchGpuTarget.Qiv && (appArgs.Length != 0 || !string.Equals(executable, Path.GetFullPath(QivResearch.Executable), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Use the pinned qIV executable without app arguments. This demo prepares its controlled dedicated configuration and disposable images.");
            if(qivNativeStartup && gpuTarget!=ResearchGpuTarget.Qiv)throw new ArgumentException("Native guard-only startup is restricted to the controlled pinned qIV demo.");
            try { held = duplicate || mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held)
            {
                const string message = "A demo from this build directory is already running on this desktop. Close it first, or explicitly use -AllowDuplicate. No existing process was attached to or terminated.";
                Status("error", message); Console.Error.WriteLine(message); return 2;
            }
            if (!File.Exists(executable)) throw new FileNotFoundException("Application executable does not exist.", executable);
            Status("starting");
            Native.DetachSoleOwnedConsole(); // Never hides/detaches the user's shared terminal.
            ApplicationConfiguration.Initialize();
            using var form = new DemoForm(executable, appArgs, hidden, backend, readyFile == null ? null : readyFile + ".show", qtSoftware, queuePointer, rootInput, wpfSoftwareHook, qaReportDirectory, hiddenNotifications, inspectionSettle, gpuTarget, qivNativeStartup, postedShortcuts, fitPreview);
            string? startupError = null;
            form.StartupCompleted += error => { startupError = error; WriteStatus(readyFile, new { state = error == null ? "ready" : "error", pid = Environment.ProcessId, executable, hidden, visible = form.Visible, ownedProcessIds = form.OwnedProcesses(), buildDirectory = AppContext.BaseDirectory, error }); };
            Application.Run(form);
            if (startupError == null) Status("closed"); return startupError == null ? 0 : 1;
        }
        catch (Exception e) { Status("error", e.Message); Console.Error.WriteLine(e); return 1; }
        finally { if (held && !duplicate) mutex.ReleaseMutex(); }
    }
    static void WriteStatus(string? path, object status)
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status));
        File.Move(path + ".tmp", path, true);
    }
}

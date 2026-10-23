using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

// One lazy capture process. Its nested job can be closed without killing the source application.
internal sealed class ReusableCaptureClient : IDisposable
{
    readonly nint parentJob;
    readonly string desktop, directory;
    readonly bool faultFixture;
    NamedPipeServerStream? pipe; StreamReader? reader; StreamWriter? writer;
    nint helperJob, process;
    internal int ProcessId { get; private set; }
    internal int Starts { get; private set; }
    internal JsonElement LastResult { get; private set; }
    internal ReusableCaptureClient(nint parentJob, string desktop, string directory, bool faultFixture = false)
    {
        this.parentJob = parentJob; this.desktop = desktop; this.directory = Path.GetFullPath(directory); this.faultFixture = faultFixture;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        string relative = Path.GetRelativePath(root, this.directory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new ArgumentException("Capture output must stay under project artifacts.");
        Directory.CreateDirectory(this.directory);
    }
    void Start()
    {
        if (process != 0) return;
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string name = "VIAVW_WGC_" + Guid.NewGuid().ToString("N"), token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            helperJob = Native.CreateJobObjectW(0, null); Native.Check(helperJob != 0, "Capture helper job");
            var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
            Native.Check(Native.SetInformationJobObject(helperJob, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Capture helper containment");
            var args = new List<string> { "--pipe", name, token }; if (faultFixture) args.Add("--test-fixture");
            var launched = Native.LaunchSuspended(Path.Combine(project, "artifacts/native-build/WgcWorker.exe"), args, "WinSta0\\" + desktop, directory);
            process = launched.hProcess; ProcessId = (int)launched.dwProcessId;
            try
            {
                if (parentJob != 0) Native.Check(Native.AssignProcessToJobObject(parentJob, process), "Assign capture worker to source scope");
                Native.Check(Native.AssignProcessToJobObject(helperJob, process), "Assign nested capture job");
                Native.Check(Native.ResumeThread(launched.hThread) != uint.MaxValue, "Resume capture worker");
            }
            catch { Native.TerminateProcess(process, 1); throw; }
            finally { Native.CloseHandle(launched.hThread); }
            using var deadline = new CancellationTokenSource(1000);
            pipe.WaitForConnectionAsync(deadline.Token).GetAwaiter().GetResult();
            reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true); writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
            var hello = JsonSerializer.Deserialize<JsonElement>(reader.ReadLineAsync(deadline.Token).AsTask().GetAwaiter().GetResult() ?? throw new IOException("Capture handshake missing"));
            if (hello.GetProperty("token").GetString() != token || hello.GetProperty("pid").GetInt32() != ProcessId || hello.GetProperty("desktop").GetString() != desktop) throw new IOException("Capture handshake mismatch");
            Starts++;
        }
        catch { Stop(); throw; }
    }
    JsonElement Request(string packet, int timeout = 3500)
    {
        Start();
        try
        {
            using var deadline = new CancellationTokenSource(timeout);
            writer!.WriteLineAsync(packet.AsMemory(), deadline.Token).GetAwaiter().GetResult();
            string line = reader!.ReadLineAsync(deadline.Token).AsTask().GetAwaiter().GetResult() ?? throw new IOException("Capture worker exited");
            LastResult = JsonSerializer.Deserialize<JsonElement>(line);
            if (!LastResult.GetProperty("success").GetBoolean()) throw new LayerOperationException(LastResult.ToString());
            return LastResult;
        }
        catch (LayerOperationException) { throw; }
        catch (OperationCanceledException e) { Stop(); throw new TimeoutException("Reusable GPU capture deadline; only its capture worker was terminated. Source application retained.", e); }
        catch { Stop(); throw; }
    }
    internal JsonElement Capture(nint window, string png)
    {
        png = Path.GetFullPath(png);
        if (Path.GetDirectoryName(png) != directory || png.IndexOfAny(['\t', '\r', '\n']) >= 0) throw new ArgumentException("Use a capture file in this worker's directory.");
        Native.GetWindowThreadProcessId(window, out uint pid); Native.Check(Native.GetWindowRect(window, out var bounds), "Capture source bounds");
        if (File.Exists(png)) File.Delete(png); // Single replaceable frame, never browser profile data.
        return Request(string.Join('\t', ((long)window).ToString(), pid.ToString(), (bounds.Right - bounds.Left).ToString(), (bounds.Bottom - bounds.Top).ToString(), png));
    }
    internal void StallForTest()
    {
        if (!faultFixture) throw new InvalidOperationException("Fault injection disabled for this capture worker.");
        Request("hang", 700);
    }
    internal void Restart() => Stop();
    void Stop()
    {
        try { writer?.Dispose(); } catch (IOException) { }
        try { reader?.Dispose(); } catch (IOException) { }
        pipe?.Dispose(); writer = null; reader = null; pipe = null;
        if (helperJob != 0) { Native.CloseHandle(helperJob); helperJob = 0; }
        if (process != 0) { Native.WaitForSingleObject(process, 1000); Native.CloseHandle(process); process = 0; }
        ProcessId = 0;
    }
    public void Dispose() => Stop();
}

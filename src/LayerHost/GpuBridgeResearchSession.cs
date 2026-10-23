using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal enum ResearchGpuTarget { AcceptedProbe, DynamicProbe, Mpv, Qiv }

// Explicit in-process adapter for exact owned, hash-checked research targets only.
// Never used by default viewers or arbitrary installed applications.
internal sealed class GpuBridgeResearchSession : IDisposable
{
    readonly ApplicationLayer layer;
    readonly long root, created;
    readonly int pid;
    readonly ResearchGpuTarget target;
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    Process helper = null!;
    Task<string>? errors;
    bool disposed;
    internal string MappingName { get; } = "Local\\VIAVW_GPU_" + Guid.NewGuid().ToString("N");
    internal int HelperPid => helper.Id;
    internal uint SourceThreadId => view.ReadUInt32(12);
    internal JsonElement StartupMetrics { get; private set; }
    internal JsonElement CursorGuardState => JsonSerializer.SerializeToElement(new { installed = view.ReadInt32(384), clipRequests = view.ReadInt32(388), warpRequests = view.ReadInt32(392) });
    internal GpuBridgeResearchSession(ApplicationLayer layer, long root, ResearchGpuTarget target = ResearchGpuTarget.AcceptedProbe)
    {
        this.layer = layer; this.root = root; this.target = target; pid = layer.ApplicationProcessId;
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string expected = Path.GetFullPath(Path.Combine(project, target switch { ResearchGpuTarget.AcceptedProbe => "artifacts/gpu-research-build/D3DProbe.exe", ResearchGpuTarget.DynamicProbe => "artifacts/gpu-dynamic-build/DynamicD3DProbe.exe", ResearchGpuTarget.Mpv => ".deps/mpv-7c4f662c0/mpv.exe", ResearchGpuTarget.Qiv => ".deps/qiv-3.2.0.452/QuickImageViewer.exe", _ => throw new ArgumentOutOfRangeException(nameof(target)) }));
        string accepted = Path.Combine(project, "artifacts/baselines/accepted-capture-timing-20261014/native-build/D3DProbe.exe");
        using var process = Process.GetProcessById(pid);
        if (!string.Equals(process.MainModule!.FileName, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("GPU instrumentation is restricted to the explicitly selected project research executable.");
        if (target == ResearchGpuTarget.AcceptedProbe)
        { if (!File.ReadAllBytes(expected).AsSpan().SequenceEqual(File.ReadAllBytes(accepted))) throw new InvalidOperationException("GPU instrumentation is restricted to the unchanged accepted project D3DProbe executable."); }
        else
        {
            string expectedHash = File.ReadAllText(Path.Combine(project, target == ResearchGpuTarget.DynamicProbe ? "artifacts/gpu-dynamic-build/DynamicD3DProbe.sha256" : target == ResearchGpuTarget.Qiv ? ".deps/qiv-3.2.0.452/qiv.sha256" : ".deps/mpv-7c4f662c0/mpv.sha256")).Trim();
            using var executable = File.OpenRead(expected);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(executable)), expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Research executable does not match its pinned/build hash.");
        }
        var census = ProcessCensus.Read(layer.GetProcessIds()).First(entry => entry.Pid == pid); created = census.CreationFileTime ?? throw new InvalidOperationException("No source identity.");
        string className = target switch { ResearchGpuTarget.AcceptedProbe => "VIAVW_D3DProbe", ResearchGpuTarget.DynamicProbe => "VIAVW_DynamicD3DProbe", ResearchGpuTarget.Qiv => "QuickImageViewer_VIAVW_" + layer.Id[6..], _ => "mpv" };
        var window = layer.GetWindows().First(candidate => candidate.Handle == root); if (window.ProcessId != pid || window.Parent != 0 || window.ClassName != className) throw new InvalidOperationException("Research source must be the exact owned allowlisted root.");
        mapping = MemoryMappedFile.CreateNew(MappingName, 4096, MemoryMappedFileAccess.ReadWrite); view = mapping.CreateViewAccessor(); view.Write(0, 0x56494156); view.Write(4, 1); view.Write(8, pid); view.Write(16, root);
        try { helper = layer.StartGpuResearchHelper(target != ResearchGpuTarget.AcceptedProbe, target == ResearchGpuTarget.Qiv); errors = helper.StandardError.ReadToEndAsync(); StartupMetrics = Command($"install\t{pid}\t{root}\t{created}\t{MappingName}\t{layer.Id}"); }
        catch { Dispose(); throw; }
    }
    JsonElement Command(string command)
    {
        using var timeout = new CancellationTokenSource(5000); string? response;
        try { helper.StandardInput.WriteLine(command); helper.StandardInput.Flush(); response = helper.StandardOutput.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            // A hung graphics reader must not remain queued behind future commands.
            // Kill only this known owned helper; its source stays in the layer job.
            if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(1000); }
            throw new TimeoutException("GPU reader deadline exceeded; the owned reader was stopped. RestartHelper can reattach and reset its surfaces.");
        }
        if (response == null) throw new InvalidOperationException("GPU research helper exited: " + (errors?.IsCompleted == true ? errors.Result : ""));
        using var document = JsonDocument.Parse(response); var value = document.RootElement.Clone(); if (!value.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("GPU bridge operation failed: " + value); return value;
    }
    internal JsonElement Capture(string path, bool invalidate = false) => layer.ResearchTransaction<JsonElement>(null, () => Command((invalidate ? "capture-paint\t" : "capture\t") + Path.GetFullPath(path)));
    internal void ResetResources() => layer.ResearchTransaction<object?>(null, () => { Command("reset"); return null; });
    internal void ResizeClient(int width, int height, ControlOwner sender = ControlOwner.Automation) => layer.ResearchTransaction<object?>(sender, () =>
    {
        if (target == ResearchGpuTarget.AcceptedProbe || width is < 160 or > 1920 || height is < 120 or > 1080) throw new ArgumentException("Resize is bounded and available only in the dynamic experiment.");
        Command($"resize\t{width}\t{height}"); return null;
    });
    internal void Chord(int[] keys, ControlOwner sender = ControlOwner.Automation) => layer.ResearchTransaction(sender, () =>
    {
        if (target is ResearchGpuTarget.Mpv or ResearchGpuTarget.Qiv) throw new InvalidOperationException("Ordinary application research input uses the existing native message path; no asynchronous key adapter is installed.");
        if (keys.Length is < 1 or > 8 || keys.Any(key => key is < 1 or > 255)) throw new ArgumentException("Bound virtual research chord.");
        try
        {
            foreach (int key in keys) { view.Write(88 + key, (byte)1); if (key == 0x11) view.Write(88 + 0xA2, (byte)1); if (key == 0x10) view.Write(88 + 0xA0, (byte)1); if (key == 0x12) view.Write(88 + 0xA4, (byte)1); }
            Thread.MemoryBarrier(); view.Write(84, 1); layer.SendQueueStateChord(root, keys, sender: sender); return true;
        }
        finally { view.Write(84, 0); Thread.MemoryBarrier(); for (int key = 0; key < 256; key++) view.Write(88 + key, (byte)0); }
    });
    internal JsonElement State => JsonSerializer.SerializeToElement(new { initialized = view.ReadInt32(24), requested = view.ReadInt32(28), completed = view.ReadInt32(32), error = view.ReadInt32(36), width = view.ReadInt32(40), height = view.ReadInt32(44), format = view.ReadInt32(48), generation = view.ReadInt32(52), copied = view.ReadInt32(72), skipped = view.ReadInt32(76), presents = view.ReadInt32(80), activeKeys = view.ReadInt32(84), acknowledged = view.ReadInt32(364), present1Calls = view.ReadInt32(368), resizeCalls = view.ReadInt32(372), sourceDevices = view.ReadInt32(376), keyAdapterInstalled = view.ReadInt32(380) });
    internal void RestartHelper() => layer.ResearchTransaction<object?>(null, () =>
    {
        if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(2000); } helper.Dispose();
        helper = layer.StartGpuResearchHelper(target != ResearchGpuTarget.AcceptedProbe, target == ResearchGpuTarget.Qiv); errors = helper.StandardError.ReadToEndAsync(); StartupMetrics = Command($"attach\t{pid}\t{root}\t{created}\t{MappingName}\t{layer.Id}"); Command("reset"); return null;
    });
    internal void StopAdapter() => layer.ResearchTransaction<object?>(null, () => { Command("stop"); return null; });
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { if (helper != null && !helper.HasExited) { if (!layer.Closed && view.ReadInt32(24) == 1) try { Command("stop"); } catch { } try { helper.StandardInput.WriteLine("exit"); helper.StandardInput.Flush(); } catch { } if (!helper.WaitForExit(1500)) { helper.Kill(); helper.WaitForExit(1000); } } }
        finally { helper?.Dispose(); view.Dispose(); mapping.Dispose(); }
    }
}

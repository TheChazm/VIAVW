using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

// Exact pinned, owned private qIV only. No retained helper, GPU device or capture hook.
internal sealed class QivCursorGuardSession : IDisposable
{
    readonly ApplicationLayer layer;
    readonly long root,created;
    readonly int pid;
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    bool disposed;
    internal string MappingName { get; }="Local\\VIAVW_QIV_GUARD_"+Guid.NewGuid().ToString("N");
    internal JsonElement Setup { get; }
    internal int InstallerPid { get; private set; }
    internal JsonElement State => JsonSerializer.SerializeToElement(new { installed=view.ReadInt32(24),clipRequests=view.ReadInt32(28),warpRequests=view.ReadInt32(32),error=view.ReadInt32(36),clipSlots=view.ReadUInt32(48),warpSlots=view.ReadUInt32(52) });
    internal QivCursorGuardSession(ApplicationLayer layer,long root)
    {
        this.layer=layer;this.root=root;pid=layer.ApplicationProcessId;
        using var process=Process.GetProcessById(pid);
        if(!layer.GetProcessIds().Contains(pid) || !string.Equals(process.MainModule!.FileName,Path.GetFullPath(QivResearch.Executable),StringComparison.OrdinalIgnoreCase) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(QivResearch.Executable)))!=QivResearch.Sha256)throw new InvalidOperationException("Cursor protection accepts only the pinned owned qIV executable.");
        var window=layer.GetWindows().Single(w=>w.Handle==root);
        if(window.ProcessId!=pid || window.Parent!=0 || window.ClassName!="QuickImageViewer_"+layer.Id)throw new InvalidOperationException("Cursor protection requires the configured owned private qIV root.");
        created=ProcessCensus.Read([pid]).Single().CreationFileTime??throw new InvalidOperationException("No qIV creation identity.");
        mapping=MemoryMappedFile.CreateNew(MappingName,4096,MemoryMappedFileAccess.ReadWrite);view=mapping.CreateViewAccessor();
        view.Write(0,0x51495647);view.Write(4,1);view.Write(8,pid);view.Write(16,root);view.Write(40,created);
        try{Setup=Invoke("install");if(view.ReadInt32(24)!=1 || Setup.GetProperty("helperGraphicsModules").GetBoolean() || Setup.GetProperty("gpuBridgeLoaded").GetBoolean())throw new InvalidOperationException("Guard-only setup failed verification.");}
        catch{Dispose();throw;}
    }
    internal JsonElement Verify() => Invoke("verify");
    internal JsonElement Probe() => Invoke("probe");
    JsonElement Invoke(string operation) => layer.ResearchTransaction<JsonElement>(null,()=>
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        using var helper=layer.StartQivGuardHelper();InstallerPid=helper.Id;var errors=helper.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(5000);
        try
        {
            helper.StandardInput.WriteLine($"{operation}\t{pid}\t{root}\t{created}\t{layer.Id}\t{MappingName}");helper.StandardInput.Flush();
            string? line=helper.StandardOutput.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            if(!helper.WaitForExit(1000))throw new TimeoutException("Guard installer exit deadline.");
            if(line==null)throw new InvalidOperationException("No guard installer result: "+(errors.IsCompleted?errors.Result:""));
            using var document=JsonDocument.Parse(line);var result=document.RootElement.Clone();
            if(helper.ExitCode!=0 || !result.GetProperty("ok").GetBoolean())throw new InvalidOperationException("Guard operation failed: "+result);return result;
        }
        finally{if(!helper.HasExited){helper.Kill();helper.WaitForExit(1000);}}
    });
    public void Dispose(){if(disposed)return;disposed=true;view.Dispose();mapping.Dispose();}
}

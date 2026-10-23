using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

// A passive provider can block inside COM. Keep it out of the input worker and
// the application job: a deadline terminates only this disposable helper.
internal sealed class PassiveInspection : IDisposable
{
    internal const int ProviderDeadlineMilliseconds = 750;
    readonly object sync = new();
    readonly string desktop;
    readonly bool stall;
    readonly Dictionary<long, Entry> entries = [];
    long next;
    bool disposed;
    nint job, process;
    NamedPipeServerStream? pipe;
    StreamReader? reader;
    StreamWriter? writer;
    int workerPid;
    Entry? active;
    sealed class Entry(long ticket, Request request)
    {
        internal readonly long Ticket = ticket;
        internal readonly Request Request = request;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Status = "pending";
        internal JsonElement? Result;
        internal string? Error;
        internal int WorkerPid;
    }
    internal PassiveInspection(string desktop, bool stall = false) { this.desktop = desktop; this.stall = stall; }
    internal long Begin(Request request)
    {
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if(active is {} running && !running.Done.Task.IsCompleted)
            {
                throw new InspectionBusyException("An inspection is already pending; poll that ticket before starting a new sample.");
            }
            var entry = new Entry(++next, request); active = entry; entries.Add(entry.Ticket, entry);
            while(entries.Count > 32) entries.Remove(entries.Keys.Min());
            _ = Task.Run(() => Run(entry));
            return entry.Ticket;
        }
    }
    internal bool Finished(long ticket) { lock(sync) return Find(ticket).Done.Task.IsCompleted; }
    Entry Find(long ticket) => entries.TryGetValue(ticket, out var entry) ? entry : throw new InvalidOperationException("Inspection ticket expired or does not belong to this layer.");
    internal JsonElement Poll(long ticket)
    {
        lock(sync)
        {
            var e = Find(ticket);
            return JsonSerializer.SerializeToElement(new { ticket, status = e.Status, window = e.Request.Window,
                workerPid = e.WorkerPid, milliseconds = e.Clock.Elapsed.TotalMilliseconds,
                providerDeadlineMilliseconds = ProviderDeadlineMilliseconds, result = e.Result, error = e.Error });
        }
    }
    internal JsonElement Sample(long ticket, int waitMilliseconds)
    {
        Task done; lock(sync) done = Find(ticket).Done.Task;
        // Compatibility path only. New callers use Begin/Poll without waiting.
        done.Wait(waitMilliseconds);
        var poll = Poll(ticket);
        var result = poll.GetProperty("result");
        var fields = result.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(result.GetRawText())!
            : new Dictionary<string,JsonElement> { ["nodes"] = JsonSerializer.SerializeToElement(Array.Empty<object>()), ["errors"] = JsonSerializer.SerializeToElement(Array.Empty<string>()), ["truncated"] = JsonSerializer.SerializeToElement(true) };
        fields["inspection"] = poll;
        return JsonSerializer.SerializeToElement(fields);
    }
    async Task Connect()
    {
        NamedPipeServerStream connection;
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if(pipe?.IsConnected == true) return;
            string name = "VIAVW_inspect_" + Guid.NewGuid().ToString("N");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            pipe = connection = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            job = Native.CreateJobObjectW(0, null); Native.Check(job != 0, "Create passive inspection job");
            var limits = new Native.EXTENDED_LIMIT { BasicLimitInformation = new Native.BASIC_LIMIT { LimitFlags = 0x2000 } };
            Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.EXTENDED_LIMIT>()), "Contain passive inspection helper");
            var child = Native.LaunchSuspended(Path.Combine(AppContext.BaseDirectory, "LayerHost.exe"),
                [stall ? "--inspection-stall-worker" : "--inspection-worker", name, token], "WinSta0\\" + desktop, Environment.CurrentDirectory);
            process = child.hProcess; workerPid = (int)child.dwProcessId;
            try { Native.Check(Native.AssignProcessToJobObject(job, process), "Assign passive helper"); Native.Check(Native.ResumeThread(child.hThread) != uint.MaxValue, "Resume passive helper"); }
            finally { Native.CloseHandle(child.hThread); }
            handshakeToken = token;
        }
        using var deadline = new CancellationTokenSource(1500);
        await connection.WaitForConnectionAsync(deadline.Token);
        reader = new StreamReader(connection, Encoding.UTF8, false, 4096, true);
        writer = new StreamWriter(connection, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var hello = JsonSerializer.Deserialize<JsonElement>(await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("Inspection handshake missing"));
        if(hello.GetProperty("token").GetString() != handshakeToken || hello.GetProperty("desktop").GetString() != desktop || hello.GetProperty("pid").GetInt32() != workerPid)
            throw new IOException("Inspection worker identity mismatch");
    }
    string? handshakeToken;
    async Task Run(Entry entry)
    {
        try
        {
            await Connect();
            lock(sync) { ObjectDisposedException.ThrowIf(disposed,this); entry.WorkerPid = workerPid; }
            using var deadline = new CancellationTokenSource(ProviderDeadlineMilliseconds);
            await writer!.WriteLineAsync(JsonSerializer.Serialize(entry.Request).AsMemory(), deadline.Token);
            while(true)
            {
                string line = await reader!.ReadLineAsync(deadline.Token) ?? throw new IOException("Inspection helper disconnected");
                var reply = JsonSerializer.Deserialize<Reply>(line)!;
                if(!reply.Success) throw new LayerOperationException(reply.Error ?? "Inspection failed");
                lock(sync)
                {
                    entry.Result = reply.Data.GetProperty("result").Clone();
                    entry.Status = reply.Data.GetProperty("status").GetString()!;
                }
                if(entry.Status == "complete") break;
            }
        }
        catch(Exception error)
        {
            lock(sync)
            {
                entry.Status = disposed ? "cancelled" : error is OperationCanceledException ? "timed_out" : "failed";
                entry.Error = error is OperationCanceledException ? "Passive inspection deadline exceeded; only its helper was stopped. No input was replayed." : error.Message;
                Reset();
            }
        }
        finally { entry.Clock.Stop(); entry.Done.TrySetResult(); }
    }
    void Reset()
    {
        // Separate job has no application process. Also terminate a suspended
        // helper if assignment failed, using the retained process handle.
        if(process != 0) { Native.TerminateProcess(process, 3); Native.CloseHandle(process); process = 0; }
        if(job != 0) { Native.CloseHandle(job); job = 0; }
        try { pipe?.Dispose(); } catch(IOException) { }
        pipe = null; reader = null; writer = null; workerPid = 0;
    }
    public void Dispose() { lock(sync) { if(disposed) return; disposed = true; Reset(); } }
}
internal sealed class InspectionBusyException(string message):InvalidOperationException(message);

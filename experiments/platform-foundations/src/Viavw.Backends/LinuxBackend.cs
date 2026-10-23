// Copyright (c) 2026 The Coded Craftsman, LLC. MIT; see LICENSE.
namespace Viavw.Backends;

// Internal seams keep lifecycle fault tests from running target programs on the
// development machine. The production implementation always checks local Linux.
internal interface IOwnedDisplay : IDisposable
{
    bool HasExited { get; }
    Task<bool> StopAsync(TimeSpan timeout);
}
internal interface ILinuxRuntime
{
    IReadOnlyList<string> Preflight(LinuxOptions options);
    string Prepare(LinuxOptions options, string id, byte[] cookie);
    IOwnedDisplay Start(CommandPlan plan);
    Task AuthenticateAsync(LinuxOptions options, byte[] cookie, CancellationToken cancellationToken);
    void RemoveOwnedFiles(string directory);
}

public sealed class LinuxXvncBackend : IPlatformBackend
{
    readonly LinuxOptions options;
    readonly ILinuxRuntime runtime;
    readonly SemaphoreSlim gate = new(1, 1);
    int reserved;
    public LinuxXvncBackend(LinuxOptions options) : this(options, new LinuxRuntime()) { }
    internal LinuxXvncBackend(LinuxOptions options, ILinuxRuntime runtime)
    {
        LinuxLaunchPlan.Validate(options); this.options = options; this.runtime = runtime;
    }
    public BackendCapabilities Discover()
    {
        var blockers = runtime.Preflight(options);
        return new("linux-xvnc", blockers.Count == 0, false, false, false, false, blockers);
    }
    public async Task<IIsolatedSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref reserved) >= 4) throw new BackendException("session_budget", "Four display reservations per backend, including uncertain cleanup.");
            var blockers = runtime.Preflight(options);
            if (blockers.Count != 0) throw new BackendException("preflight_failed", string.Join(", ", blockers));
            Interlocked.Increment(ref reserved);
            string id = Guid.NewGuid().ToString("N"); string? directory = null; IOwnedDisplay? process = null;
            var cookie = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            try
            {
                directory = runtime.Prepare(options, id, cookie);
                cancellationToken.ThrowIfCancellationRequested();
                process = runtime.Start(LinuxLaunchPlan.Server(options, directory));
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(options.StartupTimeoutMilliseconds);
                await runtime.AuthenticateAsync(options, cookie, deadline.Token);
                if (process.HasExited) throw new IOException("Owned Xvnc exited during readiness.");
                return new LinuxSession(id, options, directory, process, runtime, Release);
            }
            catch (Exception error)
            {
                bool stopped = process is null && error is not BackendException { Outcome: OperationOutcome.Unknown };
                if (process is not null)
                {
                    try { stopped = await process.StopAsync(TimeSpan.FromMilliseconds(options.StopTimeoutMilliseconds)); }
                    catch { stopped = false; }
                    if (stopped) process.Dispose();
                }
                if (stopped)
                {
                    try { if (directory is not null) runtime.RemoveOwnedFiles(directory); }
                    catch (Exception cleanupError) { throw new BackendException("cleanup_uncertain", $"Display stopped, but retain reservation {id} and files at {directory} for reconciliation.", OperationOutcome.Unknown, cleanupError); }
                    Interlocked.Decrement(ref reserved);
                }
                // Never replay create after an uncertain cleanup. Reservation and
                // files remain; no scan-by-name, display adoption or PID kill.
                if (!stopped) throw new BackendException("cleanup_uncertain", $"Owned startup could not be confirmed stopped; retain reservation {id} at {directory} for operator reconciliation.", OperationOutcome.Unknown, error);
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
                throw new BackendException("startup_failed", "No display retained; Xvnc readiness failed or timed out.", process is null ? OperationOutcome.NotStarted : OperationOutcome.Completed, error);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(cookie); }
        }
        finally { gate.Release(); }
    }
    void Release() => Interlocked.Decrement(ref reserved);
}

internal sealed class LinuxSession(string id, LinuxOptions options, string directory,
    IOwnedDisplay process, ILinuxRuntime runtime, Action release) : IIsolatedSession
{
    readonly SemaphoreSlim gate = new(1, 1);
    SessionStatus status = new(id, SessionState.DisplayReady, ControlOwner.Human, 0,
        ":" + options.DisplayNumber, "Authenticated X display only; application/capture/input are unavailable.");
    bool released, displayStopped;
    public SessionStatus Status
    {
        get
        {
            gate.Wait();
            try
            {
                var snapshot = Volatile.Read(ref status);
                return snapshot.State == SessionState.DisplayReady && process.HasExited
                    ? snapshot with { State = SessionState.Uncertain, Reason = "Owned display exited; close/reconcile retained files." }
                    : snapshot;
            }
            finally { gate.Release(); }
        }
    }
    public SessionStatus ChangeOwner(long expectedRevision, ControlOwner owner)
    {
        gate.Wait();
        try
        {
            RequireRevision(expectedRevision);
            if (!Enum.IsDefined(owner)) throw new BackendException("invalid_owner", "Unknown ownership value.");
            if (status.State != SessionState.DisplayReady || process.HasExited)
                throw new BackendException("session_unavailable", "Display is closed, exited or uncertain.");
            if (owner != status.Owner) Volatile.Write(ref status, status with { Owner = owner, OwnershipRevision = checked(status.OwnershipRevision + 1) });
            return status;
        }
        finally { gate.Release(); }
    }
    void RequireRevision(long revision)
    {
        if (revision != status.OwnershipRevision) throw new BackendException("stale_ownership", "Re-observe ownership; stale commands cannot reclaim the session.");
    }
    public async Task CloseAsync(long expectedRevision, bool force, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRevision(expectedRevision); cancellationToken.ThrowIfCancellationRequested();
            if (status.State == SessionState.Closed) return;
            if (!force) throw new BackendException("force_required", "No application close/save-prompt transport exists. Explicit force is required to stop this display.");
            // Once termination begins, cancellation cannot truthfully mean not_started.
            Volatile.Write(ref status, status with { State = SessionState.Uncertain, Reason = "Owned display termination in progress." });
            bool stopped;
            try { stopped = displayStopped || await process.StopAsync(TimeSpan.FromMilliseconds(options.StopTimeoutMilliseconds)); }
            catch (Exception error) { throw new BackendException("cleanup_uncertain", "Cannot confirm owned display exit.", OperationOutcome.Unknown, error); }
            if (!stopped) throw new BackendException("cleanup_uncertain", "Cannot confirm owned display exit; files and capacity reservation retained.", OperationOutcome.Unknown);
            if (!displayStopped) { process.Dispose(); displayStopped = true; }
            try { runtime.RemoveOwnedFiles(directory); }
            catch (Exception error) { throw new BackendException("cleanup_uncertain", "Display exited but owned files require reconciliation.", OperationOutcome.Unknown, error); }
            Volatile.Write(ref status, status with { State = SessionState.Closed, Reason = "Owned display exited. Escaped external application processes are outside this foundation." });
            if (!released) { released = true; release(); }
        }
        finally { gate.Release(); }
    }
    // Disposal does not implicitly discard a Human's session. The host must make
    // an explicit close decision, matching protocol 2 client disconnect policy.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

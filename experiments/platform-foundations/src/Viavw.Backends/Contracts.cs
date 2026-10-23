// Copyright (c) 2026 The Coded Craftsman, LLC. MIT; see LICENSE.
namespace Viavw.Backends;

// Protocol-facing concepts only. This library is not a protocol 1/2 server and
// does not change Windows HWND/virtual-key wire semantics into portable claims.
public enum ControlOwner { Automation, Human }
public enum OperationOutcome { NotStarted, Completed, Unknown }
public enum SessionState { Starting, DisplayReady, Closed, Uncertain }
public sealed class BackendException(string code, string message,
    OperationOutcome outcome = OperationOutcome.NotStarted, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public OperationOutcome Outcome { get; } = outcome;
}
public sealed record BackendCapabilities(string Backend, bool CanStartIsolatedDisplay,
    bool CanLaunchApplication, bool CanObserve, bool CanInput, bool DurableReconnect,
    IReadOnlyList<string> Blockers);
public sealed record SessionStatus(string Id, SessionState State, ControlOwner Owner,
    long OwnershipRevision, string? Display, string? Reason);
public interface IIsolatedSession : IAsyncDisposable
{
    SessionStatus Status { get; }
    SessionStatus ChangeOwner(long expectedRevision, ControlOwner owner);
    Task CloseAsync(long expectedRevision, bool force, CancellationToken cancellationToken = default);
}
public interface IPlatformBackend
{
    BackendCapabilities Discover();
    Task<IIsolatedSession> CreateAsync(CancellationToken cancellationToken = default);
}

public sealed class MacOsBackend : IPlatformBackend
{
    public BackendCapabilities Discover() => new("macos", false, false, false, false, false,
        OperatingSystem.IsMacOS()
        ? ["isolated_session_transport_unproven", "remote_session_license_review_required",
           "same_user_screen_and_accessibility_do_not_establish_isolation"]
        : ["target_os_required", "isolated_session_transport_unproven", "remote_session_license_review_required"]);

    public Task<IIsolatedSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new BackendException("unsupported_isolation", "No verified macOS isolated-session transport is implemented. No console, AX or global-input fallback is permitted.");
    }
}

// Copyright (c) 2026 The Coded Craftsman, LLC. MIT; see LICENSE.
using System.Buffers.Binary;
using System.Text.Json;
using Viavw.Backends;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++; Console.WriteLine("PASS " + name);
}
void Reject(Action action, string code)
{
    try { action(); throw new Exception("Expected " + code); }
    catch (BackendException error) { Check(error.Code == code && error.Outcome == OperationOutcome.NotStarted, code); }
}
async Task<BackendException> RejectAsync(Func<Task> action, string code, OperationOutcome outcome)
{
    try { await action(); throw new Exception("Expected " + code); }
    catch (BackendException error) { Check(error.Code == code && error.Outcome == outcome, code + ":" + outcome); return error; }
}
var options = new LinuxOptions("/usr/bin/Xvnc", "/run/user/1000/viavw", "/run/user/1000/vnc-passwd");
LinuxLaunchPlan.Validate(options);
Reject(() => LinuxLaunchPlan.Validate(options with { PrivateRoot = "/" + new string('x', 57) }), "invalid_path");
foreach (var invalid in new[] { options with { DisplayNumber = 0 }, options with { Width = 4000 },
    options with { Height = 0 }, options with { StartupTimeoutMilliseconds = 99999 }, options with { StopTimeoutMilliseconds = -1 } })
    Reject(() => LinuxLaunchPlan.Validate(invalid), "invalid_limits");
foreach (string path in new[] { "relative", "/tmp/../etc", "/tmp//foo", "/tmp/foo\nbar", "C:\\Xvnc", "/tmp/./x" })
    Reject(() => LinuxLaunchPlan.Validate(options with { XvncExecutable = path }), "invalid_path");
var plan = LinuxLaunchPlan.Server(options, "/run/user/1000/viavw/session-test");
Check(plan.Arguments.Contains("-1") && plan.Arguments.Contains("-rfbunixpath"), "RFB TCP disabled; private Unix endpoint");
Check(plan.Arguments.Contains("-auth") && plan.Arguments.Contains("tcp") && !plan.Arguments.Contains("-ac"), "X auth required; X TCP disabled");
Check(plan.Arguments.Contains("-AcceptKeyEvents=0") && plan.Arguments.Contains("-AcceptPointerEvents=0"), "unimplemented input disabled");
Check(plan.Arguments.Contains("-AcceptCutText=0") && plan.Arguments.Contains("-SendCutText=0"), "clipboard bridges disabled");
Check(!plan.Environment.ContainsKey("DISPLAY") && !plan.Environment.ContainsKey("WAYLAND_DISPLAY") && !plan.Environment.ContainsKey("DBUS_SESSION_BUS_ADDRESS"), "server environment has no console bus/display");
var app = LinuxLaunchPlan.Application("/usr/bin/editor", ["a; touch /tmp/no", "$(uname)", "a b"], ":100", "/private/Xauthority", "/private/home");
Check(app.Arguments[0] == "a; touch /tmp/no" && app.Arguments[1] == "$(uname)", "application arguments remain literal tokens");
Check(app.Environment["DISPLAY"] == ":100" && app.Environment.Count == 6 && !app.Environment.ContainsKey("DBUS_SESSION_BUS_ADDRESS"), "application plan whitelist only");
Reject(() => LinuxLaunchPlan.Application("/bin/app", [], ":0", "/private/auth", "/private/home"), "invalid_display");
Reject(() => LinuxLaunchPlan.Application("/bin/app", Enumerable.Repeat("x", 129), ":100", "/private/auth", "/private/home"), "argument_budget");
Reject(() => LinuxLaunchPlan.Application("/bin/app", ["x\0y"], ":100", "/private/auth", "/private/home"), "argument_budget");
var cookie = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
byte[] setup = X11Authentication.Setup(cookie);
Check(setup.Length == 48 && setup[0] == 'l' && BinaryPrimitives.ReadUInt16LittleEndian(setup.AsSpan(2)) == 11, "X11 setup header");
Check(BinaryPrimitives.ReadUInt16LittleEndian(setup.AsSpan(6)) == 18 && setup.AsSpan(32).SequenceEqual(cookie), "X11 cookie offset/padding");
byte[] authority = X11Authentication.Authority("host", 100, cookie);
Check(BinaryPrimitives.ReadUInt16BigEndian(authority) == 256 && BinaryPrimitives.ReadUInt16BigEndian(authority.AsSpan(2)) == 4, "Xauthority local family/network byte order");
Check(authority.AsSpan(authority.Length - 16).SequenceEqual(cookie), "Xauthority cookie bytes");
Check(!X11Authentication.Accepted(new byte[8]), "X11 denied authentication is not ready");
byte[] accepted = [1, 0, 11, 0, 0, 0, 8, 0];
Check(X11Authentication.Accepted(accepted), "X11 accepted setup framing");
Reject(() => X11Authentication.Accepted(new byte[7]), "display_auth_failed");
Reject(() => X11Authentication.Accepted([2, 0, 11, 0, 0, 0, 8, 0]), "display_auth_failed");
Reject(() => X11Authentication.Accepted([1, 0, 11, 0, 0, 0, 255, 255]), "display_auth_failed");
Reject(() => X11Authentication.Accepted([1, 0, 12, 0, 0, 0, 8, 0]), "display_auth_failed");
Check(LinuxRuntime.MemoryBlocker(["MemAvailable: 0 kB"], 1280, 800) == "machine_capacity", "low memory rejected");
Check(LinuxRuntime.MemoryBlocker(["MemAvailable: 999999999 kB"], 1280, 800) is null, "memory headroom accepted");
foreach (string memory in new[] { "", "MemAvailable:", "MemAvailable: 1", "MemAvailable: 1 MB", "MemAvailable: 18446744073709551615 kB" })
    Check(LinuxRuntime.MemoryBlocker([memory], 1280, 800) == "memory_headroom_unknown", "unknown/malformed memory fails closed");
var native = new LinuxXvncBackend(options);
if (!OperatingSystem.IsLinux())
{
    Check(!native.Discover().CanStartIsolatedDisplay && native.Discover().Blockers.Contains("target_os_required"), "native Linux rejects Windows");
    await RejectAsync(async () => { await native.CreateAsync(); }, "preflight_failed", OperationOutcome.NotStarted);
}
var mac = new MacOsBackend();
Check(!mac.Discover().CanInput && !mac.Discover().CanStartIsolatedDisplay && mac.Discover().Blockers.Contains("remote_session_license_review_required"), "Mac honest isolation/license blockers");
await RejectAsync(async () => { await mac.CreateAsync(); }, "unsupported_isolation", OperationOutcome.NotStarted);

var runtime = new FakeRuntime(); var backend = new LinuxXvncBackend(options, runtime);
Check(backend.Discover().CanStartIsolatedDisplay && !backend.Discover().CanLaunchApplication && !backend.Discover().CanObserve && !backend.Discover().CanInput && !backend.Discover().DurableReconnect, "starter capability boundaries");
var session = await backend.CreateAsync();
Check(session.Status.State == SessionState.DisplayReady && session.Status.Owner == ControlOwner.Human && session.Status.OwnershipRevision == 0, "new session starts Human owned");
Check(runtime.Starts == 1 && runtime.Authentications == 1 && runtime.LastPlan!.Environment.Count == 2, "one start followed by private auth");
session.ChangeOwner(0, ControlOwner.Automation); session.ChangeOwner(1, ControlOwner.Human);
Reject(() => session.ChangeOwner(0, ControlOwner.Automation), "stale_ownership");
Check(session.ChangeOwner(2, ControlOwner.Human).OwnershipRevision == 2, "idempotent owner does not advance revision");
Reject(() => session.ChangeOwner(2, (ControlOwner)77), "invalid_owner");
await RejectAsync(() => session.CloseAsync(2, false), "force_required", OperationOutcome.NotStarted);
await session.DisposeAsync(); Check(!runtime.Processes[0].HasExited, "client disposal preserves session");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await session.CloseAsync(2, true, cancelled.Token); throw new Exception("Expected cancel"); }
    catch (OperationCanceledException) { Check(runtime.Processes[0].Stops == 0, "pre-close cancellation has no side effect"); }
}
await session.CloseAsync(2, true);
Check(session.Status.State == SessionState.Closed && runtime.Removes == 1 && runtime.Processes[0].Stops == 1, "force close only owned process/files");
await session.CloseAsync(2, true); Check(runtime.Processes[0].Stops == 1, "closed retry does not repeat kill");

runtime = new FakeRuntime { FailAuthentication = true }; backend = new(options, runtime);
await RejectAsync(async () => { await backend.CreateAsync(); }, "startup_failed", OperationOutcome.Completed);
Check(runtime.Starts == 1 && runtime.Processes.Single().Stops == 1 && runtime.Removes == 1, "failed auth cleanup; no startup replay");
using (var cancelled = new CancellationTokenSource())
{
    runtime = new FakeRuntime { DuringAuthentication = cancelled.Cancel }; backend = new(options, runtime);
    try { await backend.CreateAsync(cancelled.Token); throw new Exception("Expected startup cancellation"); }
    catch (OperationCanceledException) { Check(runtime.Starts == 1 && runtime.Processes.Single().Stops == 1 && runtime.Removes == 1, "startup cancellation confirms cleanup before propagating"); }
}
runtime = new FakeRuntime { StartUnknown = true }; backend = new(options, runtime);
await RejectAsync(async () => { await backend.CreateAsync(); }, "cleanup_uncertain", OperationOutcome.Unknown);
Check(runtime.Removes == 0, "uncertain spawn retains auth files");
runtime = new FakeRuntime { FailAuthentication = true, FailRemoval = true }; backend = new(options, runtime);
await RejectAsync(async () => { await backend.CreateAsync(); }, "cleanup_uncertain", OperationOutcome.Unknown);
Check(runtime.Processes.Single().Stops == 1 && runtime.Removes == 0, "startup file cleanup failure stays explicit");
runtime = new FakeRuntime { FailAuthentication = true, StopSucceeds = false }; backend = new(options, runtime);
for (int i = 0; i < 4; i++)
    await RejectAsync(async () => { await backend.CreateAsync(); }, "cleanup_uncertain", OperationOutcome.Unknown);
await RejectAsync(async () => { await backend.CreateAsync(); }, "session_budget", OperationOutcome.NotStarted);
Check(runtime.Starts == 4 && runtime.Removes == 0, "uncertain startup retains files and reservations");
runtime = new FakeRuntime { StopSucceeds = false }; backend = new(options, runtime); session = await backend.CreateAsync();
await RejectAsync(() => session.CloseAsync(0, true), "cleanup_uncertain", OperationOutcome.Unknown);
Check(session.Status.State == SessionState.Uncertain && runtime.Removes == 0, "uncertain stop blocks usable state");
Reject(() => session.ChangeOwner(0, ControlOwner.Automation), "session_unavailable");
runtime.Processes[0].StopSucceeds = true; await session.CloseAsync(0, true);
Check(session.Status.State == SessionState.Closed, "explicit close reconciliation succeeds");
runtime = new FakeRuntime { FailRemoval = true }; backend = new(options, runtime); session = await backend.CreateAsync();
await RejectAsync(() => session.CloseAsync(0, true), "cleanup_uncertain", OperationOutcome.Unknown);
runtime.FailRemoval = false; await session.CloseAsync(0, true);
Check(runtime.Processes[0].Stops == 1 && session.Status.State == SessionState.Closed, "file cleanup retry does not use disposed process");
runtime = new FakeRuntime(); backend = new(options, runtime); session = await backend.CreateAsync(); runtime.Processes[0].HasExited = true;
Check(session.Status.State == SessionState.Uncertain, "unexpected server exit invalidates readiness");
await session.CloseAsync(0, true);
runtime = new FakeRuntime(); backend = new(options, runtime);
var sessions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => backend.CreateAsync()));
await RejectAsync(async () => { await backend.CreateAsync(); }, "session_budget", OperationOutcome.NotStarted);
await Task.WhenAll(sessions.Select(s => s.CloseAsync(0, true)));
session = await backend.CreateAsync(); Check(runtime.Starts == 5, "capacity released after confirmed close"); await session.CloseAsync(0, true);
Console.WriteLine(JsonSerializer.Serialize(new { checks, result = "passed", host = Environment.OSVersion.ToString(),
    linuxRuntime = "UNRUN", macRuntime = "UNRUN", testType = "Windows compilation and deterministic lifecycle/validation fakes" }));

sealed class FakeRuntime : ILinuxRuntime
{
    public bool FailAuthentication, StopSucceeds = true, FailRemoval, StartUnknown;
    public int Starts, Authentications, Removes;
    public CommandPlan? LastPlan;
    public Action? DuringAuthentication;
    public List<FakeProcess> Processes = [];
    public IReadOnlyList<string> Preflight(LinuxOptions options) => [];
    public string Prepare(LinuxOptions options, string id, byte[] cookie) => options.PrivateRoot + "/session-" + id;
    public IOwnedDisplay Start(CommandPlan plan)
    {
        if (StartUnknown) throw new BackendException("spawn_uncertain", "injected uncertain spawn", OperationOutcome.Unknown);
        Starts++; LastPlan = plan; var process = new FakeProcess { StopSucceeds = StopSucceeds }; Processes.Add(process); return process;
    }
    public Task AuthenticateAsync(LinuxOptions options, byte[] cookie, CancellationToken cancellationToken)
    {
        Authentications++; DuringAuthentication?.Invoke(); cancellationToken.ThrowIfCancellationRequested();
        if (FailAuthentication) throw new IOException("injected auth failure"); return Task.CompletedTask;
    }
    public void RemoveOwnedFiles(string directory) { if (FailRemoval) throw new IOException("injected cleanup failure"); Removes++; }
}
sealed class FakeProcess : IOwnedDisplay
{
    public bool HasExited { get; set; }
    public bool StopSucceeds;
    public int Stops;
    public Task<bool> StopAsync(TimeSpan timeout) { Stops++; HasExited = StopSucceeds; return Task.FromResult(StopSucceeds); }
    public void Dispose() { }
}

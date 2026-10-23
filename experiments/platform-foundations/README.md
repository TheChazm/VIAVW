# VIAVW Linux/macOS backend foundations

Experimental starter, developed and independently verified on Windows using .NET SDK 10.0.302. This is a C# library and a dependency-free verification executable, not an installed VIAVW port or a new wire-protocol server. Integration adds only this experiment; no Windows project references it and no installed application or service changes.

## Implemented

- `IPlatformBackend` / `IIsolatedSession`: explicit capabilities, Human/Automation ownership revisions, display lifecycle, and known/uncertain outcomes. New displays start Human-owned. Stale revisions cannot reclaim ownership. `NotStarted` means no launch began; `Completed` on a startup failure means the attempted display was confirmed stopped; `Unknown` requires reconciliation. These enum names need explicit mapping to the existing protocol's error outcome strings at integration.
- Linux: local target detection and read-only preflight; caller-provisioned Xvnc executable, private root and VNC password file; geometry/time/socket-path limits; memory admission reserve; literal argument arrays and clean environment. No dependencies or credentials are created/downloaded/installed by preflight. Missing prerequisites fail closed.
- Linux: a real Xvnc child process, generated per-session Xauthority cookie, negative and positive X11 cookie probes, bounded startup and force termination of that exact child. No process-name/PID scans, foreign display adoption, recursive deletion or X lock removal. Only the task-created `Xauthority`/`rfb` paths and now-empty session directory are removed after confirmed exit. Unknown cleanup retains capacity/files and reports the session ID/path. Startup is never retried automatically.
- Linux: bounded application command/environment planning only. It strips ambient DISPLAY, Wayland, DBus and desktop activation variables by constructing a whitelist. The plan is intentionally not executable through the session API until an isolated-instance application profile and observation/input path are qualified.
- macOS: target-aware discovery with explicit session transport and topology/license review blockers. Create fails before native activity. No AX, console, screen sharing, VM or global-input fallback.

`DisplayReady` means X11 cookie authentication succeeded while the owned server remained alive; it does not mean an application is ready. `CanLaunchApplication`, `CanObserve`, `CanInput` and `DurableReconnect` are false. The library makes no universal compatibility claim. There is no input method, and RFB keyboard/pointer acceptance and clipboard bridging are disabled. No foreground/keyboard/mouse APIs are invoked.

`CanStartIsolatedDisplay` reports that local preflight permits a startup attempt, not that the native implementation has been qualified. Ownership revisions detect stale requests; they do not authenticate callers or authorize a Human-to-Automation transition. A future host must enforce that authorization separately and must not expose unrestricted ownership changes to an agent.

## Build and verify without network or packages

From this directory, using existing .NET 10 tools:

```powershell
dotnet build tests/Viavw.Backends.Verification/Viavw.Backends.Verification.csproj --configuration Release -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet tests/Viavw.Backends.Verification/bin/Release/net10.0/Viavw.Backends.Verification.dll
```

NuGet sources are cleared and there are no package references. First build only generates local SDK restore metadata. Tests use deterministic lifecycle fakes; the production Linux runtime refuses to run on Windows. The checked-in test source includes argument/environment validation, X11 wire framing, memory admission parsing, stale ownership, capacity, cancellation before termination, uncertain spawn/auth/stop/cleanup, explicit reconciliation, and unexpected server exit. The independent integration result is recorded in [the session summary](docs/sessions/2026-10-23-frozen-foundations-review.md).

## Explicit target proof gaps and lifetime limits

Linux Xvnc startup, installed-version flag support, Xauthority interoperability, negative-cookie rejection, socket permissions and cleanup have NOT been executed on Linux. macOS runtime tests have NOT been run. A target integration test must observe the human console/display/pointer before and after, verify no host DBus/Wayland connection, prove an ordinary app's separate instance, and verify saved results before enabling application/input capabilities.

The private root must already exist with owner-only permissions, have no symlink ancestors, and fit 56 UTF-8 bytes; password-file group/other permissions must be absent. The caller supplies a trusted Xvnc installation and an already-provisioned VNC password file. The backend does not alter system permissions, accounts, authentication settings or network configuration. The server command disables X TCP and RFB TCP, uses a private RFB Unix socket, and does not connect a viewer. Direct child stdout/stderr are drained without retained logs; detailed native diagnostics are a remaining integration task.

Each backend instance selects one explicit display (100..999) and never removes a stale/busy display lock. Separate configured instances are required for distinct displays. Four outstanding reservations per instance bound retained uncertainty; this is not a global machine/session limit. Geometry bounds and a 512 MiB admission reserve plus a framebuffer estimate are not hard CPU/RAM limits. No cgroup is configured. Paths are checked against symlinks; hostile concurrent same-user filesystem replacement is outside this trusted-caller starter's guarantee.

The owned server is killed only on explicit force close or failed-start cleanup. Client disposal leaves it alive. The server is a direct child, with no durable keeper/reconnect/crash-recovery implementation: controller crash can leave an orphan requiring manual reconciliation. Closing the display disconnects X clients but makes no claim to terminate independently launched, detached or brokered applications. Graceful application close and save-prompt preservation are unavailable, so unforced close fails. Uncertain startups retain no in-memory public session handle; their reported ID/path must be reconciled by an operator rather than blindly creating again. Do not wire this into the Windows durable flow or advertise protocol-2 recovery.

## Coordinated integration

This experiment was integrated from the frozen new-files-only `integration.patch`, independently of subsequent platform work. The local `Directory.Build.props` establishes `net10.0` and does not inherit the root Windows Forms target. No Windows project references, enums or existing protocol code change. Review and map `ControlOwner` and outcome names at the future host boundary; retain opaque platform session/display identifiers rather than treating them as HWNDs or translating Windows virtual keys speculatively.

Read-only baseline references were obtained via Git archive from `1a47fcc8bc0c696c25f0edef345fcace02140976`: `LICENSE`, `docs/AGENT-PROTOCOL.md`, `docs/DURABLE-LAYERS-20261023.md`, `src/LayerHost/Protocol.cs`, `src/LayerHost/LayerCapacity.cs`. No in-flight working-tree changes were copied. No applicable AGENTS.md or .agents directory was found at the repository/root source locations inspected; no raw session history was read.

## Official implementation references

- TigerVNC Xvnc options and separate virtual display: https://tigervnc.org/doc/Xvnc.html
- Upstream recommended session manager (not invoked here): https://tigervnc.org/doc/vncsession.html
- X11 connection setup specification: https://www.x.org/releases/X11R7.7/doc/xproto/x11protocol.html
- Apple different-user versus same-user remote control: https://support.apple.com/guide/remote-desktop/choose-how-to-control-and-observe-apd4f46319e/mac
- Parent research identifies macOS 27 SLA sections 2I(iii)/2B(iii) for exact topology review: https://www.apple.com/legal/sla/docs/macOS27.pdf . This starter neither resolves loopback topology licensing nor treats it as a universal prohibition.

MIT applies to our source; see LICENSE. External tools retain their own terms; see THIRD_PARTY_NOTICES.md.

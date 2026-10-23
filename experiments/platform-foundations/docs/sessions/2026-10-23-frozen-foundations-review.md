# Frozen platform foundations integration — 2026-10-23

Baseline: `24d0a772607a4554be25c888c645037175319a34`. Reviewed only the frozen first platform patch, SHA-256 `9B1770C352F2E2D5D7BE87A29333DF6DA1E35ACCE4DCDDE9DCD2C14C191E044F`; subsequent worker changes are excluded.

The patch contains 12 new source/config/license/documentation files under `experiments/platform-foundations`. Review covered the complete additive diff, literal launch arguments and environment whitelist, target preflight, X11 authentication framing, exact-child termination, retained uncertainty/capacity, ownership revisions, scoped nonrecursive cleanup, project references and dependency notices. No blocking defect was found for experimental inclusion. Integration clarifies that ownership revisions are not caller authorization and preflight capability is not native qualification. Source and verification code remain identical to the frozen patch. The company MIT notice is retained; no external software is bundled or relicensed.

Independent validation on Windows, .NET SDK 10.0.302, from a task-owned extracted copy with empty package cache and cleared feeds:

```powershell
dotnet build tests/Viavw.Backends.Verification/Viavw.Backends.Verification.csproj --configuration Release -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet tests/Viavw.Backends.Verification/bin/Release/net10.0/Viavw.Backends.Verification.dll
```

Both commands exited 0. Build: zero warnings and errors; deterministic verification: 76 checks passed. The checks exercise fake-runtime lifecycle/error cases and validation/wire framing; they do not launch Xvnc, an application, a native session or an input path. NuGet cache remained empty. No live VIAVW service contact or GUI tests occurred.

Linux native startup/flags, Xauthority interoperability, socket permissions, human-console noninterference and cleanup remain unrun. macOS create remains blocked; no native macOS tests ran. Application launch is a plan only; launch/capture/input/durable-reconnect capabilities remain false. Controller crash/orphan reconciliation, graceful application close and protocol mapping remain explicit gaps. Physical human acceptance is pending independently of these checks.

This is an additive source experiment, with no Windows protocol/runtime coupling. Installed/selected 0.1.2 `tool-20261023i` and CharlieAI are unchanged. No installation, dependency download, push, publication, remote change or release tag is part of this integration. Charlie retains control of pushing.

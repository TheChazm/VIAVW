# Excel reconciliation regression — 2026-10-23

Baseline: 7f78df56d86d65ea653fe43d20cc4b6245d71e09. Objective: strengthen recovery evidence for the recorded Excel Save As unknown-input transition without interfering with ongoing integration.

Read current compatibility/installed Office records and production ToolService/Transport. Read-only process metadata showed several preexisting VIAVW clients. Initial restricted CIM read was unavailable; a scoped read with elevated execution permission succeeded. No existing client, app, service, document or runtime record was contacted or modified. A conflict-free live GUI lane could not be established, so this slice used the authorized fake-peer fallback.

Added a dependency-free console regression linking unchanged production ToolService, Transport and DetachedProcess source; added operation-level evidence and documented commands. No product defect was established, so there is no speculative production fix. The platform follow-on delta is excluded.

Actual commands from the task-owned source copy, with cleared package feeds and an empty scoped NuGet cache:

```powershell
dotnet build tests/ViavwTool.ReconciliationTests/ViavwTool.ReconciliationTests.csproj -c Release -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet tests/ViavwTool.ReconciliationTests/bin/Release/net10.0/ViavwTool.ReconciliationTests.dll
```

Both exited 0; build zero warnings/errors; 30 assertions passed, including persistence before unknown dispatch, reconnect rejection, stale/Human/suspended-context barriers, no replay, partial-segment containment and uncertain owner reconciliation. Test fixture cleanup completed. No GUI applications, native sessions, production services or captures were used. See [the validation matrix](../validation/2026-10-23-excel-reconciliation.md).

Remaining acceptance: intended XLSX Save As after the real uncertain F12 transition, exact saved/reopened workbook data and formulas, fresh visual capture, and physical human noninterference. Installed/selected 0.1.2 tool-20261023i is unchanged. No CharlieAI, dependency installation, push, tag, remote, release-selection or security change. Charlie handles pushing.

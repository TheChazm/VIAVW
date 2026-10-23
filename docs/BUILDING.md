# Build from a clean checkout

Builds below do not install VIAVW, create applications or select a release. The
source license is still pending the owner's choice; runtime license notices do
not license this repository. Charlie alone handles pushing and publication.

## Build prerequisites

Use Windows x64, PowerShell 7 and a .NET 10 SDK with Windows Desktop and x64
app-host targeting packs. The verified environment uses SDK 10.0.302 and reference
packs 10.0.10. No third-party NuGet package is required for the normal six-project
build. This does not mean .NET framework packs can be absent. No script installs
an SDK or changes Windows settings.

Run from your clone directory, not a fixed workstation path:

```powershell
.\scripts\build.ps1 -Offline
```

This builds LayerHost, LayerProbe, WpfProbe, WpfRenderHook, ViavwTool and
ViavwDesktop. `-Offline` clears package feeds/fallback folders with the tracked
NuGet configuration. A missing prerequisite fails instead of being downloaded.
Without `-Offline`, ordinary configured .NET restore behavior applies.

## Local tracked-source package

Self-contained publishing additionally needs matching pre-existing official
`Microsoft.NETCore.App.Runtime.win-x64` and
`Microsoft.WindowsDesktop.App.Runtime.win-x64` NuGet runtime packs, including their
licenses/notices. SDK 10.0.302 offline publishing also requested
`Microsoft.AspNetCore.App.Runtime.win-x64`; provide that existing cache pack too.
The verified runtime-pack version is 10.0.10; installed SDK app-host packs
must also match. Supply an already populated package cache. This slice does not
provide or install dependencies. No API key or signing certificate is required.

```powershell
.\scripts\build-standalone-tool.ps1 -SourceMode Tracked `
  -Release tool-local-0-1-2 -Version 0.1.2 -Offline `
  -PackageCache 'D:\ExistingNuGetCache' -OutputRoot "$env:TEMP\viavw-local-packages"
```

The destination and ZIP must be new. Tracked mode publishes the host, MCP/CLI tool
and manager, checks shared runtime consistency and unchanged source hashes, and
includes runtime notices plus a source-hash manifest. It creates a full package
and ZIP without reading any `*-current.json`, installing files or updating
selection/connection files. `package.json` labels it build-only: packaging does
not establish application compatibility, Human acceptance or bit-identical
reproduction of an earlier release. Keep packages out of the installation.

The default `-SourceMode Pinned` preserves the existing research packaging route.
It intentionally requires the locally preserved durable selection/snapshot.
Those ignored inputs are not part of a clean checkout; use Tracked for a new
source build. Neither build mode is the separate release-selection command.

The historical MSI builder remains a separate stage: it requires a verified
portable selection/archive, MSVC x64 tools discoverable through `vswhere`, Windows
SDK headers/libraries, WiX plus `WixToolset.UI.wixext` at its documented local
tool location (`artifacts/installer-tools/wix.exe`), and the same runtime packs.
This clean-package validation does not build, sign, install or test an MSI and
does not fabricate those prerequisites or alter a production selection to test
them. A selection-independent MSI entry point remains follow-up work.

## Installed-application test prerequisites and overrides

Application suites are opt-in GUI work. Building or validating path policy does
not run them. Do not run them concurrently with a person's production layers or
another test batch without coordination. Use disposable documents/profiles.

| Variable | Scope/default |
| --- | --- |
| `VIAVW_NOTEPAD_EXE` | Absolute installed `notepad++.exe`; host disposable-fixture preparation otherwise uses the Program Files location. |
| `VIAVW_7ZIP_EXE` | Absolute installed `7zFM.exe` for standalone CLI/SDK tests. |
| `VIAVW_WORD_EXE` | Absolute installed `WINWORD.EXE` for Office tests. |
| `VIAVW_EXCEL_EXE` | Absolute installed `EXCEL.EXE` for Office tests. |
| `VIAVW_TEST_SDK` | Optional existing dependency directory for SDK/Office Python tests; otherwise use an existing environment/venv or the preserved local test dependency folder. |

App paths default below the process's Program Files directory. Invalid explicit
overrides fail without silently using another installed app. Generic tool users
still supply an executable directly to `viavw_create`. Older research scripts
retain their own documented options and are outside this portability slice.

Python 3 with the tested top-level dependencies in
`scripts/requirements-test.txt` is needed for SDK/Office tests. No dependency
installation occurs in the test scripts. Transitive dependency locking is not
claimed. Word/Excel require separately installed Office; these apps are not
redistributed. Native/GPU research needs additional documented MSVC/SDK/framework
prerequisites and is not part of the normal build validation.

The offline path-policy tests launch no application:

```powershell
python -B .\scripts\test-app-test-paths.py
```

Read the [sanitized validation summary](validation/2026-10-23-source-build.md).
Generated raw logs, captures, runtime state and local package selections remain
ignored. Their historical paths are evidence references for the original local
run, not downloadable files supplied by this repository.

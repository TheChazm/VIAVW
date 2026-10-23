# Build from a clean checkout

Builds below do not install VIAVW, create applications or select a release. The
source is licensed under [MIT](../LICENSE), copyright 2026 The Coded Craftsman,
LLC. Bundled runtimes and other third-party components retain their own notices. Charlie alone handles pushing and publication.

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

## MSI generation from explicit inputs

Python 3, an existing WiX executable and matching WiX UI extension DLL, MSVC x64
tools, and Windows SDK headers/libraries are required. Generation was verified
with MSVC 14.51.36231, Windows SDK 10.0.26100.0 and UI extension 5.0.2. Nothing is
installed or downloaded by this explicit-input route. Future ZIPs include the project LICENSE in their hash manifest; MSI payloads
retain that notice alongside unchanged third-party notices. A different existing
project LICENSE is rejected rather than overwritten. The native stable launcher
is built from tracked C++ source; the manifest-verified manager is reused from
the portable package, without a .NET republish or private selection file.

Use your actual existing tool paths and the expected SHA-256 from your trusted
portable build receipt. Example paths below are placeholders:

```powershell
python .\scripts\build-windows-installer.py installer-local `
  --archive 'D:\Build\VIAVW-Tool-0.1.2-tool-local-0-1-2-win-x64.zip' `
  --archive-sha256 '<expected archive SHA-256>' --version 0.1.2 `
  --output-root 'D:\Build\MSI' --wix 'D:\Tools\WiX\wix.exe' `
  --wix-extension 'D:\Tools\WiX\WixToolset.UI.wixext.dll' `
  --msvc-root 'D:\Tools\MSVC\14.51.36231' `
  --windows-sdk-root 'D:\WindowsKits\10' --windows-sdk-version 10.0.26100.0 --test
```

Alternatively, use `--package <untouched directory>` and
`--package-manifest-sha256 <expected package.json SHA-256>` instead of the archive
arguments. A directory must contain exactly its manifest files and package.json;
live state or extra files are rejected. Use the ZIP or a fresh extraction if
diagnostics already created runtime state in a package directory.

Input validation checks complete file hashes, safe unique Windows paths, required
host/tool/manager/runtime/notice files, Windows x64 format and MSI numeric-version
limits. `--version` asserts agreement with the package; it never relabels a
different product version. Input hashes prove consistency with the provided pin,
not author authenticity or application qualification. Outputs must use a new
build directory; installation/source directories are rejected as output roots.

`--msvc-root` names an existing MSVC version directory. Without it, the existing
standard Visual Studio locator is used; override with `--vswhere`. WiX/extension
paths must exist in explicit mode. Missing prerequisites fail; no extension/tool
installation is attempted. Full commands and local hashes are recorded in the
generated `installer.json`, alongside `generationOnly: true`.

With no explicit archive/package, the legacy local-selection mode and
`--desktop-from` option remain available. They are not needed for this clean
source-to-ZIP-to-MSI route. No builder changes release selection or installs MSI.
The `--test` switch separates product/registry/menu identity; it does not itself
provide an isolated install location. Installation, repair and removal
qualification require a separately authorized disposable environment. This slice
generated an MSI only and did not sign, install or qualify it.

See the [generation-only validation summary](validation/2026-10-23-msi-generation.md).

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

## Owner-transport regression without GUI

This dependency-free console test links production Transport.cs and runs only
in-process fake named pipes. It does not start the installed tool, LayerHost,
applications or private desktops, and does not send physical input.

```powershell
dotnet build .\tests\ViavwTool.TransportTests\ViavwTool.TransportTests.csproj -c Release -p:RestoreConfigFile="$PWD/scripts/NuGet.offline.config" -p:NuGetAudit=false
dotnet .\tests\ViavwTool.TransportTests\bin\Release\net10.0\ViavwTool.TransportTests.dll
```

See the [fail-closed transport evidence](validation/2026-10-23-owner-transport.md).
These tests validate transport failure semantics, not GUI/application compatibility
or physical Human acceptance.

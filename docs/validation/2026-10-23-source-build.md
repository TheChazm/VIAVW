# Public validation summary: source build, 23 October 2026

This is a sanitized engineering summary. Raw local artifacts, captured images,
application data and process identifiers are excluded from the repository.

Before this slice, all 150 tracked LayerHost source/project files exactly matched
the selected 0.1.2 package's preserved build-source directory; shared build
properties and tool source hashes matched too. Thus the tracked-source route
does not start from the older pre-Office host. The only host-source change in this
slice adds an explicit Notepad++ test-fixture executable-path override; input,
capture, ownership and recovery implementations are unchanged.

The initial Git baseline was exported with `git archive` into an isolated
directory containing 330 committed files and no ignored artifacts/dependencies.
The original four-project documented build passed using SDK 10.0.302, installed
10.0.10 reference packs, an empty package cache and cleared external package
feeds: four successful builds, zero warnings/errors, no downloaded package files.
A restricted first attempt failed on SDK registry access before compilation; the
approved retry passed. This was an environment permission limit, not a source
compilation failure.

## Current tracked-source validation

A fresh copy of the intended tracked snapshot (337 files before builds) contained
no preserved artifacts, dependencies or selections. Commands executed there:

```powershell
.\scripts\build.ps1 -Offline
python -B .\scripts\test-app-test-paths.py
.\scripts\build-standalone-tool.ps1 -SourceMode Tracked -Release tool-clean-source-012b -Version 0.1.2 -Offline -PackageCache '<isolated existing-pack cache>' -OutputRoot '<fresh task-owned output>'
# New package only; no application launch:
viavw.exe doctor
viavw.exe tools
```

All six projects built with zero warnings/errors in approximately 16.08 seconds.
Five offline path-policy tests passed. Tracked-source host/tool/manager publishing
and ZIP creation passed in approximately 19.64 seconds. All 160 source hashes,
675 packaged file hashes, manager inclusion, archive contents and the manifest
were verified; `doctor` passed and all 13 tool schemas were discoverable. The ZIP
contains 676 files including the manifest, and is 114,142,193 bytes. Its SHA-256:
`61FD1D46049B089BF7BD56F3F573ECB7281FA68241B8AA595530FC384C7CA9BC`.

The first offline package attempt failed at restore because the isolated cache
lacked `Microsoft.AspNetCore.App.Runtime.win-x64` 10.0.10. That official package
already existed in the local cache; copying it alongside the existing .NET and
Windows Desktop runtime packs allowed the package-only retry to pass. No package
download/installation or source-code workaround was needed. The initial offline
path tests also encountered a restricted system-temp location; they passed with
a task-owned temp directory. These environment failures are retained in the
local logs rather than presented as successful first attempts.

The legacy pinned-mode body was checked against the baseline and remains
unchanged apart from the new explicit mode dispatch. Pinned publishing was not
rerun. No GUI application tests, manager UI acceptance or MSI build/install tests
were performed. The new package is build-only and was never selected/installed.
The separately retained accepted package is still 0.1.2 `tool-20261023i`.

Separately, an earlier bounded test of the accepted 0.1.2 package ran 180 seconds,
30 exact edit/save cycles and 180 input actions in one disposable Notepad++ layer.
Foreground event subscription plus 1,653 samples recorded no test-owned
foreground observation; no sampled desktop/confinement changes or event drops
occurred. Maximum sample gap was approximately 113 ms. Scoped cleanup left no
surviving held test-process identities. This reports bounded observations, not
proof of zero transient interference. The person was using a laptop, so physical
Human acceptance on the tested machine remains pending. This earlier runtime
evidence does not automatically validate a newly compiled package.

CharlieAI, installed VIAVW and release-selection files are unchanged by this
slice. No source license, remote, tag, publication or push was added. MSI installation and cross-platform runtime validation remain unperformed.
A later [generation-only MSI follow-up](2026-10-23-msi-generation.md) validates
explicit inputs and build output without claiming installed qualification.

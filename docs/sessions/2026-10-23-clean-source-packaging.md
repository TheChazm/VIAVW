# 2026-10-23: clean-source packaging

Objective: make the tracked source build and package without depending silently
on ignored release selections/snapshots. Installed VIAVW and CharlieAI remain
untouched; Charlie alone handles pushing/publication. No license, remote or
release tag is chosen, and no production release selection is changed.

Provenance before editing: all 150 tracked host source/project files exactly
matched the selected package's preserved build-source directory. Shared build
properties and tool-source hashes matched too. The host's only implementation
edit here is an explicit, validated Notepad++ test-fixture path override; default
behavior is preserved. Input/capture/ownership/recovery source is unchanged.

Changes: normal build now includes Tool and Desktop; SourceMode Tracked builds
the host/tool/manager, source-hash manifest, runtime notices and full ZIP into a
new output without reading or updating selections. The original pinned body is
preserved. A tracked offline NuGet configuration clears external sources. Public
tests accept explicit app/dependency paths, with five offline path-policy checks.
Build prerequisites and sanitized public validation evidence are documented.

Actual validation: fresh exported baseline plus intended changes, no ignored
inputs; six projects built offline with zero warnings/errors (16.08 seconds),
five path-policy tests passed, package publishing/ZIP passed (19.64 seconds),
160 source hashes and 675 package-file hashes verified, manager included,
doctor passed, 13 tools discoverable, ZIP contents verified (676 entries).
The initial package attempt lacked the existing ASP.NET Core runtime pack in the
isolated cache; it was copied from the existing local cache and the package-only
retry passed with feeds still blocked. No dependencies were installed/downloaded.
The initial path-test sandbox temp restriction was resolved by using task-owned
temp. No original logs were concealed or counted as successful attempts.

See docs/BUILDING.md and docs/validation/2026-10-23-source-build.md for commands,
versions and limits. Raw local logs/captures/application state are not committed.
These results establish source build/package mechanics, not fresh package GUI
compatibility or physical Human acceptance. No further GUI runs occurred while
the user began CharlieAI integration. MSI tooling, a selection-independent MSI
entry point, source license and physical acceptance remain separate work.

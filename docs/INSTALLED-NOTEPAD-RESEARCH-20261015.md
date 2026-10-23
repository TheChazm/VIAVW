# Installed Notepad++ compatibility — 15 October 2026

Follow-up: the [ordinary document workflow](NOTEPAD-WORKFLOW-20261015.md) extends these initial mechanism tests with actual file opening, selection edits, Save As, file switching, closing and reopening. The fitted-preview option resolves the cropping limitation recorded below for the earlier demo.

Everyday installed applications now form a direct compatibility target. Small fixtures remain useful for isolating a mechanism and checking regressions; their success is not evidence that an everyday application works.

## Application and isolation

Tested **the existing `C:\Program Files\Notepad++\notepad++.exe`, version 8.8.3**, not a rebuilt editor or downloaded substitute. SHA-256: `3C729C08DA054834A6F7C6287539670D5A4B64A37017B03536D5E2EA57390FE7`.

The executable stayed unchanged. No DLL injection, plugin, Scintilla command API, document automation API or clipboard writes were used. Each instance had a private desktop, owned process tree, disposable UTF-8 document and separate settings under project artifacts. Plugins were disabled. Notepad++ documents its separate-instance, no-session, no-plugin and settings-directory options in its [command-line manual](https://raw.githubusercontent.com/notepad-plus-plus/npp-usermanual/master/content/docs/command-prompt.md). This is application configuration, not a sandbox against arbitrary filesystem access.

Microsoft Word was found installed, but **was not launched or tested** in this experiment.

## Results

| Behavior | Result |
| --- | --- |
| Select all, Unicode typing, append, Ctrl+S | Exact saved contents verified |
| Ctrl+Z / Ctrl+Y | Undo/redo verified through subsequently saved contents |
| Human/automation ownership | Wrong sender rejected; same process/document preserved |
| Ctrl+O / Escape | Ordinary file picker captured and cancelled through direct layer APIs |
| Worker recovery | Original application/root retained; further edits saved without replay |
| Actual viewer | Click, replacement typing, save, automation toolbar and handoff passed |
| Preview hidden | Editing/saving continued without advancing preview version |
| Two viewers | Independent processes/desktops/documents; startup barrier established overlap |
| Default DirectWrite document pixels | **Blank** in all four tested native capture paths |
| Built-in GDI rendering option | Document text visible in root captures and the viewer |

Default-renderer toolbar pixels alone initially looked encouraging. Screenshot inspection exposed the empty editor; separate document-region pixel checks now distinguish it. `PrintWindow` flags 1 and 3, redraw followed by flags 3, and `WM_PRINTCLIENT` all missed the default document text. With GDI, the first three captured text; `WM_PRINTCLIENT` still missed it. This does not establish a universal DirectWrite limitation.

GDI is selected only in disposable configuration (`MISC/writeTechnologyEngine=0`), supported by the application's [versioned configuration parser](https://github.com/notepad-plus-plus/notepad-plus-plus/blob/v8.8.3/PowerEditor/src/Parameters.cpp). It is a compatibility setting, not transparent capture of the default renderer.

The viewer also exposed a real shortcut issue: synchronous forwarding bypassed Notepad++'s accelerator processing. New opt-in `--posted-shortcuts` / `-PostedShortcuts` uses the existing private queue-state path with posted messages. Existing viewer defaults remain available. It retains serialized commands and ownership checks, with bounded empirical modifier timing rather than an application acknowledgement.

An initial harness error omitted `<History>` from its fresh configuration. This Notepad++ version then crashed during startup. Adding the empty element resolved it; versioned source contains an unchecked History-node removal. Earlier failures and Windows exception details are retained. A later viewer test incorrectly sampled preview version before a queued refresh finished; sampling after the Hide completion corrected the assertion.

## Verification and measurements

Primary evidence: `artifacts/installed-notepad-20261015-121108`. **58 functional assertions** passed: 21 default-renderer, 22 GDI, and 5 in each of three actual-viewer runs. Default-renderer functional success explicitly does **not** mean successful document capture. All five source workflows closed gracefully and cleanup passed. Read-only sampled physical desktop, pointer, focus and confinement checks passed; checked personal config/session files and executable hashes were unchanged. Physical human acceptance remains pending.

Direct workflows took about 2.8 seconds of activity. Source-only sampled machine CPU was about 1.19%; replaced-worker, replacement-worker and supervisor costs are excluded, so this is not total wrapper consumption. Source private memory at the endpoint was about 76.8 MiB with default rendering and 25.0 MiB with GDI. Three ordinary GDI captures took 20.1–23.2 ms. These are small local samples, not general performance guarantees.

The existing qIV five-case window suite passed again, along with the Qt software shortcut viewer and four-framework queue-input regression. The preserved pre-change baseline contains 228 files with hashes. Final audit: `artifacts/m20-final-audit.json`. Browser-profile deletion was not attempted; no new browser profiles were required.

## Demonstration

From the project directory:

```powershell
.\scripts\demo-notepad.ps1 -SkipBuild
```

Take **Human control**, click the first editor line, type, then press Ctrl+S. Return to automation and confirm picture typing is rejected. Take Human control again to continue the same document. Use **Hide preview** and **Show preview** to inspect on demand. Close after saving. The preview currently crops oversized ordinary windows; full scrolling, drag behavior and modal return through this generic viewer need further testing.

`-DefaultRendering` reproduces the default renderer's missing document pixels. `scripts/test-installed-notepad.ps1 -SkipBuild` repeats automated tests with fresh disposable data. No visible demo was left running.

Next: investigate default Notepad++ document capture using ordinary child-window presentation/capture, then test Word separately with disposable local documents and explicit instance/template/add-in controls. Fixture work should support a concrete failure found in these real applications.

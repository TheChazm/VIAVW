# Ordinary Notepad++ document workflow — 15 October 2026

The primary compatibility test now performs a useful editing task in the workstation's installed Notepad++ 8.8.3 through the actual VIAVW viewer. It starts with an empty editor and uses ordinary keyboard and picture input. The [testing standard](COMPATIBILITY-TESTING.md) makes this approach the basis for future application investigations, including Visual Studio with disposable projects.

## Workflow and results

The final run completed the following sequence in one application process:

1. Open `notes.txt` through Ctrl+O and the ordinary file picker.
2. Select its status line using navigation and Shift+End; replace that line and save.
3. Undo and redo the replacement, saving and checking each intermediate state.
4. Append a Unicode note using ordinary typing and Enter; save with verified CRLF endings.
5. Use Notepad++'s Ctrl+Alt+S Save As command to create `notes-reviewed.txt`.
6. Edit the copy and verify the original notes remain unchanged.
7. Open `checklist.txt`, revise one checklist line and save it.
8. Switch back with Ctrl+Tab, edit/save the copy, and verify the checklist remains independent.
9. Click the checklist's actual tab to switch back by pointer.
10. Close both documents, reopen the saved copy through the file picker, verify the editor's loaded text against the expected saved contents, then edit/save again.
11. Return ownership to automation, verify rejected picture typing, then resume the same document under Human ownership.

All final files matched their separate expected contents. The final workflow has **31 checks and 24 viewer screenshots**. Main-window and application identities survived the dialogs and switches. Screenshots were inspected, including selected text and the reopened document. Saved bytes and ordinary control text provide additional evidence; no application-model API manufactured the result.

## Problems discovered and changes

The original viewer cropped the Open dialog, leaving its filename field outside the interactive picture. New opt-in `--fit-preview` / `-FitPreview` reuses the existing scaled-coordinate mapping. The Notepad++ demo now enables it. Both the main document and full file dialogs fit in the standard viewer.

Ctrl+Tab initially opened Notepad++'s document switcher but left it open. Key releases still targeted the original editor after focus moved to the switcher. The optional posted-shortcut path now resolves focus for each release, validates it against the original GUI thread, owned job and private desktop, and rejects an invalid destination. This resolved the tested switcher workflow. Keyboard state restoration and serialized ownership remain in place; this is not physical-device emulation or support for every stateful shortcut.

The workflow also corrected several test assumptions. This application uses Ctrl+Alt+S for Save As; Ctrl+Shift+S saves all files. Open and Save As place the filename field at different heights, so pointer coordinates now come from observed edit-control bounds. Text-entry calls containing a carriage return did not reproduce pressing Enter through this viewer; the scenario now issues the Enter key. Selection replacement produced two undo groups on the tested path: one undo left the first replacement character, and a second restored the original selection. Both redo states were verified. Comparison with physical typing remains pending.

Earlier failed runs are retained under `artifacts/notepad-workflow-*`; they are not included as successful workflows.

## Scope and verification

The workflow uses **Notepad++'s built-in GDI rendering setting**, disabled plugins and project-local settings/documents. The installed executable and checked personal configuration/session files stayed unchanged. The existing Notepad++ process was preserved. Default DirectWrite document pixels remain blank in the tested native capture paths; the broader workflow does not resolve that limitation. Word and Visual Studio were not launched in this experiment. Physical-human acceptance is pending.

Primary evidence: `artifacts/installed-notepad-20261015-123912/ordinary-document-workflow/notepad-workflow.json`. The full five-case installed-application suite also reran the default/GDI diagnostics, single viewer and two concurrent viewers. It passed, with the default-renderer pixel limitation recorded separately. The workflow duration includes deliberate pacing, screenshots and inspection, so it is not an input-latency benchmark.

Regression and cleanup evidence is indexed by `artifacts/m21-verification-index.json` and `artifacts/m21-final-audit.json`. A hashed snapshot of the prior working implementation was preserved at `artifacts/baselines/verified-installed-notepad-20261015-122833`. No Git operations or browser-profile deletions were performed.

## Repeat or demonstrate

```powershell
# Primary workflow first, followed by supporting installed-application tests:
.\scripts\test-installed-notepad.ps1 -SkipBuild

# Visible viewer with GDI, posted shortcuts and a fitted preview:
.\scripts\demo-notepad.ps1 -SkipBuild
```

For a manual demonstration, take Human control, use Ctrl+O to open disposable files, select/edit/save text, use Ctrl+Alt+S to save a new copy, and switch files with Ctrl+Tab or their tabs. Close and reopen a saved file. Return to automation and confirm picture typing is rejected. Use fresh documents under project artifacts; no visible demonstration is left running automatically.

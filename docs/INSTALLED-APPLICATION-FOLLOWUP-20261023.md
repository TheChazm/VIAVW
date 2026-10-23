# Installed application follow-up — 23 October 2026

VIAVW **0.1.2**, release **tool-20261023i**, is installed. CharlieAI is unchanged; the user is still preparing its connection.

## Connection

Stable Windows executable: `%LOCALAPPDATA%\Programs\VIAVW\viavw.exe`

Arguments: `mcp`. Connection file: `%LOCALAPPDATA%\Programs\VIAVW\mcp-config.json`. Open **VIAVW** from Start for its optional manager. Installer: `C:\Programming\Projects\VIAVW\dist\VIAVW-Setup-0.1.2-x64.msi`.

The small native launcher forwards stdio and arguments to the selected release. Supported updates can change the release without changing the configured AI path. **Close applications and disconnect tool clients before updating.** This does not migrate open applications between releases. Normal AI disconnects still preserve the application in its existing release.

## Investigation and implementation

Installed Word and Excel exercise ordinary editing, keyboard translation, document rendering, calculations and native dialogs. Initial experiments used actual registered Codex tools; final installation checks used the public executable through the official MCP SDK and stable launcher.

* Excel's window-DC capture was blank. PrintWindow displayed the actual grid. Creation now defaults to `auto`, selecting `print-window` for WINWORD.EXE and EXCEL.EXE. Explicit choices remain supported; no global Office renderer settings changed.
* Word could reorder a paragraph break after subsequent characters in a mixed text batch. Text now splits at CR/LF/Tab boundaries and uses the existing paced private queue for these controls. Enter, Tab and Backspace key commands use that path too; normal character runs retain fast delivery.
* Excel F12 opened Save As but returned unknown during the modal transition. Pending input intent is now persisted before dispatch. Unknown or partially forwarded input blocks further tool input across client reconnects. Fresh observation and explicit reconciliation are required; no input is replayed.
* The installer, manager, connection file and Codex registration now use the stable executable path. Installer tests have a separate product identity, registry and Start menu folder. Tested and production payloads match byte for byte.
* Early owner exit reports readiness failure without requesting an unsupported exit-code property from a process opened by PID.

A trial shared storage location was rejected by the host's existing owned-directory validation. It was withdrawn. The accepted private-desktop host and its control boundary remain intact; failure evidence is retained.

## Actual compatibility and verification

| Installed application | Verified result |
| --- | --- |
| Word | Three select-all, multiline typing, Backspace, Enter and save cycles. Exact paragraph strings checked inside Word's saved DOCX XML; actual capture visually inspected. Graceful close. |
| Excel | Ctrl+G cell navigation, formulas, computed CSV values 6 and 5, F12 Save As, observation/reconciliation and dialog cancellation. Actual grid visually inspected. Earlier baseline additionally saved genuine XLSX formulas and reopened the workbook. |
| Notepad++ | 25 repeated edit/save workflows while a second client queried another app. Unsaved memory survived controller replacement and was subsequently saved. |
| 7-Zip | Real disposable archive extraction through MCP, independent concurrent layer and graceful close. |

Exact package: **35 public-interface checks**, **14 official-SDK checks**. Stable launcher: the same 14-check SDK workflow plus **7 argument/path/error checks**. Installed Office: **13 checks**. Isolated MSI: **20 checks**, including repair, blocked removal while an app runs and retained documents after removal. Actual installed 0.1.1 → 0.1.2 upgrade, file hashes, doctor, manager self-test and preservation of unrelated Codex configuration were checked.

The stable-launcher run measured 152 input calls: **67.1 ms median, 79.6 ms p95, 96.1 ms maximum**. Separate-layer status median: **2.9 ms**. Twenty-five complete edit/save workflows: **11.83 seconds**. Installed Office input-call median: **70.9 ms**. These are API timings, not end-to-end screen latency or proof of foreground-native equivalence. No new controlled CPU/GPU utilization benchmark was performed.

## Limitations, cleanup and next work

Excel F12 can still report unknown after opening its dialog. This release exposes and contains that uncertainty. Some expanded popup pixels remain absent from composition; accessibility can provide additional evidence. Office recovery-file information was acknowledged only in the disposable instance; existing recovery files were not opened or cleared. Excel sometimes retains owned helpers after normal close; the disposable job trees were force-closed after saved-file verification.

All tested owned application processes are closed; process survivor audits are empty. About **1.59 GB** of rebuildable installer staging was removed. Source, selected executables, installers, logs and screenshots remain. Policy-blocked browser profiles were untouched. No Git operations, CharlieAI changes, unrelated app changes or privileged modifications occurred.

Physical Human acceptance and CharlieAI integration remain pending. The existing Codex conversation lost its old stdio connections during update and did not reload its native VIAVW namespace. Registration points to the new stable path, which passed independent installed MCP tests. A new/reloaded Codex session may be needed for its native tools.

For a manual Office demonstration, create a disposable Word document using installed `WINWORD.EXE` with `/a`, `/n`, and its document path, or an Excel workbook using `EXCEL.EXE` with `/x`, `/s`, and its path. Omit captureMode. Observe readiness and owned windows before input. Handoff opens the Human viewer; click the picture before typing. Only the person can return ownership to automation. Full exe paths are under `C:\Program Files\Microsoft Office\root\Office16`.

Next: investigate native modal transitions and observation-driven readiness, then broaden selection, navigation and dialog workflows. Retain the uncertainty fence and exact saved-content checks.

## Evidence under C:\Programming\Projects\VIAVW

* Public interface: `artifacts/standalone-verification-9ef3b621c97540a08fdd4a7a5cf1adad/verification.json`
* Official SDK: `artifacts/standalone-sdk-66a8809a07ca4bafb65f3945e89a9e58/verification.json`
* Stable launcher SDK: `artifacts/standalone-sdk-17b8591c301c454abd817dedc6c9fde6/verification.json`
* Installed Office: `artifacts/office-tool-8f5c5967b27347458bd1c25babbce3a5/verification.json`
* Installer: `artifacts/installer-verification-7d7d39d1200a41f880379175f0fa671f/verification.json`
* Actual upgrade: `artifacts/local-install-6bd15d0092d54f0baf7b7029827ddec3/verification.json`
* Entry point: `artifacts/entrypoint-verification-42f16bc3056448c7957f521359a54344/verification.json`
* Baseline failures: `artifacts/installed-apps-followup-437e1320cfa34bd5ab9fccc869980792/baseline-findings.json`

# Windows installation — October 23, 2026

Updated installation: **0.1.2** now uses the stable path `%LOCALAPPDATA%\Programs\VIAVW\viavw.exe`. See [installed application follow-up](INSTALLED-APPLICATION-FOLLOWUP-20261023.md) for current paths, tests and remaining limitations. The details below record the original 0.1.1 installation.

VIAVW is installed for this Windows account and registered as an external Codex tool. Its private-desktop implementation and previously accepted portable package remain preserved. CharlieAI's source and configuration were not changed.

## Open VIAVW

Choose **VIAVW** from the Windows Start menu, or open this folder and double-click its **VIAVW** shortcut:

```text
%LOCALAPPDATA%\Programs\VIAVW
```

The manager has **New test document**, **Launch application**, **Take human control**, **Show viewer**, **Hide viewer**, **Close application**, **Refresh**, and **Tool connection**. New test document prepares a disposable Notepad++ document with independent settings. Select an application row to show its viewer or request Human ownership. Return control through the viewer's **Return to automation** button. Closing the manager or a durable viewer preserves the running applications.

The generic launcher accepts one argument per line. Applications can require independent-instance options and particular rendering settings; compatibility still varies. It does not make every application's launch behavior safe or compatible automatically.

## Installer and tool paths

Standard Windows MSI, version 0.1.1:

```text
C:\Programming\Projects\VIAVW\dist\VIAVW-Setup-0.1.1-x64.msi
```

Installed external tool:

```text
%LOCALAPPDATA%\Programs\VIAVW\releases\tool-20261023e\viavw.exe
```

MCP argument: `mcp`. A generated connection file for other compatible AI hosts is ready at:

```text
%LOCALAPPDATA%\Programs\VIAVW\mcp-config.json
```

The existing tool protocol remains version 0.1.0. The 0.1.1 MSI adds the Windows manager and installation lifecycle around those same verified runtime/tool files. Python, an SDK and an installed .NET runtime are not required to run it. The MSI is approximately 62.7 MiB and installs 676 files, including bundled runtimes. It is a locally built unsigned installer; no code-signing certificate was fabricated.

The MSI provides a normal setup wizard, shortcuts, Windows Installed apps registration, repair and removal. Windows Installer confirmed the per-user unmanaged context, with no elevation requirement and no reboot needed in the tests. No driver, Windows service, firewall rule, Windows security policy or physical input setting was installed or changed by VIAVW. Windows Installer itself performs its normal package caching/registration.

## Actual checks

Nineteen [installer acceptance checks](../artifacts/installer-verification-9eb88ee5284f4c538e1ee21174ece67f/verification.json) passed: actual install into a folder containing spaces, all installed hashes, bundled runtime, connection-file paths, manager diagnostics, both shortcuts, per-user registration, installed Notepad++ launch, blocked uninstall preserving its PID/files, graceful close, normal repair, successful uninstall, document/state preservation and registration/shortcut cleanup.

The verified MSI was then installed into the normal account application folder. All 676 installed release files matched their hashes; 670 original package files passed the tool's integrity check. [Local installation receipt](../artifacts/local-install-5619b91a0ada4d819530a3e7ec2efb21/verification.json).

Codex's supported CLI registered only the `viavw` server entry, pointing at the installed executable. The previous configuration was backed up privately in the user's `.codex` directory. Parsed configuration comparison confirmed that all unrelated settings were preserved. The CLI's normalization of an existing empty argument array was detected and restored. Registration follows the official [Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) workflow. No API key or CharlieAI code change was needed.

**Codex loaded all thirteen tools in this same conversation.** Actual calls to the registered tools created a private installed Notepad++ application, captured its image, clicked the editor, selected/replaced text, used Enter and Backspace, saved with Ctrl+S, and verified the exact on-disk content. The resulting PNG was inspected and the application closed normally. [Live registered-tool evidence](../artifacts/codex-installed-live-34566ce1d50645e783645ee90c465f50/verification.json).

The installed manager itself was also launched on a disposable private desktop through those registered tools. Its eight actions, application grid and status were visually inspected, then it was closed normally. [Manager visual check](../artifacts/codex-installed-live-34566ce1d50645e783645ee90c465f50/manager-visual.json). This avoided activating a physical-desktop window or sending physical input. It is visual/automated verification, not fabricated physical Human acceptance.

All test layers are closed. AI-host-managed `viavw.exe mcp` processes remain available as the installed tool connection; they are not running application layers. Multiple Codex clients may each start a server process. Hidden idle layers do not continuously capture.

## Repair, removal and retained data

Use the MSI to repair, or Windows **Settings → Apps → Installed apps → VIAVW** to uninstall. Close the manager and application layers **and disconnect VIAVW in AI clients first**: the guard blocks removal while installed VIAVW processes are active. It does not stop those applications automatically. External AI-host configuration is managed separately; remove its VIAVW entry if permanently uninstalling the tool.

Only shipped files and owned installation shortcuts/configuration are removed. User-created documents, captures, signing key and layer records under the installed release's `artifacts` folder remain retained. Reinstall to the same location to inspect retained data. Do not move a release with live layers or delete their directories. The preserved older research profiles whose deletion was policy-blocked were untouched.

Early testing found a malformed silent-test argument, a diagnostic-mode UI-context deadlock and missing persistence of a custom installation path for removal. They were corrected before the selected MSI passed; earlier test logs remain under `artifacts/installer-verification-*`. No failed installer is distributed in `dist`.

CharlieAI and other hosts can use the installed executable and generated MCP connection file through their supported local tool mechanism. **Actual CharlieAI-side registration and a physical Human handoff remain pending**; the live Codex connection is demonstrated. Application compatibility, generic GPU capture and complete virtual-device emulation retain their previously documented limits.

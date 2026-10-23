# Standalone VIAVW tool — October 23, 2026

VIAVW is now distributed as an executable Windows tool, independent of CharlieAI source code. Its viewer is optional and opened on demand. MCP-capable AI applications can discover and call thirteen tools; other integrations can execute the same tool with JSON arguments. No Python, SDK or installed .NET runtime is required by the package. A local tool runner is required for a cloud AI. CharlieAI and other AI applications' configurations were not changed.

## Deliverables

- Selected executable: `C:\Programming\Projects\VIAVW\artifacts\packages\tool-20261023e\viavw.exe`
- Portable ZIP: `C:\Programming\Projects\VIAVW\artifacts\packages\VIAVW-Tool-0.1.0-tool-20261023e-win-x64.zip`
- [Connection and workflow instructions](STANDALONE-TOOL.md), also included inside the package as README.md.
- [Selected release](../artifacts/standalone-tool-current.json), with exact package/binary hashes and evidence.

Run the executable with `mcp` for a local stdio MCP server, `tools` for tool discovery, `call viavw_TOOL --file request.json` for another integration, or `doctor` to verify release files. The shipped example configuration requires its executable path updated after extraction. Extract to a writable directory and keep the package in place while its layers are running.

The ZIP is approximately **108.7 MiB**; its complete extracted release is approximately **251.0 MiB** across 671 files, including the .NET and Windows Desktop runtimes and their license notices. Bundled runtimes account for most of that size. Test-only Python SDK files, source code, observation signing keys, documents and running layers are excluded from the archive. This is a local portable package, not an installed Windows service or a signed installer.

## Implementation

`src/ViavwTool` implements MCP stdio lifecycle, strict tool schemas, structured results and actual PNG image responses. A CLI uses the same operations. It talks directly to the existing durable per-layer named pipes, with a fresh transport epoch/sequence handshake on each connection. Signed observation tokens bind the target layer, controller epoch, ownership revision and input sequence across tool reconnects. Input never automatically retries.

An exclusive per-layer command file lock serializes separate tool processes; different layers remain independent. Human handoff opens the viewer and blocks AI input. The tool deliberately has no Human-to-Automation reclaim command: the person returns control in the viewer, after which the AI must observe again. Graceful close preserves native save prompts; force explicitly discards an owned layer.

The independent controller launcher does not inherit the AI's stdin/stdout/stderr handles. This avoids keeping an AI transport waiting for EOF until the application closes. It checks physical-machine memory headroom before new controllers, retains startup identity records, and can recover one unambiguous keeper/startup record when a tool dies before writing its application catalog.

The packaged layer host is a self-contained build from the preserved durable-D source snapshot. Existing accepted host/viewer/API executables and their release selections remain unchanged. The new tool has its own package manifest and source hashes. `scripts/build-standalone-tool.ps1`, the two public-interface test scripts, and `scripts/package-standalone-tool.py` reproduce building, verification and package selection.

## Actual verification

The final release passed **35 direct public-interface checks** using a relocated package and a restricted executable search path without Python/SDK/dotnet available to the tool. Tests exercised installed Notepad++ and 7-Zip: click, selection, multiline Unicode/Tab text, Enter, Backspace, save, actual archive extraction, capture, accessibility inspection, optional viewer, native save prompt, Human rejection and explicit disposable cleanup. They also checked reconnect, shared-state rejection across two AI clients, same-request deduplication, live-controller replacement and actual unsaved memory preservation. [Direct evidence](../artifacts/standalone-verification-1ad9968cff7e47ae82511f0c3f1ce723/verification.json).

The **official MCP Python SDK 1.30.0** independently passed **12 further checks**. It parsed all thirteen schemas and actual PNG responses, operated two installed applications, and completed **25 edit/save cycles** with exact saved-content verification while another MCP process made 75 status calls against the other app. It then terminated only the exact owned controller process, simulated a lost tool catalog write, recovered the same application PID, verified the retained editor capture, and saved the actual unsaved text. [SDK evidence](../artifacts/standalone-sdk-aef92fb177804ecfb4b489ff7e3848eb/verification.json).

The final ZIP was extracted into another path containing spaces. Every shipped file passed integrity verification, all thirteen tools were discoverable, and the bundled runtime operated with no installed runtime on the tool's search path. [Archive evidence](../artifacts/standalone-archive-71182fc8a8f14f6c89e26c7287121d0e/verification.json).

Captured images of the recovered Notepad++ document, the native save prompt and the 7-Zip extraction dialog were inspected. All owned applications/controllers were closed, with no survivors in either final application test. Physical Human use was not fabricated; the ownership transitions and denial checks were automated.

## Performance and failed experiments

The official-client run completed 25 six-action editing/saving workflows in **4.57 seconds**, including observations and saved-file verification. Across 152 input calls, median API response was **4.24 ms**, 95th percentile **50.11 ms**, maximum **60.94 ms**. Peer status calls had median **2.27 ms**, maximum **30.02 ms**. These are local tool/forwarding measurements, not glyph-to-screen or physical display latency. The earlier native-viewer fluidity evidence is unchanged. This small workload did not measure new CPU/GPU cost.

The first experimental package exposed premature pipe flushing and inherited process handles that held an external client's EOF open. Both were fixed in the separate tool code. A later experiment reproduced 7-Zip's synchronous modal list-view mouse timeout; the existing backend's explicit posted click option was exposed through the tool and actual extraction then passed. No unknown click was automatically replayed. Failed reports remain in `standalone-verification-07b4f47628a241d0ab86e0ac6a62bfdc` and `standalone-verification-6d67c0720d7c495fb9ea877a39a021dd`; intermediate named builds were retained.

Runtime/test data is retained in owned project directories. The previously policy-blocked browser profiles were untouched. The MCP SDK was installed only into an owned test dependency directory, not globally or inside the release. No Git operations, driver installation, privilege changes, security bypass or unrelated project edits occurred.

## Remaining acceptance

The executable/tool boundary is built and tested. Configuring an actual AI application's tool runner and completing one real collaborative task through it remain separate acceptance steps. CharlieAI is unchanged by the user's explicit boundary. Physical Human interaction with this packaged viewer remains pending. The standalone package directly retested Notepad++ and 7-Zip, not every prior Visual Studio workflow. The underlying host retains those prior capabilities; its experimental Visual Studio setup adapter is not exposed by this first generic-create tool.

Application compatibility still depends on rendering/input behavior and safe independent-instance launch settings. General private-desktop GPU capture, complete virtual-device semantics, all Windows applications and foreground-native visual speed are not universal claims. Real app crashes, OS logoff/reboot or simultaneous controller/keeper loss do not preserve app memory. Cross-process retries of uncertain create/close commands are not exactly-once: list/status and reconcile actual state before deciding what to do next.

Recommended next acceptance: configure this external tool in a chosen AI host, edit/save one disposable installed-application document, ask for Human attention, make a physical edit, return control through the viewer, and let the AI observe and finish the same task in the same application. This requires no VIAVW code embedded in CharlieAI.

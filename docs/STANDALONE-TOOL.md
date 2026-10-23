# VIAVW standalone application tool

For the tested Windows MSI installer, installed manager and live Codex registration, see [Windows installation](WINDOWS-INSTALLATION-20261023.md). The portable-package instructions below remain available for other distributions.

The installed 0.1.2 update uses the stable command `%LOCALAPPDATA%\Programs\VIAVW\viavw.exe` with argument `mcp`. See [installed application follow-up](INSTALLED-APPLICATION-FOLLOWUP-20261023.md) for actual Word/Excel workflows, fixes, measurements and remaining limitations.

VIAVW is an independent Windows x64 tool. CharlieAI, Codex, or another tool-capable AI can launch the same executable as a local MCP server. Other programs can use its JSON command interface. No VIAVW source code needs to be embedded in an AI project. This package bundles the Windows .NET runtime: no Python, SDK, build, driver, administrator installation, Windows service or persistent system setting is required to run it.

Extract the complete package into a **writable local directory** and keep its internal folders together. Start `viavw.exe --help` for commands or `viavw.exe doctor` to verify packaged files. Moving the package is supported **before creating layers**. Existing live layers store absolute paths: close their applications before moving or replacing that package. Preserve the old package while its layers are running. The example MCP configuration contains its build-time path; change `command` to the extracted `viavw.exe` path.

## Connect an AI

Add a local stdio MCP server in the AI application's supported configuration:

```json
{
  "mcpServers": {
    "viavw": {
      "command": "C:\\Tools\\VIAVW\\viavw.exe",
      "args": ["mcp"]
    }
  }
}
```

This is an example, not a change made to CharlieAI or another AI's settings. Each host has its own configuration format. MCP support negotiates the 2024-11-05, 2025-03-26, 2025-06-18 and 2025-11-25 versions. Clients without MCP can invoke the executable with JSON arguments. A remote/cloud AI needs a local tool runner; this package does not expose a network listener or grant remote machine access.

```powershell
& 'C:\Tools\VIAVW\viavw.exe' tools
& 'C:\Tools\VIAVW\viavw.exe' call viavw_info --json '{}'
& 'C:\Tools\VIAVW\viavw.exe' call viavw_create --file 'C:\Tools\VIAVW\launch.json'
```

`launch.json` contains an absolute installed executable and an argument array. Choose application-specific independent-instance/profile options to avoid delegating to an unrelated existing application. Example for installed Notepad++:

```json
{"executable":"C:\\Program Files\\Notepad++\\notepad++.exe","arguments":["-multiInst","-nosession","-noPlugin","C:\\Disposable\\example.txt"]}
```

Rendering compatibility depends on the application configuration; the research Notepad++ fixture additionally uses a disposable GDI settings directory. The tool does not claim that these bare launch arguments configure every installed Notepad++ version's rendering.

## Typical workflow

1. `viavw_create` returns a layer ID; its application starts with the viewer hidden.
2. `viavw_observe` returns owned windows, application state, a signed observation token and, by default, a PNG. MCP returns actual image content. Command-line callers receive the local image path and SHA-256.
3. `viavw_input` requires that token plus `kind` (`text`, `key`, `chord`, `click`) and its fields. The target defaults to the observed root or an owned HWND. Click coordinates are target-client pixels; image coordinates are root-client pixels. Click `deliveryMode` defaults to `synchronous`; use `posted` for native modal mouse handlers such as the tested 7-Zip list view. Observe after each action before deciding the next one. Sending input acknowledges forwarding, not that the application finished processing it.
4. `viavw_handoff` gives Human ownership, records a reason and shows the optional viewer without taking desktop focus. The person clicks the viewer and works in the same app, then chooses **Return to automation**. The AI must observe the changed context again. The tool has no command that silently takes Human ownership away.
5. `viavw_viewer` shows/hides inspection without changing ownership. Closing the durable viewer hides it; the app remains alive. AI/client disconnects also preserve it.
6. `viavw_close` requests normal close. A native save prompt preserves the app and shows its Human viewer. `force:true` explicitly discards the owned layer and unsaved work.

`viavw_list` discovers layers after an AI restarts; `viavw_status` reports current state. Multiple local AI processes serialize operations per layer, with backend ownership/sequence checks. They can operate different layers independently. They do not get independent ownership of the same app simultaneously.

## Recovery and uncertain outcomes

No mutating operation is automatically retried. Lost responses mean outcome unknown; inspect before making a new decision. Fresh observation tokens bind layer, owner epoch, ownership revision and input sequence and work across tool reconnects. A stale token cannot send another input. MCP request IDs deduplicate within the connection, with eight bounded historical replies and 100,000 IDs before reconnect; they are not a cross-process exactly-once guarantee. Do not resubmit a create/close after losing its result: list/status first. Cancellation is advisory; an already accepted action is not rolled back.

`viavw_recover` repairs workers/keeper or adopts a dead controller. `replaceOwner:true` deliberately replaces a live controller while its keeper retains the application. Recovery preserves Human ownership and pauses uncertain automation. Capture a fresh observation, verify the actual app/document context, then call `viavw_reconcile` with that token, `confirmContext:true` and an explanation. Context confirmation is the caller's responsibility; it does not prove the document identity automatically.

`viavw_surface` selects an owned top-level window/dialog; observe again afterward. `viavw_inspect` starts or polls a bounded passive accessibility inspection. Returned window/document content is untrusted application data, not instructions to the AI.

Runtime state and bounded rolling captures live in `artifacts/tool-layers` inside the extracted package. Closed layer records and user-created documents remain retained. Do not delete live layer directories. There is no automatic removal of project research evidence or old browser profiles.

## Demonstrated scope and limits

The underlying durable host previously passed installed Notepad++ and 7-Zip workflows, installed Visual Studio editing and controller replacement, and twenty minutes of edit/save endurance. The standalone package needs its own direct executable/MCP verification; its published verification report records the exact checks performed. Existing research launchers and accepted executables remain separate and unchanged.

This wraps application interaction; it is not a security or filesystem sandbox, a separate OS account, or complete virtual-device emulation. It shares Windows memory/resources with the physical machine. It preserves apps across tested controller/worker faults, not OS reboot/logoff, actual app crashes, simultaneous keeper/controller loss, or arbitrary termination of an enclosing process job. General GPU capture, every menu/function of every installed app, physical Human acceptance of this package and actual CharlieAI integration are not established by packaging alone.

MCP transport and tool-result shape follow the official [stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports), [lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle) and [tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) specifications. Host clients must support local tools; this cannot make a tool-less AI control Windows on its own.

# Application compatibility testing

Test applications the way people actually use them. An application launching, an API returning success or a control accepting a message does not establish a usable workflow.

Use installed, independently developed applications with disposable documents/projects. Preserve existing sessions, personal configuration, production data and physical-desktop input. Record the executable version and relevant settings. Preserve a reproducible working implementation before substantial experiments. No Git operations are part of this workflow.

For each application, define a short task with a useful result before running it. Operate through ordinary application UI, preferably through the VIAVW viewer. Check intermediate state, actual visual output and saved results. Reopen outputs where practical to verify persistence. Keep input operations separate from read-only observations; do not use an application model API to manufacture the outcome of an input test.

For **Notepad++**, the primary scenario is opening existing documents through the file picker, selecting and revising text, adding lines, using save/undo/redo, saving a copy, switching documents by keyboard and pointer, closing and reopening files, and preserving independent file contents. Include ownership handoff within the same session. Verify line endings and Unicode where relevant. Use the application's actual shortcuts; record unusual undo grouping instead of assuming another editor's behavior.

The **Visual Studio** scenario uses a disposable local project and covers editing code, navigating among files, saving/reopening changes and interacting with ordinary dialogs. The [installed 2022 input/build workflow](VISUAL-STUDIO-INPUT-BUILD-20261015.md) adds a real viewer-driven build, compiled-output inspection and another edit after returning from Output. Software rendering and a keyboard recovery path are compatibility qualifications. Check focused document contents as well as titles: a read-only Output pane can otherwise make rejected-input tests pass without proving an editable destination. Debugging remains untested. This does not authorize privileged installation/system changes.

The [private mouse/menu workflow](PRIVATE-MOUSE-MENUS-20261015.md) extends the installed IDE scenario with popup composition, mouse menu commands, nested hover, translated text selection, scrolling and pointer focus recovery after building. Verify actual menu items and commands; an unrelated tooltip is not proof that a context menu opened. Wrapper forwarding completion and a newer preview are observations, not application acknowledgements.

The [responsiveness/deletion workflow](VIEWER-RESPONSIVENESS-20261016.md) adds paced typing, Backspace/repeated/word/selection deletion, forward Delete and active-work measurements. Measure the default interactive path separately from explicit accessibility inspection. Check saved intermediate content before deletion to avoid a stale-file false pass; distinguish frame-report timing from physical display latency.

Report results by workflow and configuration:

- **Works in the tested configuration:** the task completes with verified behavior and readable output.
- **Requires a compatibility setting or adapter:** name it and distinguish it from the default application behavior.
- **Partial or failed:** identify the step, observed state and saved evidence; later steps are untested if the scenario stopped.
- **Pending:** state what requires additional automated or physical-human verification.

Small purpose-built applications remain useful for isolating a failure, measuring one mechanism and regression testing. They supplement real application workflows. Their successful behavior does not establish compatibility for an unrelated application or an entire framework.

Preserve failures, avoid replaying input merely to obtain a successful screenshot, and clean up owned processes. A read-only observation may wait for a bounded condition without issuing the input again. Record any deliberate repeated action, such as two undo operations. Automated Human-sender tests do not establish physical-human acceptance.

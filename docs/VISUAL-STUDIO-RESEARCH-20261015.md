# Visual Studio 2022: initial workflow experiment

Follow-up: [normal package loading plus the IDE's built-in software rendering setting now supports a private editing workflow](VISUAL-STUDIO-PRIVATE-WORKFLOW-20261015.md). This report preserves the earlier failed configuration and isolation incident.

**The ordinary editing workflow failed.** The installed Visual Studio Community 2022 IDE (17.14.36203.30) launched on a private desktop, but its main native capture was blank and its editor could not open the disposable code file. This is not a working Visual Studio demonstration. Visual Studio 2026 was discovered but not run.

## What was tested

The sample is a small, local .NET Framework 4.8 C# library solution with two code files. The intended task was opening the project and files, editing/saving code, navigating files and using dialogs. No external packages, source control operations or application instrumentation were used.

After correcting the launcher issue described below, runs used `/RootSuffix VIAVW…`, `/SafeMode`, `/NoSplash` and a project-local activity log. Microsoft documents the [separate experimental settings profile](https://learn.microsoft.com/en-us/visualstudio/extensibility/devenv-command-line-switches-for-vspackage-development?view=visualstudio) and [Safe Mode](https://learn.microsoft.com/en-us/visualstudio/ide/reference/safemode-devenv-exe?view=vs-2022). These are configuration qualifications; they do not establish compatibility for a user's normal extension set.

| Step | Observation |
| --- | --- |
| Launch private IDE | Shell and disposable solution title appeared |
| Capture main window | `PW_CLIENTONLY | PW_RENDERFULLCONTENT` returned a white image |
| Load C# project | IDE warned that a project was not loaded correctly |
| Acknowledge warning | Ordinary targeted click worked |
| Ctrl+O | Open File dialog appeared and captured readably |
| Enter disposable `Greeting.cs` path | Filename input worked; opening produced a COM `E_FAIL` dialog |
| Edit, save, switch/reopen code documents | Not verified; execution stopped before editing |
| Actual viewer | No usable initial preview within the 90-second harness deadline; shutdown exposed a blocking-operation prompt |

The first sample build inherited the wrapper repository's nullable settings, which are unsuitable for the legacy project's default C# version. A local `Directory.Build.props` isolated the sample. It then **built successfully with the installed MSBuild**, with compiler sharing and node reuse disabled. Repeating the private IDE attempt with that corrected project still produced the project-load warning and COM error. The independent build is a setup control, not successful IDE compatibility.

The activity log reports CSharpPackage initialization failures involving unavailable `SVsRegisterEditors`, plus ServiceHub activation failures including `E_ACCESSDENIED`. The underlying contribution of Safe Mode, the experimental profile, job containment and the private desktop has not been separated. No security or job restrictions were weakened to make it pass. Blank WPF capture is a separate observed issue; the existing .NET 10 startup adapter was not applied to Visual Studio.

## Isolation incident and launcher correction

**The first attempt did not use the intended experimental profile.** Our launcher enclosed every argument in quotes. Visual Studio treated its quoted switches as filenames, opened the disposable solution under the normal profile and showed a missing-file warning. The checked normal-settings digest changed. Its normal `ApplicationPrivateSettings.xml` now contains a VIAVW reference, and the private registry hive was written during that attempt.

There was no IDE process running before the experiment. No unrelated project source was opened or edited. However, **the original normal-settings state has not been restored**: no pre-run restoration copy was retained. The initial settings change must not be hidden by later successful isolation checks.

`Native.Quote` now leaves simple arguments unquoted and escapes arguments that need quotes. Subsequent Safe Mode titles, separate profile paths and unchanged normal-profile hashes verified switch recognition. Five Windows-parser round trips cover spaces, empty arguments, quotes, trailing backslashes, Unicode and literal metacharacters. Setup validation also rejects missing/invalid experimental suffixes, outside-project solutions and extra environment overrides.

## Evidence, cleanup and retained data

- Initial incident: `artifacts/visual-studio-startup-initial`.
- Corrected startup: `artifacts/visual-studio-startup-switches-fixed`.
- Viewer attempt: `artifacts/visual-studio-viewer-initial/vs-workflow.json`.
- Final direct workflow with corrected project: `artifacts/visual-studio-direct-valid-project`.
- Independent build: `artifacts/visual-studio-project-build-corrected.log`.
- Verification/audit: `artifacts/m22-verification-index.json`, `artifacts/m22-final-audit.json`.

All owned IDE/helper processes were contained and removed. Graceful shutdown did not complete during the blocked/modal states, so owned jobs were disposed. Sampled physical desktop, focus, pointer and confinement checks passed. The installed executable stayed unchanged. Checked normal-profile files stayed unchanged in the corrected runs. No account, consent or licensing prompts were automatically accepted.

Visual Studio ignored the attempted AppData environment redirection for its experimental settings. One retained experimental profile occupies roughly **28.3 MiB** under the normal per-user Visual Studio AppData locations. Two project-local disposable profile trees added another **79.1 MiB** to the retained-profile inventory. These paths are recorded in the evidence. No experimental profile or browser profile was deleted. No installers, drivers, privileged operations or Git commands were used.

The existing five-case Notepad++ suite and five-case qIV window suite passed after the common launcher change. The previous working implementation is preserved in `artifacts/baselines/verified-notepad-workflow-20261015-141511`.

Next, isolate Visual Studio's editor/service initialization failure with a repeatable experimental profile before expanding the workflow, and investigate main-window capture separately. Physical-human acceptance remains pending.

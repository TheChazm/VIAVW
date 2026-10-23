# Development tracking and version records

Use local Git for source, reproducible test scripts and sanitized engineering
handoffs. Charlie alone handles GitHub pushing and publication. This workflow does not authorize automation to push or publish. This setup
does not choose a remote URL, license, release tag or commit identity.

## Baseline and commits

Do not stage an active task's changing source. Wait for its final handoff, pin the
release manifest, and deliberately review a coherent snapshot. Start with source,
build scripts, documentation and test source; inspect `git status --short` and
`git diff --cached --stat` plus the staged diff. Do not use an unreviewed whole-tree
add. Review credentials, local paths, licensing and accidental runtime material
before the first commit. Keep files with unresolved review findings unstaged.

Build products, packages, dependency caches, live layer/session data, captures,
raw logs, credentials and machine-specific private settings are ignored. A clean
Git status is not proof that runtime files are safe to delete. Never delete or
move a live layer's files as part of Git housekeeping.

## Line endings

The repository stores normalized LF text. General text and shell scripts check
out with LF; PowerShell/batch scripts check out with CRLF. Common binary assets
are marked binary in `.gitattributes`. Existing working files are not rewritten
by adding this policy; Git normalizes text when staging. Do not change global Git
configuration or mass-convert source to resolve a line-ending notification.

## Version convention

Preserve the existing semantic version and named-build conventions. At this
session's snapshot the selected tool and MSI are both 0.1.2 and package identity
is `tool-20261023i`; these are observations, not a new acceptance declaration.
Version changes must update the existing build/package/installer entry points
consistently and verify their generated metadata. Do not introduce a second
competing version source during active work. Protocol versions and build suffixes
are separate from product release versions. A passing candidate does not prove
that an installation was updated. Accepted tags require a separate human choice.

## Engineering sessions

Create a short Markdown record in `docs/sessions/YYYY-MM-DD-topic.md` for each
bounded development task: objective, files changed, commands/checks actually run,
results, historical evidence used, gaps and next owner/action. Distinguish tests
executed in that session from results read in another task's reports. Record
release identifiers/hashes where useful; omit user document content, tokens,
private machine configuration and raw assistant transcripts. These engineering
records are distinct from runtime application/session records under artifacts.

Core acceptance remains uninterrupted human foreground/keyboard/mouse use while
AI work proceeds. Automated telemetry is bounded observation, not physical human
acceptance. A future platform backend must fail closed instead of silently using
the human desktop. Mac/Linux work is research context only in this session.

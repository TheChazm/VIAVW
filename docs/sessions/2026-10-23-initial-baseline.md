# 2026-10-23: initial local baseline

Objective: record the existing VIAVW source as an honest initial local snapshot
after the original development task's final handoff. Charlie performs all pushing
and publication. No remote, license, release tag or historical commits are chosen.

The supplied final handoff says VIAVW 0.1.2 was installed, owned test apps closed,
and CharlieAI unchanged. Read-only verification at 16:36 UTC matched selected and
installed package `tool-20261023i`, matching manifests and tool/host assembly
hashes. Installation receipt reported success and closed layers. Existing public
interface/SDK/Office reports passed 35/14/13 checks with empty survivor lists.
These are historical results read here, not runtime tests rerun by this session.

Documentation-only fixes: eight account-specific installation prefixes changed
to `%LOCALAPPDATA%`; executable paths, release IDs and historical version labels
remain. Two isolated Windows-1252 dash bytes in the existing viewer speed report
were replaced with UTF-8 en dashes; no whole-file transcode or numeric edit.

Baseline review covers source, reproducible scripts, experiments and sanitized
documentation. Build outputs, installers, dependencies, live layer/session data,
logs, captures, credentials and private configuration remain ignored. Initial
credential-pattern and staged-blob reviews found no matches across 330 UTF-8
text files, with no account-specific paths or generated/private files staged.
Five inherited blank lines at EOF were retained; no implementation rewrite was
made for those whitespace-only warnings. Pattern scanning is not proof that all
private material or licensing issues are absent. Use the existing verified
configured author/committer identity without setting a new identity.

An isolated noninterference harness was prepared outside this source tree.
Seven offline policy tests and syntax/schema/default-mode validation passed with
subprocess creation and Windows API loading blocked. The bounded runtime probe
is a subsequent step and is not claimed as passed in this baseline. Human
acceptance remains pending: laptop activity does not establish physical use of
the tested Windows machine. A minimal .gitattributes policy stores normalized LF text, uses CRLF checkout
for PowerShell/batch scripts, and marks common binary assets; no global Git
setting or existing working-file endings were changed.

No new CPU/GPU benchmark or cross-platform runtime
validation is claimed. CharlieAI and selected/installed releases are unchanged.

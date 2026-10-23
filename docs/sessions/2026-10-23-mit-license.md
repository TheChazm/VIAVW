# 2026-10-23: approved MIT license and company credit

The user approved MIT licensing with the exact project copyright credit:
Copyright (c) 2026 The Coded Craftsman, LLC.

Added root LICENSE using the canonical permission, condition and warranty text
from https://opensource.org/license/mit, with the approved copyright attribution.
No extra license conditions were added. README and current build/package docs
identify the project license and preserve separate third-party terms. Earlier
session records retain their historical pending-license status.

Future tracked and pinned package builds copy LICENSE before manifest hashing;
ZIP assembly already includes all manifest files. MSI payload preparation adds
that exact project notice before file enumeration, reuses an identical existing
notice and rejects a different notice rather than overwriting it. The installer
receipt records the project notice hash. Existing runtime notices remain separate
and unchanged; this does not relicense third-party components. Frozen installed
and selected 0.1.2 packages are not retroactively rewritten.

Validation actually run: python -B scripts/test-project-license.py (six passed)
and python -B scripts/test-installer-inputs.py (twelve passed), with task-owned
temporary directories. The focused tests execute the actual project-license copy
line and manifest hash block from both PowerShell package modes against small
fixtures, verify ZIP notice bytes and unchanged third-party fixture terms, and
check MSI payload notice inclusion/conflict handling before file enumeration.
No compiler, package rebuild, WiX generation, MSI installation or GUI application
was run in this slice. This is notice-inclusion validation, not new runtime or
installer qualification.

Changes are limited to approved licensing, notice propagation, tests and docs.
Existing configured author identity is retained. Charlie alone performs pushing
and publication. No remote edit, push, tag, signing/security change or CharlieAI
change was made. Installed/selected 0.1.2 binaries and manifest remain unchanged.

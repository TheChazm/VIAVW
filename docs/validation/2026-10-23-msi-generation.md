# Explicit MSI input validation and generation: 23 October 2026

Scope: installer build boundary only. Existing authoring/custom actions and
legacy local-selection workflow remain. Explicit archive/package mode validates
the supplied input and tools without relying on ignored selection files.

Twelve offline tests passed: valid archive/directory input, required hash pins,
requested-version agreement, MSI version limits, platform/manifest requirements,
unsafe/absolute/traversal/Windows-reserved paths, case collisions, unmanifested or
tampered files, missing manager/host metadata, extra runtime state, missing tools,
changed input and existing output. Synthetic test files were never executed.

A fresh copy of the tracked source plus this slice contained no artifact/cache
selections. The explicit builder used the previously generated tracked-source
0.1.2 ZIP, with expected SHA-256:
`61FD1D46049B089BF7BD56F3F573ECB7281FA68241B8AA595530FC384C7CA9BC`.
It reused the package's hash-verified manager, compiled the tracked native
launcher using existing MSVC 14.51.36231 and Windows SDK 10.0.26100.0, and invoked
existing WiX tooling with an explicit UI extension 5.0.2 DLL. No dependency was
downloaded or installed; no .NET republish was required.

Generation completed successfully in approximately 41.33 seconds. Output:
`VIAVW-Setup-0.1.2-test-x64.msi`, 65,821,727 bytes, SHA-256
`BA7579BA6DF0EC84D7740D9ADA8E00D7DFF5F8CC055E1875A1EC63E5A278D5AD`.
The receipt covers 676 release payload files plus the stable launcher and its
release pointer. Requested MSI/product version and archive manifest both equal
0.1.2; release identity is `tool-clean-source-012b`. The expected output hash was
rechecked against the generated file. Raw local paths/logs stay excluded.

This is generation evidence only. The MSI was never executed or installed;
repair/removal/upgrade, signatures, fresh-package application compatibility and
physical Human acceptance were not tested. Existing accepted-package lifecycle
reports cannot substitute for qualification of this new MSI.

Installed and selected 0.1.2 `tool-20261023i`, CharlieAI, security/global settings
and runtime applications remain unchanged. No remote, publication, push, release
tag or license choice was made. See BUILDING.md for reproducible inputs/options.

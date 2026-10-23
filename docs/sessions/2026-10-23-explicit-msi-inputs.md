# 2026-10-23: explicit MSI inputs

Objective: finish source-to-ZIP-to-MSI reproducibility without private selection
files. Scope stayed within installer input/tool boundary, tests and docs. Charlie
alone performs publication/pushing; no license, remote or tag is chosen.

Changes: optional explicit archive+SHA-256 or untouched package+manifest-SHA-256;
strict manifest/path/version/file validation; required verified manager reuse;
explicit existing WiX/extension and MSVC/Windows SDK options; fresh output roots;
generation-only receipt. Legacy local-selection and desktop-overlay behavior
remain available. Existing MSI authoring/custom actions were preserved.

Actual commands in a fresh task-owned source copy: Python offline installer-input
test script (12 passed), then the MSI builder with the tracked-source ZIP pin,
expected version 0.1.2, explicit existing compiler/SDK/WiX/extension paths, new
output directory and --test identity. Generation passed in 41.33 seconds, creating
a 65,821,727-byte MSI. The 676-file release payload and two root files were
recorded; generated MSI SHA-256 was independently rechecked. The manager was not
republished and no ignored selection existed in the validation copy.

See docs/BUILDING.md and docs/validation/2026-10-23-msi-generation.md for exact
input/output hashes, prerequisite versions and limits. Raw local logs, payloads
and tool paths remain outside Git. No GUI app or MSI installation was launched.
This does not establish installed/runtime qualification, signing or physical
Human acceptance. Installed/selected 0.1.2 and CharlieAI remain untouched. All
changes were reviewed for secrets/private/generated content before local commit.

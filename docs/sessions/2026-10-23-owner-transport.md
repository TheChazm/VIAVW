# 2026-10-23: owner-transport fail-closed regression

Selected a Windows operational correctness defect instead of additional packaging
work: reusable owner transport could dispatch after a mismatched reply. Original
source reproduced transport_unknown followed by an accepted second fake input;
two simulated requests were received. No real application/desktop/service ran.

Changed only OwnerConnection in src/ViavwTool/Transport.cs. Files helpers remain
unchanged. Ambiguous framing/EOF/timeout invalidates the connection, validates
handshake/envelopes and rejects later writes. Valid owner errors and follow-up
inspection remain supported; normal timeout stays ninety seconds. The tool's
persistent intent fence and Human ownership mechanisms are unchanged.

Added a dependency-free linked-source console test under
tests/ViavwTool.TransportTests, documented no-GUI commands and sanitized evidence.
Actual validation in a fresh task-owned copy: tool/harness offline builds with
zero warnings/errors; all 27 assertions passed after correcting a fake-peer EOF
cleanup failure. The first failed run is retained separately; unexpected fake
peer failures are not suppressed. No runtime defect was inferred from that
test-only cleanup issue.

No idle-machine assumption was made while CharlieAI integration could be active.
No GUI, real layer/service, physical input, new package/install, dependency
download, security/global-setting change, push, publication, remote edit or tag
was performed. Installed/selected 0.1.2 remains unchanged. Actual application and
Human acceptance remain separate. The parallel platform starter is not integrated
or runtime-qualified by this Windows slice. See the validation summary for limits.

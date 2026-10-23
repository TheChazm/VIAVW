# Milestone 1 manual acceptance

Milestone 1 was accepted by the user as a successful proof of concept within its demonstrated compatibility boundaries.

Evidence source: the user's Milestone 1 acceptance and Milestone 2 request. These are user-reported observations, separate from the automated JSON/capture evidence. The user did not supply execution timestamps, hardware-input telemetry, or screenshots for these tests; none are inferred or invented here.

The user reports launching `LayerHost.exe --demo` directly and verifying:

- Background automation accumulated text in the private application while the user continued normal desktop/keyboard/mouse work. No focus stealing, automation-caused pointer motion, unexpected foreground keystrokes, or interference was observed.
- The viewer displayed the text and application state produced by automation, with the same application process identity.
- Human control accepted the user's text through the viewer while the application remained on its private desktop.
- Returning to automation rejected subsequent human input and preserved the running application and document state without restart.

This accepts the tested workflow: automation operates the application, the user voluntarily inspects/takes control, and the user returns control to automation.

The user also reports that `scripts/demo.ps1` used `Start-Process -WindowStyle Hidden`, which hid the actual viewer and led to multiple invisible instances being launched. The original report's claim that this only hid the manager console was incorrect. Direct executable startup showed the viewer and allowed acceptance testing. Milestone 2 corrects the launcher default and adds explicit hidden mode/startup diagnostics/duplicate prevention.

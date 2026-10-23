"""Read-only startup checks. Does not launch apps or assert human acceptance."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import sys

from viavw_client import ROOT, verified_host


def main():
    checks = {}
    problems = {}
    def check(name, operation):
        try:
            if operation() is False:
                raise RuntimeError("Check returned false")
            checks[name] = True
        except Exception as error:
            checks[name] = False
            problems[name] = str(error)
    check("windows", lambda: os.name == "nt")
    check("python_3_10_or_newer", lambda: sys.version_info >= (3, 10))
    check("preserved_host_integrity", verified_host)
    def input_desktop():
        user = ctypes.WinDLL("user32", use_last_error=True)
        user.OpenInputDesktop.restype = ctypes.c_void_p
        user.CloseDesktop.argtypes = [ctypes.c_void_p]
        desktop = user.OpenInputDesktop(0, False, 1)  # DESKTOP_READOBJECTS only.
        if not desktop:
            raise ctypes.WinError(ctypes.get_last_error())
        user.CloseDesktop(desktop)
    check("normal_input_desktop_accessible", input_desktop)
    check("installed_notepad_plus_plus", lambda: any((Path(base) / "Notepad++/notepad++.exe").is_file()
          for base in (os.environ.get("ProgramFiles", ""), os.environ.get("ProgramFiles(x86)", ""))))
    def bundle_integrity():
        bundle = json.loads((ROOT / "artifacts/agent-workspace-current.json").read_text(encoding="utf-8-sig"))
        for relative, expected in bundle["files"].items():
            path = (ROOT / relative).resolve()
            if not path.is_relative_to(ROOT) or hashlib.sha256(path.read_bytes()).hexdigest().upper() != expected:
                raise ValueError("Workspace file changed: " + relative)
        return True
    check("workspace_bundle_integrity", bundle_integrity)
    print(json.dumps(dict(passed=all(checks.values()), checks=checks, problems=problems,
          scope="VIAVW startup prerequisites; application workflows require test-agent-readiness.ps1",
          remainingAcceptance=["CharlieAI adapter in its own project", "Physical human handoff",
                               "Isolation of deep-accessibility faults that can terminate owned application layers"]), indent=2))
    if not all(checks.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    main()

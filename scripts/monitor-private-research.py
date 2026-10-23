"""Read-only physical desktop samples during direct VIAVW experiments."""
import ctypes
from ctypes import wintypes as w
import json
from pathlib import Path
import sys
import time


def main():
    output = Path(sys.argv[1]).resolve()
    user = ctypes.WinDLL("user32", use_last_error=True)
    user.GetForegroundWindow.restype = w.HWND
    user.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
    user.OpenInputDesktop.restype = w.HANDLE
    user.CloseDesktop.argtypes = [w.HANDLE]
    user.GetUserObjectInformationW.argtypes = [w.HANDLE, ctypes.c_int, w.LPVOID, w.DWORD, ctypes.POINTER(w.DWORD)]
    class LastInput(ctypes.Structure):
        _fields_ = [("size", w.UINT), ("tick", w.DWORD)]
    def sample():
        point, clip, pid = w.POINT(), w.RECT(), w.DWORD()
        if not user.GetCursorPos(ctypes.byref(point)) or not user.GetClipCursor(ctypes.byref(clip)):
            raise ctypes.WinError(ctypes.get_last_error())
        foreground = user.GetForegroundWindow()
        user.GetWindowThreadProcessId(foreground, ctypes.byref(pid))
        last = LastInput(ctypes.sizeof(LastInput), 0)
        if not user.GetLastInputInfo(ctypes.byref(last)):
            raise ctypes.WinError(ctypes.get_last_error())
        desktop = user.OpenInputDesktop(0, False, 1)
        if not desktop:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            name, length = ctypes.create_unicode_buffer(256), w.DWORD()
            if not user.GetUserObjectInformationW(desktop, 2, name, ctypes.sizeof(name), ctypes.byref(length)):
                raise ctypes.WinError(ctypes.get_last_error())
        finally:
            user.CloseDesktop(desktop)
        return dict(foreground=foreground, foregroundPid=pid.value, x=point.x, y=point.y,
                    clip=[clip.left, clip.top, clip.right, clip.bottom], inputTick=last.tick, desktop=name.value)
    started = time.monotonic()
    samples, previous, failure = [], None, None
    print("Physical desktop sampling started (read-only)", flush=True)
    try:
        while time.monotonic() - started < 3600 and not (output / "monitor-stop").exists():
            current = sample()
            if current != previous:
                samples.append(dict(ms=round((time.monotonic()-started)*1000, 2), **current))
                previous = current
            time.sleep(.025)
    except Exception as error:
        failure = repr(error)
    (output / "physical-monitor.json").write_text(json.dumps(dict(samples=samples, failure=failure,
        seconds=time.monotonic()-started, note="Read-only 25 ms samples. Launch coverage is specified in the research report; activity origin is not inferred."), indent=2), encoding="utf-8")
    print(json.dumps(dict(failure=failure, changes=len(samples), seconds=time.monotonic()-started)), flush=True)


if __name__ == "__main__":
    main()

using System.Runtime.InteropServices;

namespace LayerHost;

// Separate experiment: no global focus/cursor/input writes, hooks or application code.
internal static class WindowActivationResearch
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern nint SetActiveWindow(nint hwnd);
    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public nint hwnd, after; public int x, y, width, height; public uint flags; }
    internal static object Run(nint root, string mode)
    {
        WindowCaptureResearch.PrivateOnly();
        if (Native.GetParent(root) != 0) throw new ArgumentException("Activation research requires a top-level source window.");
        uint target = Native.GetWindowThreadProcessId(root, out _); var before = Read(target);
        if (mode is "hide-noactivate" or "show-noactivate")
            Native.Check(SetWindowPos(root, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x200 | (mode == "hide-noactivate" ? 0x80u : 0x40u)), "Private visibility without activation");
        else if (mode is "active-focus" or "active-pulse")
        {
            uint current = Native.GetCurrentThreadId(); Native.PeekMessageW(out _, 0, 0, 0, 0);
            Native.Check(Native.AttachThreadInput(current, target, true), "Attach private activation queues");
            try { if (mode == "active-pulse") { SetActiveWindow(0); Native.SetFocus(0); } SetActiveWindow(root); Native.SetFocus(root); }
            finally { Native.Check(Native.AttachThreadInput(current, target, false), "Detach private activation queues"); }
        }
        else if (mode is "show-notification" or "hide-notification")
            Native.Message(root, 0x18, mode == "show-notification" ? 1u : 0u, 0); // WM_SHOWWINDOW, no style change.
        else if (mode is "size-pulse" or "size-minimize" or "size-restore")
        {
            Native.Check(Native.GetClientRect(root, out var client), "Read owned client size");
            nint size = (client.Right & 0xffff) | ((client.Bottom & 0xffff) << 16);
            if (mode != "size-restore") Native.Message(root, 0x5, 1, size); // SIZE_MINIMIZED notification only.
            if (mode != "size-minimize") Native.Message(root, 0x5, 0, size); // SIZE_RESTORED.
        }
        else if (mode is "position-show-notification" or "position-hide-notification")
            NotifyPositionVisibility(root, mode == "position-show-notification");
        else if (mode is "activate-notification" or "activate-only" or "clickactivate-only" or "deactivate-only")
        {
            if (mode == "activate-notification") NotifyActivation(root);
            else Native.Message(root, 0x6, mode == "deactivate-only" ? 0u : mode == "clickactivate-only" ? 2u : 1u, 0);
        }
        else if (mode != "observe") throw new ArgumentException("Unknown private activation experiment.");
        return new { mode, before, after = Read(target), visible = Native.IsWindowVisible(root) };
    }
    internal static void NotifyActivation(nint root)
    {
        WindowCaptureResearch.PrivateOnly();
        // Application notifications, not global activation or a virtual input device.
        // The worker must validate its owned top-level source before calling.
        Native.Message(root, 0x6, 0, 0); // WM_ACTIVATE / WA_INACTIVE
        Native.Message(root, 0x6, 1, 0); // WM_ACTIVATE / WA_ACTIVE
    }
    internal static void NotifyPositionVisibility(nint root, bool shown)
    {
        WindowCaptureResearch.PrivateOnly();
        // WM_WINDOWPOSCHANGED is a system message with a marshalled WINDOWPOS.
        // Research only: does not call SetWindowPos or change native visibility.
        Native.Check(Native.GetWindowRect(root, out var bounds), "Read owned bounds");
        var position = new WindowPosition { hwnd = root, x = bounds.Left, y = bounds.Top, width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top, flags = 0x1 | 0x2 | 0x4 | 0x10 | 0x200 | (shown ? 0x40u : 0x80u) };
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowPosition>());
        try { Marshal.StructureToPtr(position, buffer, false); Native.Message(root, 0x47, 0, buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    static object Read(uint thread)
    {
        var state = new Native.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<Native.GUITHREADINFO>() };
        Native.Check(Native.GetGUIThreadInfo(thread, ref state), "Observe owned GUI thread");
        return new { active = (long)state.hwndActive, focus = (long)state.hwndFocus, capture = (long)state.hwndCapture, state.flags };
    }
}

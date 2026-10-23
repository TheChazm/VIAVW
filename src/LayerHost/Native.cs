using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LayerHost;

internal static partial class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] internal static extern bool RedrawWindow(nint hwnd,nint rect,nint region,uint flags);
    [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)] internal static extern nint OpenThread(uint access,bool inherit,uint threadId);
    internal const uint WM_CHAR = 0x102, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    internal const uint DESKTOP_ACCESS = 0x0001 | 0x0002 | 0x0004 | 0x0040 | 0x0080; // Deliberately excludes SWITCHDESKTOP / hooks.
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2; public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PROCESS_INFORMATION { public nint hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)] internal struct BASIC_LIMIT
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public nuint Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] internal struct EXTENDED_LIMIT { public BASIC_LIMIT BasicLimitInformation; public IO_COUNTERS IoInfo; public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    internal delegate bool EnumProc(nint hwnd, nint arg);
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO { public uint cbSize, flags; public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [StructLayout(LayoutKind.Sequential)] internal struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public POINT pt; public uint lPrivate; }
    [DllImport("user32.dll")] internal static extern bool PeekMessageW(out MSG message, nint hwnd, uint first, uint last, uint remove);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetKeyboardState(byte[] state);
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetCapture();
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessageW(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateDesktopW(string name, nint device, nint devmode, uint flags, uint access, nint security);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] internal static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetUserObjectInformationW(nint obj, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDesktopWindows(nint desktop, EnumProc callback, nint arg);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint arg);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint OpenDesktopW(string name, uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassNameW(nint hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] internal static extern nint ChildWindowFromPointEx(nint parent, POINT point, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    internal static uint? LastInputTick()
    { var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() }; return GetLastInputInfo(ref info) ? info.dwTime : null; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SendMessageTimeoutW(nint hwnd, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")] internal static extern nint SendTextTimeout(nint hwnd, uint message, nuint wparam, StringBuilder lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern int GetScrollPos(nint window, int bar);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out RECT value, uint size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, uint size);
    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetInformationJobObject(nint job, int infoClass, ref EXTENDED_LIMIT info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool QueryInformationJobObject(nint job, int infoClass, nint buffer, uint length, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(nint process, uint exit);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsProcessInJob(nint process, nint job, out bool member);
    [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList([Out] uint[] processes, uint capacity);
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    internal static void DetachSoleOwnedConsole()
    {
        var clients = new uint[1024];
        if (GetConsoleProcessList(clients, (uint)clients.Length) == 1 && clients[0] == Environment.ProcessId) FreeConsole();
    }

    internal static void Check(bool ok, string operation) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }
    internal static string ObjectName(nint handle)
    {
        var text = new StringBuilder(512);
        Check(GetUserObjectInformationW(handle, 2, text, 1024, out _), "GetUserObjectInformation");
        return text.ToString();
    }
    internal static string InputDesktopName()
    {
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == 0) return "unavailable";
        try { return ObjectName(desktop); } finally { CloseDesktop(desktop); }
    }
    internal static bool DesktopExists(string name)
    {
        var handle = OpenDesktopW(name, 0, false, 1);
        if (handle == 0) return false;
        CloseDesktop(handle); return true;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint hwnd, StringBuilder text, int capacity);
    internal static string WindowCaption(nint hwnd)
    {
        // All callers exclude our own PID. Cross-process captions come from
        // USER's stored title, without running another process's WM_GETTEXT.
        var text = new StringBuilder(8192); GetWindowTextW(hwnd, text, text.Capacity); return text.ToString();
    }
    internal static string WindowText(nint hwnd)
    {
        var text = new StringBuilder(8192);
        if (SendTextTimeout(hwnd, 0x000D, 8192, text, 2 | 0x20, 500, out _) == 0) return "<text unavailable>";
        return text.ToString();
    }
    internal static string ClassName(nint hwnd) { var s = new StringBuilder(256); GetClassNameW(hwnd, s, s.Capacity); return s.ToString(); }
    internal static void Message(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        if (SendMessageTimeoutW(hwnd, message, wparam, lparam, 2 | 0x20, 750, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Message 0x{message:X} timed out or was rejected");
    }
    internal static string Quote(string argument)
    {
        // Some native applications parse switches before removing quotes.
        // Quote only arguments that actually need Windows argument escaping.
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')) return argument;
        var b = new StringBuilder("\""); int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
            else { b.Append('\\', slashes); b.Append(c); }
            slashes = 0;
        }
        return b.Append('\\', slashes * 2).Append('"').ToString();
    }
    internal static PROCESS_INFORMATION LaunchSuspended(string exe, IEnumerable<string> args, string? desktop, string? directory, Dictionary<string, string>? overrides = null, ushort? startupShow = null)
    {
        exe = Path.GetFullPath(exe);
        var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = desktop };
        if (startupShow != null) { startup.dwFlags |= 1; startup.wShowWindow = startupShow.Value; }
        var command = new StringBuilder(string.Join(" ", new[] { exe }.Concat(args).Select(Quote)));
        nint environment = 0;
        try
        {
            if (overrides != null)
            {
                var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables()) variables[(string)variable.Key] = (string)variable.Value!;
                foreach (var pair in overrides)
                {
                    if (pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0')) throw new ArgumentException("Invalid child environment variable.");
                    variables[pair.Key] = pair.Value;
                }
                environment = Marshal.StringToHGlobalUni(string.Join('\0', variables.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0");
            }
            Check(CreateProcessW(exe, command, 0, 0, false, 0x4 | 0x08000000 | (environment != 0 ? 0x400u : 0), environment, directory, ref startup, out var info), "CreateProcess suspended");
            return info;
        }
        finally { if (environment != 0) Marshal.FreeHGlobal(environment); }
    }
    internal static int[] JobProcessIds(nint job)
    {
        int size = 65536;
        nint mem = Marshal.AllocHGlobal(size);
        try
        {
            Check(QueryInformationJobObject(job, 3, mem, (uint)size, out _), "Query job process IDs");
            int count = Marshal.ReadInt32(mem, 4);
            return Enumerable.Range(0, count).Select(i => checked((int)Marshal.ReadInt64(mem, 8 + i * 8))).ToArray();
        }
        finally { Marshal.FreeHGlobal(mem); }
    }
}

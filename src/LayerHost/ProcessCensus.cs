using System.Runtime.InteropServices;
using System.Text;

namespace LayerHost;

internal record ProcessIdentity(int Pid, int ParentPid, string Name, string? ImagePath, long? CreationFileTime, uint? ExitCode);

// Read-only process identity for IDs already returned by the layer's job. Parent PIDs
// alone are historical and can be reused; any classification must also check identity/job.
internal static class ProcessCensus
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ENTRY
    {
        public uint size, usage, pid; public nuint heap; public uint module, threads, parent; public int priority; public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32FirstW(nint snapshot, ref ENTRY entry);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32NextW(nint snapshot, ref ENTRY entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder path, ref uint size);
    internal static ProcessIdentity[] Read(int[] ids)
    {
        var wanted = ids.ToHashSet(); var records = new List<ProcessIdentity>(); nint snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) return [];
        try
        {
            var entry = new ENTRY { size = (uint)Marshal.SizeOf<ENTRY>(), name = "" };
            if (!Process32FirstW(snapshot, ref entry)) return [];
            do
            {
                if (!wanted.Contains((int)entry.pid)) continue;
                string? image = null; long? created = null; uint? exit = null; nint process = Native.OpenProcess(0x1000, false, entry.pid);
                if (process != 0)
                {
                    try
                    {
                        var text = new StringBuilder(32768); uint length = (uint)text.Capacity; if (QueryFullProcessImageNameW(process, 0, text, ref length)) image = text.ToString();
                        if (Native.GetProcessTimes(process, out long creation, out _, out _, out _)) created = creation;
                        if (Native.GetExitCodeProcess(process, out uint code)) exit = code;
                    }
                    finally { Native.CloseHandle(process); }
                }
                records.Add(new((int)entry.pid, (int)entry.parent, entry.name, image, created, exit));
            } while (Process32NextW(snapshot, ref entry));
        }
        finally { Native.CloseHandle(snapshot); }
        return records.ToArray();
    }
}

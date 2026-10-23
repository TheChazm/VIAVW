using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ViavwTool;

// Independent owners must not inherit the AI's stdio pipe handles. Otherwise
// a completed MCP process can leave its client's EOF pending until app shutdown.
internal static class DetachedProcess
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Startup
    {
        internal uint Size;internal string? Reserved,Desktop,Title;
        internal uint X,Y,XSize,YSize,XChars,YChars,Fill,Flags;
        internal ushort Show,ReservedSize;internal nint ReservedPointer,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)]struct Information{internal nint Process,Thread;internal uint Pid,Tid;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateProcessW(string application,StringBuilder command,nint processAttributes,nint threadAttributes,bool inherit,uint flags,nint environment,string directory,ref Startup startup,out Information information);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
    internal static Process Start(string executable,string directory,string[] arguments)
    {
        // These arguments are generated paths/flags; Windows file names cannot
        // contain quotes. Never pass application text or a shell command here.
        if(arguments.Any(a=>a.Contains('"')||a.EndsWith('\\')))throw new ArgumentException("Invalid owner control path.");
        var command=new StringBuilder('"'+executable+'"'+" "+string.Join(" ",arguments.Select(a=>'"'+a+'"')));
        var startup=new Startup{Size=(uint)Marshal.SizeOf<Startup>()};
        if(!CreateProcessW(executable,command,0,0,false,0x08000000,0,directory,ref startup,out var info))throw new Win32Exception(Marshal.GetLastWin32Error());
        try{return Process.GetProcessById((int)info.Pid);}finally{CloseHandle(info.Thread);CloseHandle(info.Process);}
    }
    [StructLayout(LayoutKind.Sequential)]struct Memory
    {
        internal uint Size,Load;internal ulong TotalPhysical,AvailablePhysical,TotalCommit,AvailableCommit,TotalVirtual,AvailableVirtual,Extended;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx(ref Memory memory);
    internal static void RequireRoom(bool recovery)
    {
        var memory=new Memory{Size=(uint)Marshal.SizeOf<Memory>()};
        if(!GlobalMemoryStatusEx(ref memory))throw new Win32Exception(Marshal.GetLastWin32Error());
        ulong physical=(recovery?128UL:512UL)*1024*1024,commit=(recovery?128UL:1024UL)*1024*1024;
        if(memory.AvailablePhysical<physical||memory.AvailableCommit<commit)throw new ToolFailure("machine_capacity","Insufficient physical-machine memory headroom. Existing apps are retained; no new owner started.");
    }
}

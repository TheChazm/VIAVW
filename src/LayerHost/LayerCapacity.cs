using System.Runtime.InteropServices;
namespace LayerHost;
internal static class LayerCapacity
{
    internal const ulong PhysicalReserve=512UL*1024*1024,CommitReserve=1024UL*1024*1024;
    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus
    {internal uint Length,Load;internal ulong TotalPhysical,AvailablePhysical,TotalCommit,AvailableCommit,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    internal static bool HasRoom(ulong physical,ulong commit)=>physical>=PhysicalReserve&&commit>=CommitReserve;
    internal static void RequireRoom()
    {
        var state=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};Native.Check(GlobalMemoryStatusEx(ref state),"Read layer-0 memory availability");
        if(!HasRoom(state.AvailablePhysical,state.AvailableCommit))throw new AgentException("machine_capacity","Layer 0 must retain at least 512 MiB available physical memory and 1 GiB commit capacity before starting another application. Existing applications are retained.");
    }
}

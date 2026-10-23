using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LayerHost;

// One top-down opaque surface backed by the preview's page-file mapping.
// Its capture thread owns the memory DC. Source DCs are acquired per frame.
internal sealed class MappedPreviewDc : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        internal uint Size;
        internal int Width,Height;
        internal ushort Planes,BitCount;
        internal uint Compression,ImageBytes;
        internal int XPels,YPels;
        internal uint Colors,Important,Color;
    }
    [DllImport("gdi32.dll",SetLastError=true)] static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll",SetLastError=true)] static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint pixels,SafeMemoryMappedFileHandle section,uint offset);
    [DllImport("gdi32.dll",SetLastError=true)] static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("gdi32.dll",SetLastError=true)] static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)] static extern bool RestoreDC(nint dc,int state);
    readonly uint thread=Native.GetCurrentThreadId();
    nint dc,bitmap,original;
    internal int Width { get; }
    internal int Height { get; }
    internal MappedPreviewDc(SafeMemoryMappedFileHandle section,int width,int height)
    {
        Width=width;Height=height;
        try
        {
            dc=CreateCompatibleDC(0);if(dc==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Create private preview memory DC");
            var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,BitCount=32,ImageBytes=checked((uint)(width*height*4))};
            bitmap=CreateDIBSection(dc,ref info,0,out nint pixels,section,0);
            if(bitmap==0||pixels==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Create mapped preview DIB");
            original=SelectObject(dc,bitmap);
            if(original==0||original==-1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Select mapped preview DIB");
        }
        catch{Dispose();throw;}
    }
    internal double Render(Func<nint,double> draw)
    {
        AssertThread();if(dc==0)throw new ObjectDisposedException(nameof(MappedPreviewDc));
        int state=SaveDC(dc);if(state==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Save preview DC state");
        try{return draw(dc);}
        finally
        {
            // Complete GDI writes before the reader is signalled, even on error.
            bool restored=RestoreDC(dc,state),flushed=GdiFlush();
            if(!restored||!flushed)throw new InvalidOperationException("Mapped preview GDI synchronization failed.");
        }
    }
    void AssertThread(){if(thread!=Native.GetCurrentThreadId())throw new InvalidOperationException("Mapped preview DC belongs to its capture thread.");}
    public void Dispose()
    {
        AssertThread();
        if(dc!=0&&original!=0&&original!=-1)SelectObject(dc,original);
        original=0;
        if(bitmap!=0){DeleteObject(bitmap);bitmap=0;}
        if(dc!=0){DeleteDC(dc);dc=0;}
    }
}

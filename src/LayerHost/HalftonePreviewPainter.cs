using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LayerHost;

// Research-only opaque RGB blitter. Bitmap storage remains owned by GDI+;
// the lock, borrowed destination HDC and saved DC state never outlive a draw.
internal static class HalftonePreviewPainter
{
    [StructLayout(LayoutKind.Sequential)]
    struct Info
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, Bits;
        internal uint Compression, Bytes; internal int X, Y; internal uint Colors, Important, Color;
    }
    [DllImport("gdi32.dll")] static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool RestoreDC(nint dc,int saved);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(nint dc,int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(nint dc,int x,int y,nint previous);
    [DllImport("gdi32.dll")] static extern int IntersectClipRect(nint dc,int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern int StretchDIBits(nint dc,int x,int y,int width,int height,int sx,int sy,int sw,int sh,nint bits,ref Info info,uint usage,uint rop);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    internal static bool Draw(Graphics target,Bitmap image,Rectangle destination,Rectangle clip)
    {
        if(image.PixelFormat!=PixelFormat.Format32bppRgb || destination.Width<=0 || destination.Height<=0)return false;
        var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
        try
        {
            if(data.Stride!=image.Width*4)return false;
            nint dc=target.GetHdc();
            try
            {
                int saved=SaveDC(dc);Native.Check(saved!=0,"Save preview paint DC");
                try
                {
                    Native.Check(SetStretchBltMode(dc,4)!=0,"Set HALFTONE preview stretch mode");
                    Native.Check(SetBrushOrgEx(dc,0,0,0),"Align preview stretch brush");
                    Native.Check(IntersectClipRect(dc,clip.Left,clip.Top,clip.Right,clip.Bottom)!=0,"Intersect preview clip");
                    var info=new Info{Size=40,Width=image.Width,Height=-image.Height,Planes=1,Bits=32,Bytes=checked((uint)(data.Stride*data.Height))};
                    int rows=StretchDIBits(dc,destination.X,destination.Y,destination.Width,destination.Height,0,0,image.Width,image.Height,data.Scan0,ref info,0,0x00CC0020);
                    Native.Check(rows!=0&&rows!=-1,"Stretch opaque preview pixels");
                    Native.Check(GdiFlush(),"Flush preview pixels before unlocking");
                }
                finally{Native.Check(RestoreDC(dc,saved),"Restore preview paint DC");}
            }
            finally{target.ReleaseHdc(dc);}
            return true;
        }
        finally{image.UnlockBits(data);}
    }
}

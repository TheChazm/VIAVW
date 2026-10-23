using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LayerHost;

internal static class VisualStudioFrameChecks
{
    // This disposable VS workflow contains no solid magenta UI regions. The
    // capture's deliberate failure color must not pass merely because UIA and
    // input still operate. Keep this oracle in QA, not arbitrary-app capture.
    internal static void RequireNoSentinel(byte[] png)
    {
        using var stream=new MemoryStream(png);using var image=new Bitmap(stream);
        var bits=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        int magenta=0;
        try
        {
            var row=new byte[image.Width*4];
            for(int y=0;y<image.Height;y+=4)
            {
                Marshal.Copy(bits.Scan0+y*bits.Stride,row,0,row.Length);
                for(int x=0;x<image.Width;x+=4){int i=x*4;if(row[i]>=245&&row[i+1]<=5&&row[i+2]>=245)magenta++;}
            }
        }
        finally{image.UnlockBits(bits);}
        if(magenta>32)throw new InvalidDataException($"Visual Studio frame contains {magenta} sampled magenta failure pixels. Inspect the saved PNG; UIA state alone cannot accept it.");
    }
}

using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

// Opt-in QA telemetry: compare a small source-pixel region with its first frame.
// No OCR, application adapter, extra capture, or work in ordinary viewer frames.
internal sealed class PreviewPixelProbe(string directory)
{
    record Configuration(long Root,int X,int Y,int Width,int Height,bool ExportPixels=false,int[]? Mask=null);
    Configuration? configuration;
    internal int? ChangedPixels {get;private set;}
    byte[]? baseline;
    bool[]? mask;
    internal void Reset(){configuration=null;baseline=null;mask=null;ChangedPixels=null;}
    long lastInputQpc,lastInputSequence;
    internal void InputForwarded(long sequence){lastInputQpc=System.Diagnostics.Stopwatch.GetTimestamp();lastInputSequence=sequence;}
    internal object Sample(Bitmap bitmap,long root)
    {
        if(configuration==null)
        {
            string file=Path.Combine(directory,"pixel-probe.json");
            if(!File.Exists(file))return new{ready=false};
            configuration=JsonSerializer.Deserialize<Configuration>(SnapshotFiles.ReadText(file))!;
            if(configuration.Mask is {} indices){if(indices.Length is <1 or >4096||indices.Any(i=>i<0||i>=(long)configuration.Width*configuration.Height)||indices.Distinct().Count()!=indices.Length)throw new InvalidDataException("Invalid QA glyph mask");if((long)configuration.Width*configuration.Height>4096)throw new InvalidDataException("Bound the masked QA region");mask=new bool[configuration.Width*configuration.Height];foreach(int index in indices)mask[index]=true;}
        }
        var c=configuration;
        if(c.Root!=root)return new{ready=false,reason="Source window changed"};
        if(c.Width<1||c.Height<1||c.X<0||c.Y<0||(long)c.Width*c.Height>4096||c.X+c.Width>bitmap.Width||c.Y+c.Height>bitmap.Height)throw new InvalidDataException("Bound the QA pixel region to 4096 source pixels inside its original window.");
        var bytes=new byte[c.Width*c.Height*4];
        var bits=bitmap.LockBits(new Rectangle(c.X,c.Y,c.Width,c.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<c.Height;y++)Marshal.Copy(bits.Scan0+y*bits.Stride,bytes,y*c.Width*4,c.Width*4);}
        finally{bitmap.UnlockBits(bits);}
        baseline??=(byte[])bytes.Clone();int changed=0;
        for(int i=0;i<bytes.Length;i+=4)if((mask==null||mask[i/4])&&(Math.Abs(bytes[i]-baseline[i])+Math.Abs(bytes[i+1]-baseline[i+1])+Math.Abs(bytes[i+2]-baseline[i+2])>75))changed++;
        ChangedPixels=changed;
        return new{ready=true,changedPixels=changed,pixels=c.ExportPixels?bytes:null,configuration,lastInputQpc,lastInputSequence,frameQpc=System.Diagnostics.Stopwatch.GetTimestamp(),comparison="Captured source bitmap against its first configured region; alpha ignored"};
    }
}

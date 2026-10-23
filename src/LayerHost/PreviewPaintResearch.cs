using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace LayerHost;
internal static class PreviewPaintResearch
{
    [DllImport("user32.dll")]static extern int GetGuiResources(nint process,uint flags);
    internal static int Run(string output,string imagePath)
    {
        GpuBridgeVerification.PrepareOutput(output);
        using var input=new Bitmap(imagePath);
        using var source=new Bitmap(1920,1032,PixelFormat.Format32bppRgb);
        using(var g=Graphics.FromImage(source)){g.Clear(Color.White);g.DrawImage(input,new Rectangle(0,0,source.Width,source.Height));}
        // A large ordinary-app screenshot supplies text, icons, gradients and menus.
        using var target=new Bitmap(1598,859,PixelFormat.Format32bppRgb);
        using var graphics=Graphics.FromImage(target);
        var destination=new Rectangle(0,0,target.Width,target.Height);
        void Draw(int mode){graphics.CompositingMode=mode==2?System.Drawing.Drawing2D.CompositingMode.SourceCopy:System.Drawing.Drawing2D.CompositingMode.SourceOver;if(mode==1){if(!HalftonePreviewPainter.Draw(graphics,source,destination,destination))throw new InvalidOperationException("Opaque benchmark not supported");}else{graphics.DrawImage(source,destination);graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);}}
        using var process=Process.GetCurrentProcess();int coldHandles=GetGuiResources(process.Handle,0);
        for(int n=0;n<12;n++){Draw(0);Draw(1);Draw(2);}
        int handlesBefore=GetGuiResources(process.Handle,0);
        var samples=new List<object>();
        for(int round=0;round<80;round++)foreach(int mode in round%2==0?new[]{0,1,2}:new[]{2,1,0})
        {
            long begin=Stopwatch.GetTimestamp();Draw(mode);double ms=Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
            samples.Add(new{round,mode,milliseconds=ms});
        }
        Draw(0);target.Save(Path.Combine(output,"gdi-plus.png"),ImageFormat.Png);
        Draw(1);target.Save(Path.Combine(output,"halftone.png"),ImageFormat.Png);
        Draw(2);target.Save(Path.Combine(output,"source-copy.png"),ImageFormat.Png);
        var checks=new Dictionary<string,bool>();
        using(var pattern=new Bitmap(31,19,PixelFormat.Format32bppRgb))
        using(var exact=new Bitmap(43,29,PixelFormat.Format32bppRgb))
        {
            for(int y=0;y<pattern.Height;y++)for(int x=0;x<pattern.Width;x++)pattern.SetPixel(x,y,Color.FromArgb((x*17+y*3)%256,(x*7+y*31)%256,(x*41+y*11)%256));
            using(var g=Graphics.FromImage(exact)){g.Clear(Color.Magenta);checks["opaque-draw"]=HalftonePreviewPainter.Draw(g,pattern,new Rectangle(5,4,31,19),new Rectangle(8,6,22,13));}
            bool pixels=true;for(int y=0;y<exact.Height;y++)for(int x=0;x<exact.Width;x++)pixels&=(exact.GetPixel(x,y).ToArgb()==(x>=8&&x<30&&y>=6&&y<19?pattern.GetPixel(x-5,y-4):Color.Magenta).ToArgb());
            checks["one-to-one-top-down-rgb-offset-clip"]=pixels;
            using var alpha=new Bitmap(3,3,PixelFormat.Format32bppArgb);using var dest=Graphics.FromImage(exact);
            checks["alpha-rejected"]=!HalftonePreviewPainter.Draw(dest,alpha,new Rectangle(0,0,3,3),new Rectangle(0,0,3,3));
        }
        int handlesAfter=GetGuiResources(process.Handle,0);checks["no-gdi-handle-growth"]=handlesBefore==handlesAfter;
        var result=new{passed=checks.Values.All(x=>x),checks,coldHandles,handlesBefore,handlesAfter,samples,source=new{source.Width,source.Height},destination,note="Offscreen drawing microbenchmark, not end-to-end application latency. Both paths flush before timing ends. Source screenshot resampled only to establish common benchmark dimensions."};
        File.WriteAllText(Path.Combine(output,"paint-research.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return checks.Values.All(x=>x)?0:1;
    }
}

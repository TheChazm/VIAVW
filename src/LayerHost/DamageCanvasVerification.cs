using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace LayerHost;
// An owned private-desktop fixture checks the real double-buffered control's
// WM_PAINT output through its client DC, without PrintWindow repainting it.
internal static class DamageCanvasVerification
{
    [DllImport("user32.dll")]static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")]static extern bool BitBlt(nint destination,int x,int y,int w,int h,nint source,int sx,int sy,uint rop);
    [DllImport("gdi32.dll")]static extern bool GdiFlush();
    static Bitmap Read(InputCanvas canvas)
    {
        var bitmap=new Bitmap(canvas.Width,canvas.Height,PixelFormat.Format32bppRgb);
        using(var g=Graphics.FromImage(bitmap)){nint dest=g.GetHdc(),source=GetDC(canvas.Handle);try{Native.Check(source!=0&&BitBlt(dest,0,0,bitmap.Width,bitmap.Height,source,0,0,0xCC0020),"Read painted private canvas DC");Native.Check(GdiFlush(),"Flush painted canvas read");}finally{if(source!=0)ReleaseDC(canvas.Handle,source);g.ReleaseHdc(dest);}}
        return bitmap;
    }
    internal static int Fixture(string output,bool split=false)
    {
        WindowCaptureResearch.PrivateOnly();ApplicationConfiguration.Initialize();
        using var form=new Form{Text="VIAVW disposable damage paint fixture",ClientSize=new Size(700,400)};
        using var canvas=new InputCanvas{SplitPreviewClips=split,Dock=DockStyle.Fill,FitOversizedImages=true,BackColor=Color.FromArgb(40,40,40)};form.Controls.Add(canvas);canvas.Resize+=(_,_)=>canvas.Invalidate();
        var results=new List<object>();string? failure=null;int actualPaints=0;canvas.PaintTiming=(end,message)=>{if(end&&message==0xF)actualPaints++;};
        form.Shown+=(_,_)=>form.BeginInvoke(()=>{
            try
            {
                for(int trial=0;trial<10;trial++)
                {
                    var before=canvas.Image as Bitmap;var next=new Bitmap(trial==8?803:800,500,PixelFormat.Format32bppRgb);
                    using(var g=Graphics.FromImage(next)){g.Clear(Color.FromArgb(36,36,36));using var font=new Font("Consolas",16);g.DrawString("Ordinary editor text 012345",font,Brushes.White,40,50);if(trial%2!=0)g.FillRectangle(Brushes.White,320,70,12,16);if(trial==4)g.FillRectangle(Brushes.CornflowerBlue,610,400,180,85);if(trial==5)g.FillRectangle(Brushes.Orange,0,499,800,1);}
                    if(trial==7)form.ClientSize=new Size(731,419);
                    var damage=PreviewDamage.Compare(before,next);canvas.Image=next;before?.Dispose();
                    if(damage==null)canvas.Invalidate();else if(damage.Length!=0){using var region=new Region();region.MakeEmpty();var p=canvas.Presentation;foreach(var r in damage)region.Union(PreviewDamage.Scale(r,next.Size,new Size(p.DrawWidth,p.DrawHeight)));canvas.Invalidate(region);}
                    int prior=actualPaints;canvas.Update();using var partial=Read(canvas);
                    canvas.Invalidate();canvas.Update();using var full=Read(canvas);
                    int maximumDelta=0,differentPixels=0,lightPixels=0;
                    for(int y=0;y<full.Height;y++)for(int x=0;x<full.Width;x++){var a=partial.GetPixel(x,y);var b=full.GetPixel(x,y);int delta=Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));maximumDelta=Math.Max(maximumDelta,delta);if(delta!=0)differentPixels++;if(b.R>150&&b.G>150&&b.B>150)lightPixels++;}
                    bool passed=maximumDelta<=2&&lightPixels>100;
                    results.Add(new{trial,passed,maximumDelta,differentPixels,lightPixels,sourceRectangles=damage?.Length,partialPaints=actualPaints-prior-1,drawn=canvas.Presentation});
                    if(trial is 1 or 4 or 7){partial.Save(Path.Combine(output,$"painted-{trial}.png"),ImageFormat.Png);full.Save(Path.Combine(output,$"reference-{trial}.png"),ImageFormat.Png);}
                    if(!passed)throw new InvalidOperationException("Actual canvas output differs from full redraw at trial "+trial);
                }
            }
            catch(Exception error){failure=error.ToString();}
            finally{canvas.Image?.Dispose();canvas.Image=null;File.WriteAllText(Path.Combine(output,"canvas-result.json"),JsonSerializer.Serialize(new{passed=failure==null,failure,actualPaints,results},new JsonSerializerOptions{WriteIndented=true}));form.Close();}
        });Application.Run(form);return failure==null?0:1;
    }
    internal static int Run(string output,bool split=false)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var layer=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();string? failure=null;int[] owned=[];
        try{layer.LaunchApplication(Path.Combine(AppContext.BaseDirectory,"LayerHost.exe"),[split?"--fixture-split-canvas":"--fixture-damage-canvas",output],output);var clock=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,"canvas-result.json"))&&clock.ElapsedMilliseconds<30000)Thread.Sleep(25);if(!File.Exists(Path.Combine(output,"canvas-result.json")))throw new TimeoutException("Private canvas fixture deadline");}
        catch(Exception error){failure=error.ToString();}finally{owned=layer.GetProcessIds();layer.Dispose();}
        JsonElement? result=File.Exists(Path.Combine(output,"canvas-result.json"))?JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(output,"canvas-result.json"))):null;
        bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));var physical=monitor.Complete(owned);bool passed=failure==null&&cleanup&&physical.GetProperty("passed").GetBoolean()&&result?.GetProperty("passed").GetBoolean()==true;
        File.WriteAllText(Path.Combine(output,"canvas-verification.json"),JsonSerializer.Serialize(new{passed,failure,result,cleanup,owned,physical},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

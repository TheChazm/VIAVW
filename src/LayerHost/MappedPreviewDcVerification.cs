using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayerHost;

internal static class MappedPreviewDcVerification
{
    [DllImport("gdi32.dll")] static extern nint GetStockObject(int value);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")] static extern uint SetDCBrushColor(nint dc,uint color);
    [DllImport("gdi32.dll")] static extern bool PatBlt(nint dc,int x,int y,int width,int height,uint operation);
    [DllImport("user32.dll")] static extern uint GetGuiResources(nint process,uint flags);
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var checks=new Dictionary<string,bool>();var mappings=new HashSet<string>();string? failure=null;
        void Check(string name,bool passed){checks[name]=passed;if(!passed)throw new InvalidOperationException(name);Console.WriteLine("PASS "+name);}
        void Fill(nint dc,int x,int y,int width,int height,Color color)
        {SetDCBrushColor(dc,(uint)(color.R|(color.G<<8)|(color.B<<16)));Native.Check(PatBlt(dc,x,y,width,height,0x00F00021),"Fill mapped test pattern");}
        void Draw(nint dc,int width,int height,int seed)
        {
            SelectObject(dc,GetStockObject(18));
            Fill(dc,0,0,width,height,Color.FromArgb(seed%256,71,193));
            Fill(dc,1,2,width/2,height/2,Color.FromArgb(31,seed*7%256,91));
            Fill(dc,width-1,height-1,1,1,Color.FromArgb(200,131,19));
        }
        byte[] Hash(Bitmap image)
        {
            var bits=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{var bytes=new byte[image.Width*image.Height*4];Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);for(int i=3;i<bytes.Length;i+=4)bytes[i]=255;return SHA256.HashData(bytes);}
            finally{image.UnlockBits(bits);}
        }
        bool Matches(Bitmap image,int width,int height,int seed)
        {
            using var expected=new Bitmap(width,height);using(var g=Graphics.FromImage(expected))
            {g.Clear(Color.FromArgb(seed%256,71,193));using var brush=new SolidBrush(Color.FromArgb(31,seed*7%256,91));g.FillRectangle(brush,1,2,width/2,height/2);}
            expected.SetPixel(width-1,height-1,Color.FromArgb(200,131,19));
            return Hash(image).SequenceEqual(Hash(expected))&&image.GetPixel(0,0).A==255&&image.GetPixel(width-1,height-1).A==255;
        }
        bool Gone(string name){try{using var map=MemoryMappedFile.OpenExisting(name,MemoryMappedFileRights.Read);return false;}catch(FileNotFoundException){return true;}}
        // Warm GDI+ before comparing this process's GDI-object count.
        using(var warm=new Bitmap(2,2))warm.GetPixel(0,0);
        using(var warmReader=new SharedPreviewReader())using(var warmWriter=new SharedPreviewWriter())using(var warmBitmap=new Bitmap(2,2))
            warmWriter.RenderDc(warmReader.Channel,2,2,dc=>{WindowCaptureResearch.CopyBitmap(warmBitmap,dc,0,0);return 0;},[],Stopwatch.StartNew());
        using var process=Process.GetCurrentProcess();uint before=GetGuiResources(process.Handle,0),during=0,after=0;
        try
        {
            using(var reader=new SharedPreviewReader())using(var writer=new SharedPreviewWriter())
            {
                string previous="";int generation=0,step=0;
                foreach(var(w,h) in new[]{(127,93),(127,93),(41,27),(401,211),(39,19)})
                {
                    var frame=writer.RenderDc(reader.Channel,w,h,dc=>{Draw(dc,w,h,w);return 0;},[],Stopwatch.StartNew());mappings.Add(frame.Name);
                    using var image=reader.Read(frame);
                    Check("exact top-down RGB and opaque alpha step "+(++step),frame.Opaque&&Matches(image,w,h,w));
                    Check("bounded surface reuse or resize step "+step,writer.DcSurfaceGeneration==(step==2?generation:generation+1));generation=writer.DcSurfaceGeneration;
                    if(step==2||step==3||step==5)Check("retain mapping step "+step,frame.Name==previous);
                    if(step==4)Check("release replaced mapping",frame.Name!=previous&&Gone(previous));previous=frame.Name;
                }
                using(var popup=new Bitmap(17,11,PixelFormat.Format32bppArgb))
                {
                    using(var g=Graphics.FromImage(popup)){g.Clear(Color.DarkOrange);using var brush=new SolidBrush(Color.DarkGreen);g.FillRectangle(brush,3,2,8,5);}
                    var composite=writer.RenderDc(reader.Channel,39,19,dc=>{Draw(dc,39,19,9);WindowCaptureResearch.CopyBitmap(popup,dc,7,4);return 0;},[],Stopwatch.StartNew());
                    using(var image=reader.Read(composite))Check("ARGB popup pixels composite into retained DC",image.GetPixel(7,4).ToArgb()==Color.DarkOrange.ToArgb()&&image.GetPixel(10,6).ToArgb()==Color.DarkGreen.ToArgb()&&image.GetPixel(0,0).ToArgb()==Color.FromArgb(9,71,193).ToArgb());
                    composite=writer.RenderDc(reader.Channel,39,19,dc=>{Draw(dc,39,19,9);WindowCaptureResearch.CopyBitmap(popup,dc,-3,-2);WindowCaptureResearch.CopyBitmap(popup,dc,25,13);return 0;},[],Stopwatch.StartNew());
                    using(var image=reader.Read(composite))Check("popup clipping and multiple destinations preserve correct colors",image.GetPixel(0,0).ToArgb()==Color.DarkGreen.ToArgb()&&image.GetPixel(25,13).ToArgb()==Color.DarkOrange.ToArgb()&&image.GetPixel(28,15).ToArgb()==Color.DarkGreen.ToArgb());
                }
                generation=writer.DcSurfaceGeneration;
                byte[]? retainedHash=null;Bitmap? retained=null;
                try
                {
                    for(int i=0;i<200;i++)
                    {
                        var frame=writer.RenderDc(reader.Channel,39,19,dc=>{Draw(dc,39,19,i);return 0;},[],Stopwatch.StartNew());
                        using var image=reader.Read(frame);if(!Matches(image,39,19,i))throw new InvalidDataException("Stale mapped GDI pixels");
                        if(i==0){retained=(Bitmap)image.Clone();retainedHash=Hash(retained);}
                    }
                    Check("200 fresh frames reuse a single DC and DIB",writer.DcSurfaceGeneration==generation);
                    Check("published copies remain immutable",Hash(retained!).SequenceEqual(retainedHash!));
                }
                finally{retained?.Dispose();}
                bool rejected=Task.Run(()=>{try{writer.RenderDc(reader.Channel,39,19,_=>0,[],Stopwatch.StartNew());return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();
                Check("reject use from another capture thread",rejected);
                bool failed=false;try{writer.RenderDc(reader.Channel,39,19,dc=>{Draw(dc,39,19,17);throw new InvalidOperationException("Disposable callback failure");},[],Stopwatch.StartNew());}catch(InvalidOperationException){failed=true;}
                var recovered=writer.RenderDc(reader.Channel,39,19,dc=>{Draw(dc,39,19,25);return 0;},[],Stopwatch.StartNew());
                using(var image=reader.Read(recovered))Check("recover after failed drawing without replacing surface",failed&&Matches(image,39,19,25)&&writer.DcSurfaceGeneration==generation);
                var argb=writer.Render(reader.Channel,39,19,image=>{using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(113,200,131,19));return 0;},[],Stopwatch.StartNew());
                using(var image=reader.Read(argb))Check("ARGB rendering still preserves transparency beside retained DIB",!argb.Opaque&&image.GetPixel(0,0).A==113);
                for(int i=0;i<100;i++){int w=41+i%3,h=29+i%5;var frame=writer.RenderDc(reader.Channel,w,h,dc=>{Draw(dc,w,h,i);return 0;},[],Stopwatch.StartNew());mappings.Add(frame.Name);using var image=reader.Read(frame);if(!Matches(image,w,h,i))throw new InvalidDataException("Repeated resized DIB mismatch");}
                during=GetGuiResources(process.Handle,0);
                Check("repeated resize retains only bounded GDI objects",during<=before+4);
            }
            after=GetGuiResources(process.Handle,0);
            Check("all DC and DIB objects released",after==before);
            Check("all mapped sections released",mappings.All(Gone));
            for(int i=0;i<30;i++)using(var reader=new SharedPreviewReader())using(var writer=new SharedPreviewWriter())using(var popup=new Bitmap(17,11))
            {
                using(var g=Graphics.FromImage(popup))g.Clear(Color.DarkOrange);
                var frame=writer.RenderDc(reader.Channel,39,19,dc=>{WindowCaptureResearch.CopyBitmap(popup,dc,4,2);return 0;},[],Stopwatch.StartNew());
                mappings.Add(frame.Name);using var image=reader.Read(frame);
            }
            after=GetGuiResources(process.Handle,0);
            Check("30 independent popup surfaces release all GDI objects and maps",after==before&&mappings.All(Gone));
        }
        catch(Exception error){failure=error.ToString();Console.Error.WriteLine(failure);after=GetGuiResources(process.Handle,0);}
        File.WriteAllText(Path.Combine(output,"mapped-dc.json"),JsonSerializer.Serialize(new{passed=failure==null,checks,failure,gdiObjects=new{before,during,after},note="Deterministic pixel/alpha/resize/resource-lifetime mechanism checks. Actual installed-IDE compatibility is tested separately."},new JsonSerializerOptions{WriteIndented=true}));
        return failure==null?0:1;
    }
}

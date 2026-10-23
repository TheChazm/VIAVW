using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace LayerHost;
internal static class PreviewDamageVerification
{
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);var checks=new Dictionary<string,bool>();var samples=new List<object>();var comparisons=new List<object>();
        static Bitmap Copy(Bitmap bitmap)=>bitmap.Clone(new Rectangle(Point.Empty,bitmap.Size),PixelFormat.Format32bppRgb);
        bool Equivalent(Bitmap a,Bitmap b,string name)
        {
            int maximumDelta=0,differentPixels=0;
            var r=new Rectangle(Point.Empty,a.Size);var left=a.LockBits(r,ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            try{var right=b.LockBits(r,ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);try{unsafe{for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++){bool changed=false;for(int channel=0;channel<3;channel++){int delta=Math.Abs(*((byte*)left.Scan0+y*left.Stride+x*4+channel)-*((byte*)right.Scan0+y*right.Stride+x*4+channel));maximumDelta=Math.Max(maximumDelta,delta);changed|=delta!=0;}if(changed)differentPixels++;}}}finally{b.UnlockBits(right);}}finally{a.UnlockBits(left);}
            comparisons.Add(new{name,maximumDelta,differentPixels,exact=differentPixels==0});return maximumDelta<=2;
        }
        foreach(var dimensions in new[]{(31,19,23,13),(1920,1032,1598,859),(1919,1031,1598,859),(127,65,127,65)})
        {
            var (w,h,dw,dh)=dimensions;using var before=new Bitmap(w,h,PixelFormat.Format32bppRgb);
            using(var g=Graphics.FromImage(before)){g.Clear(Color.FromArgb(35,35,35));using var font=new Font("Consolas",10);g.DrawString("Text, menu, caret and edge clipping",font,Brushes.White,4,4);}
            using var partial=new Bitmap(dw,dh,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(partial))g.DrawImage(before,new Rectangle(0,0,dw,dh));
            checks[$"{w}x{h}-identical"]=PreviewDamage.Compare(before,before)?.Length==0; // Separate locks are required below; identical object handled directly.
            for(int trial=0;trial<8;trial++)
            {
                using var after=Copy(before);
                using(var g=Graphics.FromImage(after))
                {
                    var color=trial%2==0?Brushes.White:Brushes.Black;
                    if(trial==7)g.Clear(Color.CornflowerBlue);
                    else{int x=trial==0?0:trial==1?w-1:(trial*61)%w,y=trial==2?h-1:(trial*29)%h;g.FillRectangle(color,x,y,Math.Min(17,w-x),Math.Min(14,h-y));if(trial==4)g.FillRectangle(Brushes.Orange,w-2,h-2,2,2);}
                }
                var damage=PreviewDamage.Compare(before,after)!;
                using(var g=Graphics.FromImage(partial))
                {
                    using var region=new Region();region.MakeEmpty();foreach(var rectangle in damage)region.Union(PreviewDamage.Scale(rectangle,after.Size,partial.Size));
                    g.SetClip(region,System.Drawing.Drawing2D.CombineMode.Replace);g.FillRectangle(Brushes.Black,new Rectangle(0,0,dw,dh));g.DrawImage(after,new Rectangle(0,0,dw,dh));
                }
                using var complete=new Bitmap(dw,dh,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(complete))g.DrawImage(after,new Rectangle(0,0,dw,dh));
                checks[$"{w}x{h}-edit-{trial}-output-within-two-rgb-levels"]=Equivalent(partial,complete,$"{w}x{h}-{trial}");
                if(!checks[$"{w}x{h}-edit-{trial}-output-within-two-rgb-levels"]){partial.Save(Path.Combine(output,$"failure-{w}-{trial}-partial.png"));complete.Save(Path.Combine(output,$"failure-{w}-{trial}-complete.png"));}
                using(var g=Graphics.FromImage(before))g.DrawImageUnscaled(after,0,0);
            }
        }
        using var alpha=new Bitmap(10,10,PixelFormat.Format32bppArgb);using var rgb=new Bitmap(10,10,PixelFormat.Format32bppRgb);using var other=new Bitmap(11,10,PixelFormat.Format32bppRgb);
        checks["alpha-falls-back"]=PreviewDamage.Compare(rgb,alpha)==null;checks["resize-falls-back"]=PreviewDamage.Compare(rgb,other)==null;checks["first-frame-falls-back"]=PreviewDamage.Compare(null,rgb)==null;
        using var a=new Bitmap(1920,1032,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(a))g.Clear(Color.FromArgb(36,36,36));using var b=Copy(a);using(var g=Graphics.FromImage(b))g.FillRectangle(Brushes.White,400,300,16,14);
        using var destination=new Bitmap(1598,859,PixelFormat.Format32bppRgb);using var paint=Graphics.FromImage(destination);
        for(int trial=-12;trial<80;trial++)foreach(bool incremental in trial%2==0?new[]{false,true}:new[]{true,false})
        {
            var start=Stopwatch.GetTimestamp();
            if(incremental){var damage=PreviewDamage.Compare(a,b)!;using var region=new Region();region.MakeEmpty();foreach(var rectangle in damage)region.Union(PreviewDamage.Scale(rectangle,b.Size,destination.Size));paint.SetClip(region,System.Drawing.Drawing2D.CombineMode.Replace);}
            else paint.ResetClip();
            paint.DrawImage(b,new Rectangle(Point.Empty,destination.Size));paint.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            if(trial>=0)samples.Add(new{trial,incremental,milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds});
        }
        bool passed=checks.Values.All(x=>x);File.WriteAllText(Path.Combine(output,"damage-verification.json"),JsonSerializer.Serialize(new{passed,checks,comparisons,samples,note="Full-output RGB comparison (two-level tolerance) across edge changes, scattered changes, resize fallback and fractional scaling. Microbenchmark includes complete frame comparison and clipped draw; excludes source capture and actual window presentation."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}


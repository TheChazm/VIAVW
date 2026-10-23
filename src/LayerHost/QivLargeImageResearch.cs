using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayerHost;

internal static class QivLargeImageResearch
{
    const int ImageWidth=4096,ImageHeight=3072;
    internal static object Prepare(string output,string ini)
    {
        string[] names=["01-quadrants.png","02-alternate.png","03-large.png","04-large.png"];
        Color[] colors=[Color.FromArgb(210,65,70),Color.FromArgb(60,190,90),Color.FromArgb(70,100,210),Color.FromArgb(185,60,185)];var files=new List<object>();
        for(int index=0;index<4;index++)
        {
            using var bitmap=new Bitmap(ImageWidth,ImageHeight,PixelFormat.Format24bppRgb);var data=bitmap.LockBits(new Rectangle(0,0,ImageWidth,ImageHeight),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
            try
            {
                byte[] tile=new byte[32*32*3];var random=new Random(1700+index);var color=colors[index];
                for(int p=0;p<tile.Length;p+=3){tile[p]=(byte)(color.B+random.Next(-20,21));tile[p+1]=(byte)(color.G+random.Next(-20,21));tile[p+2]=(byte)(color.R+random.Next(-20,21));}
                byte[] pixels=new byte[data.Stride*ImageHeight];for(int y=0;y<ImageHeight;y++)for(int x=0;x<ImageWidth;x+=32)Buffer.BlockCopy(tile,(y%32)*96,pixels,y*data.Stride+x*3,96);Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
            }
            finally{bitmap.UnlockBits(data);}
            using(var drawing=Graphics.FromImage(bitmap))for(int bit=0;bit<10;bit++){int code=((index+1)<<3)|5;using var brush=new SolidBrush((code&(1<<bit))!=0?Color.White:Color.Black);drawing.FillRectangle(brush,(float)((.18+.065*bit)*ImageWidth),(float)(.66*ImageHeight),(float)(.045*ImageWidth),(float)(.12*ImageHeight));}
            string file=Path.Combine(output,"images",names[index]);bitmap.Save(file,ImageFormat.Png);files.Add(new{file=Path.GetFileName(file),id=index+1,width=ImageWidth,height=ImageHeight,bytes=new FileInfo(file).Length,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))});
        }
        string config=File.ReadAllText(ini,Encoding.Unicode).Replace("qivVramCacheCount=2","qivVramCacheCount=0");File.WriteAllText(ini,config,Encoding.Unicode);
        long bytes=Directory.GetFiles(Path.Combine(output,"images")).Sum(f=>new FileInfo(f).Length);if(bytes>24*1024*1024)throw new InvalidOperationException("Disposable image budget exceeded.");
        var result=new{files,bytes,requestedVramCacheCount=0,effectiveCacheMinimumFromSource=1,preloadLookaside=1,note="Four bounded PNG documents. Far Home/End navigation is beyond adjacent preload; this does not flush or prove a cold OS file cache."};File.WriteAllText(Path.Combine(output,"media.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result;
    }
    internal static JsonElement Identity(byte[] png)
    {
        using var stream=new MemoryStream(png);using var image=new Bitmap(stream);double scale=Math.Min((double)image.Width/ImageWidth,(double)image.Height/ImageHeight),left=(image.Width-ImageWidth*scale)/2,top=(image.Height-ImageHeight*scale)/2;
        int code=0;bool valid=true;for(int bit=0;bit<10;bit++){int x=(int)(left+(.2+.065*bit)*ImageWidth*scale),y=(int)(top+.72*ImageHeight*scale);if(x<0||x>=image.Width||y<0||y>=image.Height){valid=false;continue;}var c=image.GetPixel(x,y);bool high=c.R>225&&c.G>225&&c.B>225,low=c.R<25&&c.G<25&&c.B<25;valid&=high||low;if(high)code|=1<<bit;}
        valid&=(code&7)==5;return JsonSerializer.SerializeToElement(new{valid,id=valid?code>>3:0,code,image.Width,image.Height});
    }
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);using var manager=new LayerManager();using var layer=manager.CreateLayer();using var monitor=new ResearchDesktopMonitor();
        QivCursorGuardSession? guard=null;GpuBridgeResearchSession? gpu=null;var checks=new List<object>();var samples=new List<JsonElement>();var costs=new List<object>();bool passed=true,graceful=false,last=false;string? failure=null;int pid=0;long root=0;int[] owned=[];string registry=QivResearch.RegistryState();
        void Check(string name,bool value,object? detail=null){passed&=value;checks.Add(new{name,passed=value,detail});Console.WriteLine((value?"PASS ":"FAIL ")+name);}
        (byte[] Png,object Metrics) Read(string mode,int settle)
        {
            if(mode=="gpu"){string file=Path.Combine(output,"scratch.png");var metrics=gpu!.Capture(file,true);return(File.ReadAllBytes(file),metrics);}
            var frame=layer.InspectHiddenWindow(root,true,settle);return(frame.Png,new{frame.CaptureMilliseconds,frame.NativeRenderingMilliseconds,frame.EncodingMilliseconds});
        }
        bool Matches(JsonElement identity,int expected)=>identity.GetProperty("valid").GetBoolean()&&identity.GetProperty("id").GetInt32()==expected;
        void Sample(string phase,string mode,int settle,int round)
        {
            last=!last;int expected=last?4:1;var clock=Stopwatch.StartNew();layer.SendKey(root,last?0x23:0x24);double inputMs=clock.Elapsed.TotalMilliseconds;
            var first=Read(mode,settle);double firstReturnMs=clock.Elapsed.TotalMilliseconds;var firstIdentity=Identity(first.Png);bool fresh=Matches(firstIdentity,expected);int attempts=1;var ready=first;var identity=firstIdentity;
            if(round==0||!fresh)File.WriteAllBytes(Path.Combine(output,$"{phase}-{mode}-{settle}-{round}-first.png"),first.Png);
            while(!Matches(identity,expected)&&clock.ElapsedMilliseconds<3000){Thread.Sleep(15);ready=Read(mode,settle);identity=Identity(ready.Png);attempts++;}
            bool readyVerified=Matches(identity,expected),hidden=!layer.GetWindows().First(w=>w.Handle==root).Visible;double readyMs=clock.Elapsed.TotalMilliseconds;
            if(round==0||!fresh)File.WriteAllBytes(Path.Combine(output,$"{phase}-{mode}-{settle}-{round}-ready.png"),ready.Png);
            samples.Add(JsonSerializer.SerializeToElement(new{phase,mode,settle,round,expected,inputMs,firstReturnMs,firstAttemptFresh=fresh,firstIdentity,firstMetrics=first.Metrics,readyVerified,readyMs,attempts,finalIdentity=identity,hidden}));
            Console.WriteLine($"{phase} {mode}/{settle} expected={expected} first={firstIdentity.GetProperty("id")} fresh={fresh} ready={readyVerified} attempts={attempts}");
            if(!readyVerified||!hidden)throw new InvalidOperationException("Image readiness deadline or private visibility restoration failed.");
        }
        void Phase(string name,(string Mode,int Settle)[] conditions)
        {
            int[] ids=layer.GetProcessIds();var before=ids.ToDictionary(id=>id,id=>{using var p=Process.GetProcessById(id);return p.TotalProcessorTime.TotalMilliseconds;});var clock=Stopwatch.StartNew();var engines=Task.Run(()=>GpuCounters.Sample(ids));
            for(int round=0;round<4;round++)for(int index=0;index<conditions.Length;index++){var c=conditions[(index+round)%conditions.Length];Sample(name,c.Mode,c.Settle,round);Thread.Sleep(50);}
            double ms=clock.Elapsed.TotalMilliseconds;var processes=ids.Select(id=>{using var p=Process.GetProcessById(id);double cpuMs=p.TotalProcessorTime.TotalMilliseconds-before[id];return new{pid=id,name=p.ProcessName,cpuMs,machinePercent=cpuMs/ms*100/Environment.ProcessorCount,privateMiB=p.PrivateMemorySize64/1048576.0};}).ToArray();costs.Add(new{name,ms,processes,engines=engines.GetAwaiter().GetResult(),note="Includes readiness retries and 50 ms pacing; supervisor generation/validation CPU excluded. Separate GPU engine interval, not whole-device utilization."});
        }
        try
        {
            string ini=QivResearch.Prepare(output,layer.Id);Prepare(output,ini);pid=layer.LaunchApplication(QivResearch.Executable,QivResearch.Arguments(output,ini),output);root=QivGuardResearch.WaitRoot(layer,pid);guard=new(layer,root);layer.HideLayer(root);
            var baseline=Stopwatch.StartNew();var initial=Read("native",0);while(!Matches(Identity(initial.Png),1)&&baseline.ElapsedMilliseconds<5000){Thread.Sleep(30);initial=Read("native",0);}Check("large native baseline without GPU setup",Matches(Identity(initial.Png),1)&&!guard.Setup.GetProperty("gpuBridgeLoaded").GetBoolean(),new{identity=Identity(initial.Png),guard.Setup});
            File.WriteAllBytes(Path.Combine(output,"initial-large.png"),initial.Png);if(!Matches(Identity(initial.Png),1))throw new InvalidOperationException("Unable to decode the initial large-image identity.");
            Phase("standalone-native",[("native",0),("native",30),("native",100)]);
            gpu=new(layer,root,ResearchGpuTarget.Qiv);Phase("with-gpu",[("native",0),("native",30),("gpu",0)]);
            int helper=gpu.HelperPid;gpu.StopAdapter();gpu.Dispose();gpu=null;Check("reader closes without losing standalone guard",!CompatibilityVerification.Alive(helper)&&guard.Verify().GetProperty("ok").GetBoolean());
            Check("all final images verified and hidden",samples.Count==24&&samples.All(s=>s.GetProperty("readyVerified").GetBoolean()&&s.GetProperty("hidden").GetBoolean()));
        }
        catch(Exception exception){passed=false;failure=exception.ToString();File.WriteAllText(Path.Combine(output,"failure.txt"),failure);}
        finally{gpu?.Dispose();if(!layer.Closed){owned=layer.GetProcessIds();try{graceful=layer.CloseGracefully(TimeSpan.FromSeconds(3));}catch{}}layer.Dispose();guard?.Dispose();Thread.Sleep(250);}
        bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id));var physical=monitor.Complete(owned);Check("registry and process cleanup",cleanup&&registry==QivResearch.RegistryState(),new{cleanup,graceful});Check("sampled physical desktop containment",physical.GetProperty("passed").GetBoolean(),physical);
        double Median(IEnumerable<double> source){var a=source.Order().ToArray();return(a[(a.Length-1)/2]+a[a.Length/2])/2;}
        var groups=samples.GroupBy(s=>(phase:s.GetProperty("phase").GetString(),mode:s.GetProperty("mode").GetString(),settle:s.GetProperty("settle").GetInt32())).Select(g=>new{g.Key.phase,g.Key.mode,g.Key.settle,count=g.Count(),firstFresh=g.Count(s=>s.GetProperty("firstAttemptFresh").GetBoolean()),verified=g.Count(s=>s.GetProperty("readyVerified").GetBoolean()),medianFirstMs=Median(g.Select(s=>s.GetProperty("firstReturnMs").GetDouble())),medianReadyMs=Median(g.Select(s=>s.GetProperty("readyMs").GetDouble())),maxAttempts=g.Max(s=>s.GetProperty("attempts").GetInt32())});
        File.WriteAllText(Path.Combine(output,"large-images.json"),JsonSerializer.Serialize(new{passed,failure,checks,groups,samples,costs,pid,root,owned,cleanup,graceful,note="Four 4096x3072 generated PNG documents with binary visual identity. Cache-constrained far Home/End input; no OS cache flushing or claim of a cold filesystem. All first attempts retained separately from bounded inspection-only retries. No production readiness heuristic is inferred from these known test markers. Physical human acceptance pending."},new JsonSerializerOptions{WriteIndented=true}));return passed?0:1;
    }
}

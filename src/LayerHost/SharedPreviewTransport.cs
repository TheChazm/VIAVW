using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;

namespace LayerHost;

// The authenticated, serialized layer RPC supplies the descriptor. Only the
// live preview uses this transient page-file mapping; exported captures stay PNG.
internal record SharedPreview(string Name, int Width, int Height, int Bytes, long Sequence, double Milliseconds, double NativeMilliseconds, PopupSurface[] Surfaces, bool Opaque=false,int? ClientX=null,int? ClientY=null);

internal sealed class SharedPreviewWriter : IDisposable
{
    internal const string Prefix = @"Local\VIAVW_Preview_";
    MemoryMappedFile? mapping;
    MemoryMappedViewAccessor? view;
    int capacity;
    string channel = "", name = "";
    long sequence;
    MappedPreviewDc? dcSurface;
    internal int DcSurfaceGeneration { get; private set; }

    internal static void ValidateChannel(string requestedChannel)
    {
        if (!requestedChannel.StartsWith(Prefix, StringComparison.Ordinal) || requestedChannel.Length != Prefix.Length + 32 || !Guid.TryParseExact(requestedChannel[Prefix.Length..], "N", out _)) throw new ArgumentException("Invalid private preview channel.");
    }
    void Ensure(string requestedChannel, int bytes)
    {
        ValidateChannel(requestedChannel);
        if (channel.Length != 0 && channel != requestedChannel) throw new InvalidOperationException("A worker has one private preview channel.");
        if (bytes is < 4 or > 80_000_000) throw new InvalidOperationException("Preview exceeds the capture size bound.");
        channel = requestedChannel;
        if (capacity >= bytes) return;
        dcSurface?.Dispose();dcSurface=null;
        view?.Dispose(); mapping?.Dispose(); view=null; mapping=null; capacity=0;
        name=channel+"_"+Guid.NewGuid().ToString("N");
        mapping=MemoryMappedFile.CreateNew(name,bytes,MemoryMappedFileAccess.ReadWrite);
        view=mapping.CreateViewAccessor(0,bytes,MemoryMappedFileAccess.ReadWrite);capacity=bytes;
    }

    // Render into the mapped pixels directly. The viewer still copies its own
    // immutable image before the next serialized request can overwrite this map.
    // The pointer-backed Bitmap never escapes this acquired-view lifetime.
    internal unsafe SharedPreview Render(string requestedChannel, int width, int height, Func<Bitmap,double> render, PopupSurface[] surfaces, System.Diagnostics.Stopwatch clock)
    {
        if(width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width*height>20_000_000)throw new InvalidOperationException("Capture size unsupported.");
        int bytes=checked(width*height*4);Ensure(requestedChannel,bytes);
        byte* destination=null;view!.SafeMemoryMappedViewHandle.AcquirePointer(ref destination);
        try
        {
            using var bitmap=new Bitmap(width,height,width*4,PixelFormat.Format32bppArgb,(nint)(destination+view.PointerOffset));
            double nativeMs=render(bitmap);
            return new(name,width,height,bytes,++sequence,clock.Elapsed.TotalMilliseconds,nativeMs,surfaces);
        }
        finally{view.SafeMemoryMappedViewHandle.ReleasePointer();}
    }

    // BI_RGB has no defined alpha channel. An opaque descriptor avoids an
    // additional whole-frame alpha rewrite, without changing ARGB transport.
    internal SharedPreview RenderDc(string requestedChannel,int width,int height,Func<nint,double> draw,PopupSurface[] surfaces,System.Diagnostics.Stopwatch clock)
    {
        if(width is < 1 or > 8192||height is < 1 or > 8192||(long)width*height>20_000_000)throw new InvalidOperationException("Capture size unsupported.");
        int bytes=checked(width*height*4);Ensure(requestedChannel,bytes);
        if(dcSurface==null||dcSurface.Width!=width||dcSurface.Height!=height)
        {
            dcSurface?.Dispose();dcSurface=null;
            dcSurface=new(mapping!.SafeMemoryMappedFileHandle,width,height);DcSurfaceGeneration++;
        }
        double nativeMs=dcSurface.Render(draw);
        return new(name,width,height,bytes,++sequence,clock.Elapsed.TotalMilliseconds,nativeMs,surfaces,Opaque:true);
    }

    internal unsafe SharedPreview Write(string requestedChannel, Bitmap bitmap, double nativeMilliseconds, PopupSurface[] surfaces, System.Diagnostics.Stopwatch clock)
    {
        int bytes = checked(bitmap.Width * bitmap.Height * 4);
        if (bitmap.Width > 8192 || bitmap.Height > 8192 || bytes > 80_000_000) throw new InvalidOperationException("Preview exceeds the capture size bound.");
        Ensure(requestedChannel,bytes);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride != bitmap.Width * 4) throw new InvalidOperationException("Unexpected preview pixel stride.");
            byte* destination = null;
            view!.SafeMemoryMappedViewHandle.AcquirePointer(ref destination);
            try { Buffer.MemoryCopy((void*)data.Scan0, destination + view.PointerOffset, capacity, bytes); }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
        }
        finally { bitmap.UnlockBits(data); }
        return new(name, bitmap.Width, bitmap.Height, bytes, ++sequence, clock.Elapsed.TotalMilliseconds, nativeMilliseconds, surfaces);
    }
    public void Dispose() { dcSurface?.Dispose();dcSurface=null;view?.Dispose(); view = null; mapping?.Dispose(); mapping = null; capacity = 0; }
}

internal sealed class SharedPreviewReader : IDisposable
{
    internal string Channel { get; } = SharedPreviewWriter.Prefix + Guid.NewGuid().ToString("N");
    MemoryMappedFile? mapping;
    MemoryMappedViewAccessor? view;
    string name = "";

    internal unsafe Bitmap Read(SharedPreview frame)
    {
        if (!frame.Name.StartsWith(Channel + "_", StringComparison.Ordinal) || frame.Name.Length != Channel.Length + 33 || !Guid.TryParseExact(frame.Name[(Channel.Length + 1)..], "N", out _) || frame.Width is < 1 or > 8192 || frame.Height is < 1 or > 8192 || (long)frame.Width * frame.Height > 20_000_000 || frame.Bytes != (long)frame.Width * frame.Height * 4 || frame.Sequence <= 0) throw new InvalidDataException("Invalid private preview descriptor.");
        if (frame.Name != name)
        {
            view?.Dispose(); mapping?.Dispose(); name = "";
            mapping = MemoryMappedFile.OpenExisting(frame.Name, MemoryMappedFileRights.Read);
            view = mapping.CreateViewAccessor(0, frame.Bytes, MemoryMappedFileAccess.Read);
            name = frame.Name;
        }
        if (view!.Capacity < frame.Bytes) throw new InvalidDataException("Private preview mapping is too short.");
        var format=frame.Opaque?PixelFormat.Format32bppRgb:PixelFormat.Format32bppArgb;
        var bitmap = new Bitmap(frame.Width, frame.Height, format);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, format);
            try
            {
                if (data.Stride != bitmap.Width * 4) throw new InvalidDataException("Unexpected destination pixel stride.");
                byte* source = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref source);
                try { Buffer.MemoryCopy(source + view.PointerOffset, (void*)data.Scan0, frame.Bytes, frame.Bytes); }
                finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    public void Dispose() { view?.Dispose(); view = null; mapping?.Dispose(); mapping = null; name = ""; }
}

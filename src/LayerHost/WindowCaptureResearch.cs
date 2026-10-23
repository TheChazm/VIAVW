using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LayerHost;

// Research only. Worker validates the HWND's job and desktop before entry.
internal static class WindowCaptureResearch
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RedrawWindow(nint hwnd, nint updateRect, nint region, uint flags);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("user32.dll", SetLastError = true)] static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("gdi32.dll")]static extern bool GdiFlush();
    internal static object MeasurePrivateDcCosts(nint window)
    {
        PrivateOnly();Native.Check(Native.GetClientRect(window,out var rect),"Measure owned source client");
        int width=rect.Right,height=rect.Bottom;if(width is <1 or >8192||height is <1 or >8192)throw new ArgumentException("Bound owned DC measurement");
        using var mapping=System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(null,checked((long)width*height*4));
        using var destination=new MappedPreviewDc(mapping.SafeMemoryMappedFileHandle,width,height);var samples=new List<object>();
        for(int trial=-4;trial<40;trial++)destination.Render(dc=>
        {
            var clock=Stopwatch.StartNew();nint source=GetDC(window);double acquired=clock.Elapsed.TotalMilliseconds;Native.Check(source!=0,"Measure owned source DC");double copied;
            try{Native.Check(BitBlt(dc,0,0,width,height,source,0,0,0xCC0020),"Measure owned source copy");copied=clock.Elapsed.TotalMilliseconds;}
            finally{ReleaseDC(window,source);}
            double released=clock.Elapsed.TotalMilliseconds;Native.Check(GdiFlush(),"Complete measured source copy");double flushed=clock.Elapsed.TotalMilliseconds;
            if(trial>=0)samples.Add(new{trial,getDcMilliseconds=acquired,copyMilliseconds=copied-acquired,releaseMilliseconds=released-copied,flushMilliseconds=flushed-released,totalMilliseconds=flushed});return flushed;
        });
        return new{width,height,samples,note="Forty warmed complete owned-window copies into the same mapped DIB implementation. Native stages include an explicit per-copy flush. No source DC is retained across frames and no source window is resized."};
    }
    internal static double CopyPrivateClient(nint window,nint destination,int width,int height)
    {
        PrivateOnly();var clock=Stopwatch.StartNew();
        nint source=GetDC(window);if(source==0)throw new InvalidOperationException("Owned private window DC unavailable.");
        try{Native.Check(BitBlt(destination,0,0,width,height,source,0,0,0x00CC0020),"Copy owned private client to mapped DIB");}
        finally{ReleaseDC(window,source);}
        return clock.Elapsed.TotalMilliseconds;
    }
    internal static void CopyBitmap(Bitmap bitmap,nint destination,int x,int y)
    {
        // Graphics.GetHdc on an ARGB bitmap is a drawing destination, not a
        // reliable readback DC for its existing pixels. Draw the bitmap into
        // the retained destination instead; reading that temporary DC produced
        // magenta popup rectangles while UIA actions still passed.
        using var graphics=Graphics.FromHdc(destination);
        graphics.DrawImageUnscaled(bitmap,x,y);
        graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
    }
    internal static void PrivateOnly()
    {
        string desktop = Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())), input = Native.InputDesktopName();
        if (!desktop.StartsWith("VIAVW_", StringComparison.Ordinal) || input == "unavailable" || desktop == input) throw new InvalidOperationException("Presentation research is confined to non-input private desktops.");
    }
    internal static double CopyPrivateClient(nint window,Bitmap bitmap)
    {
        PrivateOnly();var clock=Stopwatch.StartNew();
        using var graphics=Graphics.FromImage(bitmap);var destination=graphics.GetHdc();
        try
        {
            nint source=GetDC(window);if(source==0)throw new InvalidOperationException("Owned private window DC unavailable.");
            try{Native.Check(BitBlt(destination,0,0,bitmap.Width,bitmap.Height,source,0,0,0x00CC0020),"Copy owned private client pixels");}
            finally{ReleaseDC(window,source);}
        }
        finally{graphics.ReleaseHdc(destination);}
        return clock.Elapsed.TotalMilliseconds;
    }
    static void Style(nint hwnd, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0); nint previous = SetWindowLongPtrW(hwnd, index, value);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Owned research style");
    }
    static CaptureFrame Layered(nint hwnd)
    {
        PrivateOnly(); nint original = GetWindowLongPtrW(hwnd, -20);
        try
        {
            Style(hwnd, -20, (nint)((long)original | 0x80000)); bool accepted = ((long)GetWindowLongPtrW(hwnd, -20) & 0x80000) != 0;
            if (accepted) Native.Check(SetLayeredWindowAttributes(hwnd, 0, 255, 2), "Owned opaque layered window");
            Thread.Sleep(150); var frame = Capture(hwnd, "redraw-pw3");
            return frame with { Backend = "Research / external layered style " + (accepted ? "accepted" : "refused by application") };
        }
        finally { Style(hwnd, -20, original); }
    }
    static CaptureFrame Hosted(nint hwnd, bool layered)
    {
        PrivateOnly(); if (Native.GetParent(hwnd) != 0) throw new InvalidOperationException("Hosting research requires an unowned top-level window.");
        Native.Check(Native.GetWindowRect(hwnd, out var rect), "Original owned window bounds"); nint originalStyle = GetWindowLongPtrW(hwnd, -16);
        nint previousDpi = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd)); if (previousDpi == 0) throw new InvalidOperationException("Cannot match owned window DPI context.");
        nint host = 0;
        try
        {
            host = CreateWindowExW(layered ? 0x80000u : 0u, "STATIC", "VIAVW disposable presentation host", 0x90000000, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0, 0, 0, 0);
            if (host == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Private host window");
            if (layered) Native.Check(SetLayeredWindowAttributes(host, 0, 255, 2), "Opaque layered host");
            Style(hwnd, -16, (nint)(((long)originalStyle | 0x40000000) & ~0x80C40000L));
            Marshal.SetLastPInvokeError(0); SetParent(hwnd, host);
            if (Native.GetParent(hwnd) != host) throw new InvalidOperationException("Application window could not be hosted.");
            Native.Check(Native.SetWindowPos(hwnd, 0, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x4 | 0x10 | 0x20), "Private hosted layout");
            Thread.Sleep(300); var frame = Capture(host, "redraw-pw3");
            return frame with { Backend = "Research / " + (layered ? "layered" : "ordinary") + " private host" };
        }
        finally
        {
            try
            {
                if (Native.IsWindow(hwnd))
                {
                    SetParent(hwnd, 0); Style(hwnd, -16, originalStyle);
                    Native.Check(Native.SetWindowPos(hwnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x4 | 0x10 | 0x20), "Restore owned research window");
                }
                // Never destroy a parent while the application is still its child.
                if (host != 0 && (!Native.IsWindow(hwnd) || Native.GetParent(hwnd) != host)) DestroyWindow(host);
            }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
        }
    }

    internal static HiddenCaptureTrial Hidden(nint root, string strategy, string mode, int settle, nint target = 0)
    {
        PrivateOnly();
        if (Native.GetParent(root) != 0 || Native.IsWindowVisible(root)) throw new ArgumentException("Hidden capture research requires an initially hidden top-level source.");
        if (strategy is not ("framework" or "style" or "framework-style")) throw new ArgumentException("Unknown hidden capture strategy.");
        if (mode is not ("pw0" or "pw1" or "pw2" or "pw3" or "print-client" or "print-window" or "window-dc" or "redraw-pw3")) throw new ArgumentException("Unknown hidden capture mode.");
        if (settle is < 0 or > 500) throw new ArgumentException("Bound hidden capture settle to 0–500 ms.");
        if (target == 0) target = root; // Worker validates an explicit child's owned subtree.
        const long visibleBit = 0x10000000; long original = (long)GetWindowLongPtrW(root, -16);
        bool framework = strategy.Contains("framework"), style = strategy.Contains("style"), visibleBefore = false, visibleAfter = false;
        CaptureFrame? frame = null; string? error = null; var watch = Stopwatch.StartNew();
        try
        {
            if (style) Style(root, -16, (nint)(original | visibleBit));
            if (framework) { WindowActivationResearch.NotifyPositionVisibility(root, true); WindowActivationResearch.NotifyActivation(root); }
            Thread.Sleep(settle); visibleBefore = Native.IsWindowVisible(root);
            try { frame = Capture(target, mode); }
            catch (Exception exception) { error = exception.ToString(); }
            visibleAfter = Native.IsWindowVisible(root);
        }
        finally
        {
            try { if (framework && Native.IsWindow(root)) WindowActivationResearch.NotifyPositionVisibility(root, false); }
            finally
            {
                if (Native.IsWindow(root))
                {
                    // Restore only the visibility bit we changed, preserving unrelated
                    // style changes made by the application during the experiment.
                    if (style) Style(root, -16, (nint)(((long)GetWindowLongPtrW(root, -16) & ~visibleBit) | (original & visibleBit)));
                    if (Native.IsWindowVisible(root)) Native.ShowWindow(root, 0);
                }
            }
        }
        return new(strategy, mode, frame, error, visibleBefore, visibleAfter, Native.IsWindow(root) && !Native.IsWindowVisible(root), original, (long)GetWindowLongPtrW(root, -16), watch.Elapsed.TotalMilliseconds);
    }
    internal static CaptureFrame Capture(nint hwnd, string mode)
    {
        if (mode is "inspect-pw1" or "inspect-pw3")
            return Inspect(hwnd, mode == "inspect-pw3", 150);
        if (mode == "external-layered") return Layered(hwnd);
        if (mode is "host" or "layered-host") return Hosted(hwnd, mode == "layered-host");
        bool redraw = mode.StartsWith("redraw-", StringComparison.Ordinal);
        if (redraw) mode = mode[7..];
        if (mode is not ("pw0" or "pw1" or "pw2" or "pw3" or "print-client" or "print-window" or "window-dc")) throw new ArgumentException("Unknown capture experiment.");
        bool whole = mode is "pw0" or "pw2" or "print-window";
        Native.Check(whole ? Native.GetWindowRect(hwnd, out var rect) : Native.GetClientRect(hwnd, out rect), "Research capture bounds");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 20000000) throw new InvalidOperationException("Research capture size unsupported.");
        var watch = Stopwatch.StartNew();
        double? nativeMilliseconds = null;
        if (redraw) { Native.Check(RedrawWindow(hwnd, 0, 0, 0x1 | 0x4 | 0x80 | 0x100 | 0x400), "Redraw owned window"); Thread.Sleep(150); }
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Magenta); var dc = graphics.GetHdc();
            try
            {
                if (mode.StartsWith("pw")) { var nativeWatch = Stopwatch.StartNew(); Native.Check(Native.PrintWindow(hwnd, dc, (uint)(mode[2] - '0')), "Research PrintWindow"); nativeMilliseconds = nativeWatch.Elapsed.TotalMilliseconds; }
                else if (mode is "print-client" or "print-window") Native.Message(hwnd, mode == "print-client" ? 0x318u : 0x317u, (nuint)dc, mode == "print-client" ? 0x1C : 0x1E);
                else
                {
                    nint source = GetDC(hwnd); if (source == 0) throw new InvalidOperationException("Owned window DC unavailable.");
                    try { Native.Check(BitBlt(dc, 0, 0, width, height, source, 0, 0, 0x00CC0020), "Owned window BitBlt"); }
                    finally { ReleaseDC(hwnd, source); }
                }
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        using var stream = new MemoryStream(); var encodingWatch = Stopwatch.StartNew(); bitmap.Save(stream, ImageFormat.Png); byte[] bytes = stream.ToArray(); encodingWatch.Stop();
        return new(bytes, width, height, watch.Elapsed.TotalMilliseconds, "Research / " + (redraw ? "redraw-" : "") + mode, NativeRenderingMilliseconds: nativeMilliseconds, EncodingMilliseconds: encodingWatch.Elapsed.TotalMilliseconds);
    }
    internal static CaptureFrame Inspect(nint hwnd, bool fullContent, int settle)
    {
        PrivateOnly(); if (settle is < 0 or > 500) throw new ArgumentException("Bound private inspection settle to 0–500 ms.");
        bool hidden = !Native.IsWindowVisible(hwnd); var watch = Stopwatch.StartNew(); CaptureFrame frame;
        try
        {
            if (hidden) { Native.ShowWindow(hwnd, 4); Thread.Sleep(settle); }
            frame = Capture(hwnd, fullContent ? "pw3" : "pw1");
        }
        finally { if (hidden && Native.IsWindow(hwnd)) Native.ShowWindow(hwnd, 0); }
        return frame with { CaptureMilliseconds = watch.Elapsed.TotalMilliseconds, Backend = "Research / temporary private show, " + (fullContent ? "inspect-pw3" : "inspect-pw1") + ", settle " + settle + " ms, initially " + (hidden ? "hidden" : "visible") };
    }
}

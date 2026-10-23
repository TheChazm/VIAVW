using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Read-only sampled observations. Never restores or injects physical input.
internal sealed class ResearchDesktopMonitor : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Task sampling;
    readonly List<JsonElement> samples=[];
    readonly string desktop=Native.InputDesktopName();
    readonly Native.RECT clip=QivResearch.CursorClip();
    readonly nint foreground=Native.GetForegroundWindow();
    readonly uint? inputBefore=Native.LastInputTick();
    readonly Native.POINT pointer;
    internal ResearchDesktopMonitor()
    {
        Native.GetCursorPos(out pointer);var clock=Stopwatch.StartNew();
        sampling=Task.Run(()=>{string? previous=null;while(!stop.IsCancellationRequested){Native.GetCursorPos(out var p);var c=QivResearch.CursorClip();var fg=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(fg,out uint pid);var v=JsonSerializer.SerializeToElement(new {foreground=(long)fg,pid,p.X,p.Y,c.Left,c.Top,c.Right,c.Bottom,desktop=Native.InputDesktopName(),inputTick=Native.LastInputTick()});string text=v.GetRawText();if(text!=previous){samples.Add(JsonSerializer.SerializeToElement(new{ms=clock.Elapsed.TotalMilliseconds,value=v}));previous=text;}Thread.Sleep(25);}});
    }
    internal JsonElement Complete(int[] owned)
    {
        stop.Cancel();sampling.GetAwaiter().GetResult();Native.GetCursorPos(out var p);uint? inputAfter=Native.LastInputTick();
        bool pointerEquality=pointer.X==p.X&&pointer.Y==p.Y,focusEquality=foreground==Native.GetForegroundWindow(),quietInput=inputBefore!=null&&inputBefore==inputAfter;
        bool confinementStable=clip.Equals(QivResearch.CursorClip())&&samples.All(s=>{var v=s.GetProperty("value");return v.GetProperty("Left").GetInt32()==clip.Left&&v.GetProperty("Top").GetInt32()==clip.Top&&v.GetProperty("Right").GetInt32()==clip.Right&&v.GetProperty("Bottom").GetInt32()==clip.Bottom;});
        bool desktopStable=desktop==Native.InputDesktopName()&&samples.All(s=>s.GetProperty("value").GetProperty("desktop").GetString()==desktop);
        bool ownedNeverForeground=samples.All(s=>!owned.Contains(s.GetProperty("value").GetProperty("pid").GetInt32()));
        return JsonSerializer.SerializeToElement(new {passed=confinementStable&&desktopStable&&ownedNeverForeground&&(!quietInput||pointerEquality&&focusEquality),confinementStable,desktopStable,ownedNeverForeground,pointerEquality,focusEquality,quietInput,inputBefore,inputAfter,samples,note="Read-only 25 ms samples. Activity origin is not inferred. Simulated human senders do not establish physical human acceptance."});
    }
    public void Dispose(){stop.Cancel();sampling.GetAwaiter().GetResult();stop.Dispose();}
}

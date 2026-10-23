using Accessibility;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LayerHost;

// Passive point lookup rooted in an already validated owned HWND. Never calls
// AccessibleObjectFromPoint, changes focus, or invokes an accessible action.
internal static class ScopedAccessiblePoint
{
    [DllImport("oleacc.dll")]static extern int AccessibleObjectFromWindow(nint window,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object? result);
    [DllImport("oleacc.dll")]static extern int WindowFromAccessibleObject(IAccessible accessible,out nint window);
    internal static object Read(nint root,int[] allowed,int x,int y)
    {
        WindowCaptureResearch.PrivateOnly();var clock=Stopwatch.StartNew();var acquired=new List<IAccessible>();
        var point=new Native.POINT(x,y);Native.ClientToScreen(root,ref point);
        void Validate(IAccessible accessible)
        {
            Marshal.ThrowExceptionForHR(WindowFromAccessibleObject(accessible,out var window));
            if(window==0)throw new InvalidOperationException("Accessible object has no owned containing window.");
            uint thread=Native.GetWindowThreadProcessId(window,out uint pid);
            if(!allowed.Contains((int)pid)||Native.GetAncestor(window,2)!=Native.GetAncestor(root,2)||Native.ObjectName(Native.GetThreadDesktop(thread))!=Native.ObjectName(Native.GetThreadDesktop(Native.GetCurrentThreadId())))
                throw new InvalidOperationException("Accessible point escaped the validated owned root.");
        }
        try
        {
            var iid=typeof(IAccessible).GUID;Marshal.ThrowExceptionForHR(AccessibleObjectFromWindow(root,unchecked((uint)-4),ref iid,out var returned));
            var current=returned as IAccessible??throw new InvalidOperationException("No owned client accessibility interface.");acquired.Add(current);
            int child=0,depth=0;
            for(;depth<16;depth++)
            {
                Validate(current);object? hit=current.accHitTest(point.X,point.Y);
                if(hit is IAccessible descendant)
                {
                    acquired.Add(descendant);if(ReferenceEquals(descendant,current)){child=0;break;}current=descendant;continue;
                }
                if(hit is int index)
                {
                    child=index;if(index==0)break;
                    object? nested=current.get_accChild(index);
                    if(nested is IAccessible element){acquired.Add(element);current=element;continue;}
                    break;
                }
                return new{supported=false,depth,milliseconds=clock.Elapsed.TotalMilliseconds,note="Owned accessibility hit returned no child."};
            }
            Validate(current);current.accLocation(out int left,out int top,out int width,out int height,child);
            int role=Convert.ToInt32(current.get_accRole(child));int state=Convert.ToInt32(current.get_accState(child));
            string name=(current.get_accName(child)??"").Truncate(256);
            bool inside=width>0&&height>0&&point.X>=left&&point.Y>=top&&(long)point.X<((long)left+width)&&(long)point.Y<((long)top+height);
            return new{supported=depth<16&&role==42&&(state&(1|0x8000|0x10000))==0&&inside,role,state,name,depth,child,
                bounds=new{x=left,y=top,width,height},point=new{point.X,point.Y},milliseconds=clock.Elapsed.TotalMilliseconds,
                note="Read-only owned MSAA point classification. Only enabled visible text role is classified; no selection, focus or default action is performed."};
        }
        finally
        {
            // Release each marshalled acquisition after the entire query, including
            // repeated references to one RCW. No proxy is retained across requests.
            for(int i=acquired.Count-1;i>=0;i--)if(Marshal.IsComObject(acquired[i]))try{Marshal.ReleaseComObject(acquired[i]);}catch(InvalidComObjectException){}
        }
    }
}

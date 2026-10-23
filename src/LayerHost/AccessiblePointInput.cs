using System.Windows.Automation;

namespace LayerHost;

// Optional standard accessibility input, separate from passive observations.
// Only an already hit-tested, revalidated owned private control may act. A failed
// action is never replayed through native input because its effect is uncertain.
internal static class AccessiblePointInput
{
    internal static object Act(nint root,int[] allowed,int x,int y,bool focusBeforeAction=true)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        WindowCaptureResearch.PrivateOnly();
        var element=AccessibleObservation.PressElementAt(root,allowed,x,y);
        if(element==null)return new{performed=false,reason="No current eligible press control at this point"};
        var info=element.Current;Action? action=null;string? pattern=null;
        if(info.ControlType==ControlType.ComboBox&&element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var expansion))
        {
            var expand=(ExpandCollapsePattern)expansion;
            if(expand.Current.ExpandCollapseState==ExpandCollapseState.Collapsed){action=expand.Expand;pattern="ExpandCollapse.Expand";}
            else if(expand.Current.ExpandCollapseState==ExpandCollapseState.Expanded){action=expand.Collapse;pattern="ExpandCollapse.Collapse";}
        }
        else if(info.ControlType==ControlType.CheckBox&&element.TryGetCurrentPattern(TogglePattern.Pattern,out var toggle))
        {action=((TogglePattern)toggle).Toggle;pattern="Toggle";}
        else if((info.ControlType==ControlType.ListItem||info.ControlType==ControlType.RadioButton)&&element.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var select))
        {action=((SelectionItemPattern)select).Select;pattern="SelectionItem.Select";}
        else if(info.ControlType==ControlType.Button&&element.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
        {action=((InvokePattern)invoke).Invoke;pattern="Invoke";}
        else if(info.ControlType==ControlType.Button&&element.TryGetCurrentPattern(TogglePattern.Pattern,out var buttonToggle))
        {action=((TogglePattern)buttonToggle).Toggle;pattern="Button.Toggle";}
        if(action==null)return new{performed=false,reason="No supported standard input pattern"};
        string? focusError=null;
        double resolutionMilliseconds=clock.Elapsed.TotalMilliseconds;
        bool focusRequested=focusBeforeAction&&info.IsKeyboardFocusable;
        if(focusRequested)try{element.SetFocus();}catch(Exception e)when(e is InvalidOperationException or System.Runtime.InteropServices.COMException){focusError=e.Message;}
        double focusMilliseconds=clock.Elapsed.TotalMilliseconds-resolutionMilliseconds;
        action();
        return new{performed=true,pattern,name=info.Name,controlType=info.ControlType.ProgrammaticName,focusRequested,focusError,resolutionMilliseconds,focusMilliseconds,actionMilliseconds=clock.Elapsed.TotalMilliseconds-resolutionMilliseconds-focusMilliseconds,totalMilliseconds=clock.Elapsed.TotalMilliseconds,note="Standard accessibility input; not a native mouse packet or process instrumentation"};
    }
}

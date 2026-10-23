using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayerHost;

// Passive, bounded evidence from an already validated owned HWND. No value,
// selection or focus writes here; optional input actions live in AccessiblePointInput.
internal static class AccessibleObservation
{
    static bool PressControl(ControlType type)=>type==ControlType.Button||type==ControlType.ListItem||type==ControlType.CheckBox||type==ControlType.RadioButton||type==ControlType.ComboBox;
    static System.Windows.Rect PressBounds(ControlType type,System.Windows.Rect bounds)=>type==ControlType.ComboBox&&!bounds.IsEmpty&&bounds.Width>0?new(bounds.Right-Math.Min(24,bounds.Width),bounds.Y,Math.Min(24,bounds.Width),bounds.Height):bounds;
    // One candidate per serialized worker. Re-read its current geometry and
    // eligibility at each hit; never reuse a bare rectangle after layout changes.
    static (nint Root, nint Scope, AutomationElement Element)? pointerLeaf;
    internal static AutomationElement? PressElementAt(nint root,int[] allowed,int x,int y)
    {
        if(pointerLeaf is not {} candidate||candidate.Root!=root)return null;
        var origin=new Native.POINT(x,y);Native.ClientToScreen(root,ref origin);
        var info=candidate.Element.Current;
        return allowed.Contains(info.ProcessId)&&info.IsEnabled&&!info.IsOffscreen&&PressControl(info.ControlType)&&PressBounds(info.ControlType,info.BoundingRectangle).Contains(new System.Windows.Point(origin.X,origin.Y))?candidate.Element:null;
    }
    internal static object PointerGeometry(nint window, int[] allowed, MouseStep[]? points,string? preferredTextAutomationId=null,nint scopeWindow=0)
    {
        if (points == null || points.Length != 2) throw new ArgumentException("Two bounded pointer points are required.");
        var clock = Stopwatch.StartNew(); var origin = new Native.POINT(0, 0); Native.ClientToScreen(window, ref origin);
        var first = new System.Windows.Point(origin.X + points[0].X, origin.Y + points[0].Y);
        var last = new System.Windows.Point(origin.X + points[1].X, origin.Y + points[1].Y);
        var cache = new CacheRequest { TreeScope = TreeScope.Element | TreeScope.Children, TreeFilter = Automation.ControlViewCondition, AutomationElementMode = AutomationElementMode.Full };
        foreach (var property in new[] { AutomationElement.ProcessIdProperty, AutomationElement.BoundingRectangleProperty, AutomationElement.ControlTypeProperty }) cache.Add(property);
        var hitCache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        foreach (var property in new[] { AutomationElement.ProcessIdProperty, AutomationElement.BoundingRectangleProperty, AutomationElement.ControlTypeProperty, AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty, AutomationElement.IsTextPatternAvailableProperty }) hitCache.Add(property);
        var leafCache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        foreach (var property in new[] { AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty, AutomationElement.IsTextPatternAvailableProperty }) leafCache.Add(property);
        double clientPreparationMilliseconds=clock.Elapsed.TotalMilliseconds;
        double? rootLookupMilliseconds=null,hintQueryMilliseconds=null;
        var regions = new List<object>(); var presses = new List<object>(); int visited = 0;
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        var unbounded = new Stack<(AutomationElement Element, int Depth)>();
        bool cached = false,hintUsed=false;
        nint scope=scopeWindow==0?window:scopeWindow;
        object Result() => new { regions, pressRegions = presses, origin = new { origin.X, origin.Y }, milliseconds = clock.Elapsed.TotalMilliseconds,clientPreparationMilliseconds,rootLookupMilliseconds,hintQueryMilliseconds,scopeWindow=(long)scope, visited, cached,hintUsed, note = "Read-only owned subtree hit geometry. Candidates are revalidated with current bounds/eligibility. Native scope is validated by the worker; empty scoped results fall back to the root. No global ElementFromPoint or UIA input action." };
        if (pointerLeaf is { } retained && retained.Root == window && retained.Scope==scope)
        {
            try
            {
                var element = retained.Element.GetUpdatedCache(hitCache); var info = element.Cached; var bounds = info.BoundingRectangle;
                bool text = info.ControlType == ControlType.Edit && (bool)element.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
                bool press = PressControl(info.ControlType)&&PressBounds(info.ControlType,bounds).Contains(first)&&PressBounds(info.ControlType,bounds).Contains(last);
                if (allowed.Contains(info.ProcessId) && info.IsEnabled && !info.IsOffscreen && !bounds.IsEmpty && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Contains(first) && bounds.Contains(last) && (text || press))
                {
                    var hitBounds=text?bounds:PressBounds(info.ControlType,bounds);var geometry = new { hitBounds.X, hitBounds.Y, hitBounds.Width, hitBounds.Height };
                    if (text) regions.Add(geometry); else presses.Add(geometry);
                    cached = true; return Result();
                }
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        pointerLeaf = null;
        double rootStarted=clock.Elapsed.TotalMilliseconds;
        var rootElement=AutomationElement.FromHandle(scope);rootLookupMilliseconds=clock.Elapsed.TotalMilliseconds-rootStarted;
        if(scope!=window)
        {
            try
            {
                var scoped=rootElement.GetUpdatedCache(hitCache);var info=scoped.Cached;var bounds=info.BoundingRectangle;
                bool text=info.ControlType==ControlType.Edit&&(bool)scoped.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
                bool press=PressControl(info.ControlType);
                var eligible=press&&!text?PressBounds(info.ControlType,bounds):bounds;
                if(allowed.Contains(info.ProcessId)&&info.IsEnabled&&!info.IsOffscreen&&!bounds.IsEmpty&&double.IsFinite(bounds.X)&&double.IsFinite(bounds.Y)&&double.IsFinite(bounds.Width)&&double.IsFinite(bounds.Height)&&eligible.Contains(first)&&eligible.Contains(last)&&(text||press))
                {
                    var value=new{eligible.X,eligible.Y,eligible.Width,eligible.Height};
                    if(text)regions.Add(value);else presses.Add(value);
                    pointerLeaf=(window,scope,rootElement);return Result();
                }
            }
            catch(Exception error)when(error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException){}
        }
        if(preferredTextAutomationId!=null)
        {
            double hintStarted=clock.Elapsed.TotalMilliseconds;
            if(preferredTextAutomationId.Length is <1 or >256)throw new ArgumentException("Bound the optional text-control hint.");
            try
            {
                var preferred=rootElement.FindFirst(TreeScope.Descendants,new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty,preferredTextAutomationId),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty,false),
                    new PropertyCondition(AutomationElement.IsEnabledProperty,true)));
                if(preferred!=null)
                {
                    var element=preferred.GetUpdatedCache(hitCache);var info=element.Cached;var bounds=info.BoundingRectangle;
                    if(allowed.Contains(info.ProcessId)&&info.ControlType==ControlType.Edit&&info.IsEnabled&&!info.IsOffscreen&&(bool)element.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty)&&!bounds.IsEmpty&&double.IsFinite(bounds.X)&&double.IsFinite(bounds.Y)&&double.IsFinite(bounds.Width)&&double.IsFinite(bounds.Height)&&bounds.Contains(first)&&bounds.Contains(last))
                    {regions.Add(new{bounds.X,bounds.Y,bounds.Width,bounds.Height});pointerLeaf=(window,scope,preferred);hintUsed=true;hintQueryMilliseconds=clock.Elapsed.TotalMilliseconds-hintStarted;return Result();}
                }
            }
            catch(Exception error)when(error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException){ }
            hintQueryMilliseconds=clock.Elapsed.TotalMilliseconds-hintStarted;
        }
        pending.Push((rootElement,0));
        using (cache.Activate())
        {
            while (pending.Count != 0 || unbounded.Count != 0)
            {
                if (++visited > 160 || clock.ElapsedMilliseconds > 800) return TextGeometry(window, allowed);
                var (parent, depth) = pending.Count != 0 ? pending.Pop() : unbounded.Pop();
                var children = parent.GetUpdatedCache(cache).CachedChildren;
                if (children.Count > 320) return TextGeometry(window, allowed);
                foreach (AutomationElement child in children)
                {
                    var info = child.Cached; var bounds = info.BoundingRectangle;
                    if (!allowed.Contains(info.ProcessId)) continue;
                    bool finite = !bounds.IsEmpty && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0;
                    // A tab item's bounds describe its header; the selected
                    // document is a descendant outside that header rectangle.
                    if (finite && !bounds.Contains(first) && !bounds.Contains(last) && info.ControlType != ControlType.TabItem) continue;
                    bool edit = info.ControlType == ControlType.Edit;
                    bool press = PressControl(info.ControlType)&&finite&&PressBounds(info.ControlType,bounds).Contains(first)&&PressBounds(info.ControlType,bounds).Contains(last);
                    if (finite && bounds.Contains(first) && bounds.Contains(last) && (edit || press))
                    {
                        var leaf = child.GetUpdatedCache(leafCache);
                        bool text = edit && (bool)leaf.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
                        if (leaf.Cached.IsEnabled && !leaf.Cached.IsOffscreen && (text || press))
                        {
                            var hitBounds=text?bounds:PressBounds(info.ControlType,bounds);var geometry = new { hitBounds.X, hitBounds.Y, hitBounds.Width, hitBounds.Height };
                            if (text) regions.Add(geometry); else presses.Add(geometry);
                            pointerLeaf = (window,scope,child);
                            return Result();
                        }
                    }
                    if (depth < 24 && !press) { if (finite) pending.Push((child, depth + 1)); else unbounded.Push((child, depth + 1)); }
                }
            }
        }
        if(scope!=window&&regions.Count==0&&presses.Count==0)return PointerGeometry(window,allowed,points,preferredTextAutomationId);
        return Result();
    }
    internal static object TextGeometry(nint window, int[] allowed)
    {
        var watch = Stopwatch.StartNew();
        var root = AutomationElement.FromHandle(window);
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        foreach (var property in new[] { AutomationElement.ProcessIdProperty, AutomationElement.BoundingRectangleProperty, AutomationElement.IsOffscreenProperty, AutomationElement.IsEnabledProperty, AutomationElement.ControlTypeProperty }) cache.Add(property);
        var regions = new List<object>(); var pressRegions = new List<object>();
        // Fetch text/button/list geometry in one provider-side query. Do not
        // walk/read the document, selections, values or every menu on a click.
        using (cache.Activate())
        {
            var text = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
            var condition = new AndCondition(new OrCondition(text, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.RadioButton),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ComboBox)), new PropertyCondition(AutomationElement.IsOffscreenProperty, false), new PropertyCondition(AutomationElement.IsEnabledProperty, true));
            var elements = root.FindAll(TreeScope.Subtree, condition);
            if (elements.Count > 320) throw new InvalidOperationException("Too many text/button/list controls for bounded pointer geometry.");
            foreach (AutomationElement element in elements)
            {
                var info = element.Cached; var bounds = info.BoundingRectangle;
                if (allowed.Contains(info.ProcessId) && info.IsEnabled && !info.IsOffscreen && !bounds.IsEmpty && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))
                {
                    var hitBounds=PressControl(info.ControlType)?PressBounds(info.ControlType,bounds):bounds;var geometry = new { hitBounds.X, hitBounds.Y, hitBounds.Width, hitBounds.Height };
                    if (PressControl(info.ControlType)) pressRegions.Add(geometry); else regions.Add(geometry);
                }
            }
        }
        var origin = new Native.POINT(0, 0); Native.ClientToScreen(window, ref origin);
        return new { regions, pressRegions, origin = new { origin.X, origin.Y }, milliseconds = watch.Elapsed.TotalMilliseconds, note = "Read-only bulk geometry; no text, selection, focus or value writes. The normal worker deadline still bounds provider hangs." };
    }
    internal static object TextContainsPoints(nint window, int[] allowed, MouseStep[]? points)
    {
        if (points == null || points.Length != 2) throw new ArgumentException("Two bounded text-hit points are required.");
        var origin = new Native.POINT(0, 0); Native.ClientToScreen(window, ref origin);
        var first = new System.Windows.Point(origin.X + points[0].X, origin.Y + points[0].Y);
        var last = new System.Windows.Point(origin.X + points[1].X, origin.Y + points[1].Y);
        var watch = Stopwatch.StartNew(); int visited = 0;
        var pending = new Queue<(AutomationElement Element, int Depth)>(); pending.Enqueue((AutomationElement.FromHandle(window), 0));
        while (pending.Count > 0 && visited < 200 && watch.ElapsedMilliseconds < 1500)
        {
            var (element, depth) = pending.Dequeue(); visited++;
            try
            {
                var info = element.Current;
                if (!allowed.Contains(info.ProcessId)) continue;
                if (!info.IsOffscreen && info.IsEnabled && info.ControlType == ControlType.Edit && info.BoundingRectangle.Contains(first) && info.BoundingRectangle.Contains(last) && element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                    return new { supported = true, name = LimitText(info.Name), automationId = LimitText(info.AutomationId), visited, milliseconds = watch.Elapsed.TotalMilliseconds, note = "Read-only text-control geometry; selection is performed with native anchor and Shift-click messages." };
                if (depth >= 16) continue;
                for (var child = TreeWalker.ControlViewWalker.GetFirstChild(element); child != null && pending.Count < 400; child = TreeWalker.ControlViewWalker.GetNextSibling(child)) pending.Enqueue((child, depth + 1));
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        return new { supported = false, visited, milliseconds = watch.Elapsed.TotalMilliseconds };
    }
    static string LimitText(string? text) => (text ?? "").Truncate(256);
    internal static object Read(nint window, int[] allowed, string? automationId = null,bool menuItems = false, Action<object>? progress = null,bool allVisibleMatches=false)
    {
        var watch = Stopwatch.StartNew(); var nodes = new List<object>(); var errors = new List<string>();
        var walker = TreeWalker.ControlViewWalker; bool truncated = false;
        var pending = new Queue<(AutomationElement Element, int Depth)>();
        string Limit(string? value, int count) => (value ?? "").Truncate(count);
        double? Finite(double value) => double.IsFinite(value) ? value : null;
        void ReadNode(AutomationElement element, int depth)
        {
            if (nodes.Count >= 200 || depth > 16 || watch.ElapsedMilliseconds > 3500) { truncated = true; return; }
            try
            {
                var info = element.Current;
                if (!allowed.Contains(info.ProcessId)) return;
                string? value = null, text = null, visibleTextError = null; string[] selection = [], visibleText = []; bool[] selectionCoversDocument = [];
                int[] selectionStartLines = [];
                double? verticalScrollPercent = null;
                string? toggleState = null, expandCollapseState = null;
                try
                {
                if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern)) expandCollapseState=((ExpandCollapsePattern)expandPattern).Current.ExpandCollapseState.ToString();
                if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern)) toggleState = ((TogglePattern)togglePattern).Current.ToggleState.ToString();
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern)) value = Limit(((ValuePattern)valuePattern).Current.Value, 4096);
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
                {
                    var pattern = (TextPattern)textPattern; var document = pattern.DocumentRange; text = document.GetText(16384);
                    var ranges = pattern.GetSelection().Take(4).ToArray(); selection = ranges.Select(range => range.GetText(2048)).ToArray();
                    selectionCoversDocument = ranges.Select(range => range.CompareEndpoints(TextPatternRangeEndpoint.Start, document, TextPatternRangeEndpoint.Start) == 0 && range.CompareEndpoints(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.End) == 0).ToArray();
                    if(info.AutomationId=="WpfTextView")
                    try { selectionStartLines = ranges.Select(range => { var prefix=document.Clone();prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);return prefix.GetText(16384).Count(c=>c=='\n')+1; }).ToArray(); }
                    catch (Exception e) when (e is NotImplementedException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception) { visibleTextError=e.Message; }
                    try { visibleText = pattern.GetVisibleRanges().Take(3).Select(range => range.GetText(4096)).ToArray(); }
                    catch (NotImplementedException e) { visibleTextError = e.Message; }
                }
                if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollPattern)) verticalScrollPercent = Finite(((ScrollPattern)scrollPattern).Current.VerticalScrollPercent);
                }
                catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or NotImplementedException) { errors.Add(Limit(e.ToString(),2048)); }
                var bounds = info.BoundingRectangle;
                nodes.Add(new { depth, pid = info.ProcessId, type = info.ControlType.ProgrammaticName, name = Limit(info.Name, 512), id = Limit(info.AutomationId, 256), focused = info.HasKeyboardFocus, enabled=info.IsEnabled, acceleratorKey=Limit(info.AcceleratorKey,256),accessKey=Limit(info.AccessKey,256),offscreen = info.IsOffscreen, x = Finite(bounds.X), y = Finite(bounds.Y), width = Finite(bounds.Width), height = Finite(bounds.Height), value, text, selection, selectionStartLines, selectionCoversDocument, visibleText, visibleTextError, verticalScrollPercent, toggleState, expandCollapseState });
                if (automationId != null||menuItems) return;
                for (var child = walker.GetFirstChild(element); child != null; child = walker.GetNextSibling(child))
                {
                    if (pending.Count >= 400) { truncated = true; break; }
                    pending.Enqueue((child, depth + 1));
                }
            }
            catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or NotImplementedException) { errors.Add(Limit(e.ToString(), 2048)); }
        }
        var root = AutomationElement.FromHandle(window);
        if(menuItems)
        {
            var selected=root.FindAll(TreeScope.Subtree,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),new PropertyCondition(AutomationElement.IsOffscreenProperty,false)));
            truncated=selected.Count>128;
            foreach(AutomationElement match in selected.Cast<AutomationElement>().Take(128))pending.Enqueue((match,0));
        }
        else if (automationId == null) pending.Enqueue((root, 0));
        else
        {
            if (automationId.Length is 0 or > 256) throw new ArgumentException("Use a bounded automation ID.");
            var condition=new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,automationId),new PropertyCondition(AutomationElement.IsOffscreenProperty,false));
            if(allVisibleMatches)
            {
                // Explicit inspection needs both code and output editors. Keep
                // this larger provider query in the bounded disposable helper.
                var selected=root.FindAll(TreeScope.Subtree,condition);truncated=selected.Count>16;
                foreach(AutomationElement match in selected.Cast<AutomationElement>().Take(16))pending.Enqueue((match,0));
            }
            else
            {
                var selected=root.FindFirst(TreeScope.Subtree,condition);
                if(selected!=null)pending.Enqueue((selected,0));
            }
        }
        long published = -100;
        object Snapshot(bool incomplete) => new { nodes = nodes.ToArray(), errors = errors.ToArray(), truncated = incomplete || truncated, automationId, selectionPolicy = automationId == null ? "bounded subtree" : allVisibleMatches?"up to 16 visible automation ID matches":"first visible automation ID match", milliseconds = watch.Elapsed.TotalMilliseconds, note = "Read-only sampled owned subtree. Partial results are incomplete; provider calls can stall and are isolated by the inspection host." };
        while (pending.Count > 0 && nodes.Count < 200 && watch.ElapsedMilliseconds <= 3500)
        {
            var item = pending.Dequeue(); ReadNode(item.Element, item.Depth);
            if (progress != null && watch.ElapsedMilliseconds - published >= 25) { progress(Snapshot(true)); published = watch.ElapsedMilliseconds; }
        }
        truncated |= pending.Count > 0;
        return Snapshot(false);
    }
}

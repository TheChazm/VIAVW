namespace LayerHost;

// Work only from the layer's validated enumeration. Ownership is separate from
// child parentage; hidden utility windows are not usable modal destinations.
internal static class LayerWindowSelection
{
    internal static WindowInfo? ReplacementMain(IReadOnlyList<WindowInfo> windows, long original, int applicationPid)
    {
        if (windows.Any(w => w.Handle == original)) return null;
        var candidates = windows.Where(w => w.Parent == 0 && w.Owner == 0 && w.ProcessId == applicationPid &&
            w.Visible && w.Width > 100 && w.Height > 50 && w.ClassName != "#32768" && !string.IsNullOrWhiteSpace(w.Text)).ToArray();
        // A splash may disappear before the document exists. Wait rather than
        // guessing among documents or binding a helper/debuggee process.
        return candidates.Length == 1 ? candidates[0] : null;
    }
    internal static bool InSubtree(IReadOnlyList<WindowInfo> windows, long window, long root)
    {
        var seen = new HashSet<long>();
        for (int depth = 0; window != 0 && depth < 64 && seen.Add(window); depth++)
        {
            if (window == root) return true;
            window = windows.FirstOrDefault(w => w.Handle == window)?.Parent ?? 0;
        }
        return false;
    }
    static int OwnerDepth(IReadOnlyList<WindowInfo> windows, WindowInfo window, long root)
    {
        var seen = new HashSet<long>();
        for (int depth = 1; depth <= 64 && seen.Add(window.Handle); depth++)
        {
            if (window.Owner == root) return depth;
            var owner = windows.FirstOrDefault(w => w.Handle == window.Owner && w.Parent == 0);
            if (owner == null) break;
            window = owner;
        }
        return 0;
    }
    internal static bool OwnedBy(IReadOnlyList<WindowInfo> windows, WindowInfo window, long mainRoot) => window.Handle == mainRoot || OwnerDepth(windows, window, mainRoot) > 0;
    internal static WindowInfo? Select(IReadOnlyList<WindowInfo> windows, long mainRoot, long preferred = 0, long active = 0)
    {
        var main = windows.FirstOrDefault(w => w.Handle == mainRoot && w.Parent == 0 && w.Width > 100);
        if (main == null) return null;
        if (main.Enabled) return windows.FirstOrDefault(w => w.Handle == preferred && w.Parent == 0 && w.Visible && w.Enabled && OwnedBy(windows, w, mainRoot)) ?? main;
        var candidates = windows.Where(w => w.Parent == 0 && w.ClassName != "#32768" && w.Visible && w.Enabled && w.Width > 100 && w.Height > 50)
            .Select(w => (Window: w, Depth: OwnerDepth(windows, w, mainRoot))).Where(w => w.Depth > 0).ToArray();
        var activeModal = candidates.FirstOrDefault(w => w.Window.Handle == active);
        if (activeModal.Window != null) return activeModal.Window;
        if (candidates.Length == 0) return null;
        int deepest = candidates.Max(w => w.Depth);
        var selected = candidates.Where(w => w.Depth == deepest).ToArray();
        return selected.Length == 1 ? selected[0].Window : null; // Ambiguity requires inspection, not guessed input.
    }
}

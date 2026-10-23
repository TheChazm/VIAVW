using System.Runtime.InteropServices;
using System.Text;

namespace LayerHost;

internal static class OwnedMenuObservation
{
    [DllImport("user32.dll")] static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuStringW(nint menu, uint item, StringBuilder text, int count, uint flags);
    [DllImport("user32.dll")] static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] static extern bool GetMenuItemRect(nint window, nint menu, uint item, out Native.RECT rect);
    internal static object Read(nint window)
    {
        // Worker validates job and desktop before entering this read-only path.
        if (Native.ClassName(window) != "#32768") throw new ArgumentException("Menu observation requires an owned native menu window.");
        Native.Check(Native.SendMessageTimeoutW(window, 0x1E1, 0, 0, 2 | 0x20, 750, out nuint result) != 0 && result != 0, "Read owned MN_GETHMENU");
        nint menu = (nint)result; int count = GetMenuItemCount(menu);
        if (count is < 0 or > 64) throw new InvalidOperationException("Menu observation supports at most 64 items.");
        var items = new List<object>();
        for (uint index = 0; index < count; index++)
        {
            var text = new StringBuilder(256); GetMenuStringW(menu, index, text, text.Capacity, 0x400); uint flags = GetMenuState(menu, index, 0x400);
            bool hasBounds = GetMenuItemRect(0, menu, index, out var rect); var point = new Native.POINT(rect.Left, rect.Top); if (hasBounds) Native.ScreenToClient(window, ref point);
            items.Add(new { index, text = text.ToString(), flags, highlighted = (flags & 0x80) != 0, enabled = (flags & 3) == 0, separator = (flags & 0x800) != 0, hasBounds, x = point.X, y = point.Y, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top });
        }
        return new { window = (long)window, count, items };
    }
}

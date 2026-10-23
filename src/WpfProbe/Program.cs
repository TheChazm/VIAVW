using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WpfProbe;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--software")) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        new Application().Run(new ProbeWindow(args.Contains("--confirm-close"), args.Contains("--enable-grab-adapter"), args.Contains("--layered")));
    }
}
internal sealed class ProbeWindow : Window
{
    readonly TextBox editor = new() { AcceptsReturn = true, FontSize = 18 };
    readonly TextBox scrolling = new() { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Text = string.Join("\n", Enumerable.Range(1, 50).Select(i => "Scroll line " + i)) };
    readonly TextBlock status = new();
    readonly Marker marker = new();
    Button? counter;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly bool confirm;
    readonly bool adapter;
    int ticks, clicks, keyEvents, wheel, context, shortcuts, altShortcuts, drags;
    string dialog = "closed", lastKey = "", lastModifiers = "None";
    bool ctrlState, asyncCtrlState;
    int nativeMouseMessages;
    int printMessages, paintMessages;
    string lastSystemCommand = "none";
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    public ProbeWindow(bool confirm, bool adapter, bool layered)
    {
        this.confirm = confirm;
        this.adapter = adapter;
        Title = "Disposable WPF application"; Width = 636; Height = 479; Left = 40; Top = 40;
        if (layered) { WindowStyle = WindowStyle.None; AllowsTransparency = true; }
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(240, 243, 247)) };
        Content = canvas;
        void Add(UIElement element, double x, double y, double w, double h) { if (element is FrameworkElement f) { f.Width = w; f.Height = h; } Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
        Add(new TextBlock { Text = "Ordinary WPF controls and custom drawing", FontSize = 18 }, 24, 20, 570, 30);
        Add(editor, 24, 60, 570, 50);
        var button = new Button { Content = "Increment counter", IsDefault = true };
        counter = button;
        button.Click += (_, _) => { clicks++; marker.Changed = clicks % 2 != 0; marker.InvalidateVisual(); };
        Add(button, 24, 128, 200, 44);
        var modal = new Button { Content = "Open dialog" }; modal.Click += (_, _) => OpenDialog(); Add(modal, 24, 184, 200, 38);
        Add(marker, 420, 128, 150, 84);
        Add(scrolling, 24, 238, 350, 130);
        Add(status, 24, 390, 570, 45);
        PreviewKeyDown += (_, e) => { keyEvents++; lastKey = e.Key.ToString(); lastModifiers = Keyboard.Modifiers.ToString(); ctrlState = (GetKeyState(0x11) & 0x8000) != 0; asyncCtrlState = (GetAsyncKeyState(0x11) & 0x8000) != 0; };
        PreviewMouseWheel += (_, e) => wheel += e.Delta;
        marker.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) drags++; };
        var menu = new ContextMenu(); var action = new MenuItem { Header = "Mark context action" }; action.Click += (_, _) => context++; menu.Items.Add(action); editor.ContextMenu = menu;
        var command = new RoutedCommand(); InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control))); CommandBindings.Add(new CommandBinding(command, (_, _) => shortcuts++));
        var alt = new RoutedCommand(); InputBindings.Add(new KeyBinding(alt, new KeyGesture(Key.K, ModifierKeys.Alt))); CommandBindings.Add(new CommandBinding(alt, (_, _) => altShortcuts++));
        Loaded += (_, _) => { editor.Focus(); timer.Start(); Write(); };
        timer.Tick += (_, _) => { ticks++; Write(); };
        Closing += ClosingWindow;
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((nint hwnd, int msg, nint wp, nint lp, ref bool handled) =>
            { if (msg == 0x112) lastSystemCommand = "0x" + ((long)wp & 0xfff0).ToString("X"); if (msg is >= 0x200 and <= 0x20A) nativeMouseMessages++; if (msg is 0x317 or 0x318) printMessages++; if (msg == 0xF) paintMessages++; return 0; });
        };
    }
    void OpenDialog()
    {
        dialog = "open";
        var child = new Window { Title = "WPF modal review", Owner = this, Width = 320, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) }; child.Content = panel;
        panel.Children.Add(new TextBox { Text = "Modal state", Margin = new Thickness(0, 0, 0, 12) });
        var ok = new Button { Content = "Accept", IsDefault = true, Height = 32 }; ok.Click += (_, _) => child.DialogResult = true; panel.Children.Add(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Height = 32 }; panel.Children.Add(cancel);
        child.ShowDialog(); dialog = "closed";
    }
    void ClosingWindow(object? sender, CancelEventArgs e)
    {
        File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "closing-observation.json"), JsonSerializer.Serialize(new { lastSystemCommand, nativeMouseMessages, text = editor.Text }));
        if (!confirm || editor.Text.Length == 0) { timer.Stop(); return; }
        dialog = "save-prompt"; Write();
        var answer = MessageBox.Show(this, "Save the disposable document before closing?", "WPF save opportunity", MessageBoxButton.YesNoCancel);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; dialog = "closed"; }
        else { if (answer == MessageBoxResult.Yes) File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "saved-document.txt"), editor.Text); timer.Stop(); }
    }
    void Write()
    {
        string request = Path.Combine(Environment.CurrentDirectory, "grab.request");
        if (adapter && File.Exists(request))
        {
            File.Delete(request);
            var content = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string imagePath = Path.Combine(Environment.CurrentDirectory, "fixture-grab.png");
            using (var file = File.Create(imagePath + ".tmp")) encoder.Save(file);
            File.Move(imagePath + ".tmp", imagePath, true);
        }
        status.Text = $"ticks={ticks}; clicks={clicks}; keys={keyEvents}; wheel={wheel}; text={editor.Text}";
        string path = Path.Combine(Environment.CurrentDirectory, "observed.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { framework = "WPF", pid = Environment.ProcessId, ticks, clicks, keyEvents, wheel, context, shortcuts, altShortcuts, drags, dialog, lastKey, lastModifiers, ctrlState, asyncCtrlState, nativeMouseMessages, printMessages, paintMessages, lastSystemCommand, text = editor.Text, selection = editor.SelectedText, caret = editor.CaretIndex, focus = Keyboard.FocusedElement?.GetType().Name, focusName = Keyboard.FocusedElement == editor ? "editor" : Keyboard.FocusedElement == scrolling ? "scrolling" : "other", scrollOffset = scrolling.VerticalOffset, hovered = counter?.IsMouseOver, contextOpen = editor.ContextMenu?.IsOpen, renderingTier = RenderCapability.Tier >> 16, renderPreference = RenderOptions.ProcessRenderMode.ToString(), layered = AllowsTransparency }));
        try { File.Move(path + ".tmp", path, true); }
        catch (IOException) { } // Keep observation failures out of application input/rendering paths.
        catch (UnauthorizedAccessException) { }
    }
}
internal sealed class Marker : FrameworkElement
{
    internal bool Changed;
    protected override void OnRender(DrawingContext drawingContext)
    { drawingContext.DrawRectangle(new SolidColorBrush(Changed ? Color.FromRgb(242, 140, 40) : Color.FromRgb(40, 180, 100)), null, new Rect(0, 0, ActualWidth, ActualHeight)); }
}

using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// Ordinary installed IDE, disposable source, passive UIA observations. The
// matrix distinguishes receipt of USER messages from actual editor behavior.
internal static class VisualStudioPointerVerification
{
    internal static int Run(string output, string seedFile, string mode)
    {
        if (mode is not ("sync-prime" or "sync-no-prime" or "posted-prime" or "posted-no-prime")) throw new ArgumentException("Choose one isolated pointer variant.");
        foreach (var process in Process.GetProcessesByName("devenv"))
            using (process) throw new InvalidOperationException("An existing Visual Studio instance must not be reused.");
        var setup = VisualStudioPrivateVerification.Prepare(output, seedFile);
        const string original = "namespace ViavwDisposable { public static class Greeting { public const string Text = \"Pointer research\"; } }";
        File.WriteAllText(setup.First, original);
        var json = new JsonSerializerOptions { WriteIndented = true };
        var personal = VisualStudioResearch.PersonalSettings();
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var identities = new Dictionary<int, ProcessIdentity>(); var cases = new List<object>();
        long main = 0; string stage = "startup"; string? failure = null; int pid = 0;
        void Census() { foreach (var id in ProcessCensus.Read(layer.GetProcessIds())) identities[id.Pid] = id; }
        void Wait(Func<bool> condition, string reason, int timeout = 12000)
        { var clock = Stopwatch.StartNew(); do { Census(); if (condition()) return; Thread.Sleep(100); } while (clock.ElapsedMilliseconds < timeout); throw new TimeoutException(reason); }
        void Chord(params int[] keys)
        {
            long focus = layer.GetFocusedWindow(main);
            layer.SendQueueStateChord(focus == 0 ? main : focus, keys, posted: true, sender: layer.Owner, followFocusOnKeyUp: true, releaseStateBeforeKeyUp: true); Thread.Sleep(200);
        }
        JsonElement Observe(string name)
        { var state = layer.ObserveAccessible(main); File.WriteAllText(Path.Combine(output, name + ".json"), state.GetRawText()); return state; }
        JsonElement Editor(JsonElement state) => state.GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetString() == "WpfTextView" && n.GetProperty("text").GetString() == File.ReadAllText(setup.First));
        void Frame(string name) => File.WriteAllBytes(Path.Combine(output, name + ".png"), layer.CaptureVisualOutput(main, CaptureBackend.PrintWindowFullContent).Png);
        void Open()
        {
            Chord(0x11, 0x4F); WindowInfo? picker = null;
            Wait(() => (picker = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770" && w.Text == "Open File")) != null, "Open File dialog did not appear.");
            long field = layer.GetFocusedWindow(picker!.Handle);
            if (!layer.GetWindows().Any(w => w.Handle == field && w.ClassName.Contains("Edit", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("No focused filename field.");
            layer.SendQueueStateChord(field, [0x11, 0x41], posted: true, sender: layer.Owner, releaseStateBeforeKeyUp: true);
            layer.PostKeyboardInput(field, setup.First, layer.Owner); layer.PostKey(field, 0x0D, layer.Owner);
            Wait(() => !layer.GetWindows().Any(w => w.Handle == picker.Handle && w.Visible), "Open File dialog did not close."); Thread.Sleep(500);
            if (!Editor(Observe("opened")).GetProperty("focused").GetBoolean()) throw new InvalidOperationException("Opening source did not focus its editor.");
        }
        try
        {
            pid = layer.LaunchApplication(setup.Executable, setup.Arguments.Where(a => a != "/SafeMode").ToArray(), output);
            Wait(() => { main = layer.GetWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text.Contains("VIAVWResearch -", StringComparison.Ordinal))?.Handle ?? 0; return main != 0; }, "IDE did not open.", 90000);
            Thread.Sleep(2000); Open(); Frame("01-opened");
            layer.TakeHumanControl();
            foreach (var variant in new[] { (Name: "sync-prime", Prime: true, Posted: false), (Name: "sync-no-prime", Prime: false, Posted: false), (Name: "posted-prime", Prime: true, Posted: true), (Name: "posted-no-prime", Prime: false, Posted: true) }.Where(v => v.Name == mode))
            {
                stage = variant.Name; Chord(0x11, 0x41);
                var before = Observe(variant.Name + "-before"); var editor = Editor(before);
                if (!editor.GetProperty("selection").EnumerateArray().Any(s => s.GetString() == original)) throw new InvalidOperationException("Select-all precondition failed.");
                var origin = layer.ClientOrigin(main);
                int x = (int)editor.GetProperty("x").GetDouble() + 120 - origin.X, y = (int)editor.GetProperty("y").GetDouble() + 8 - origin.Y;
                var hit = layer.HitTest(main, x, y);
                bool rejected = false;
                try { layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y)], variant.Prime, posted: variant.Posted); } catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Wrong-owner mouse input was not rejected.");
                var clock = Stopwatch.StartNew();
                var delivery = layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move, hit.X, hit.Y), new(MouseMessage.LeftDown, hit.X, hit.Y, 1), new(MouseMessage.LeftUp, hit.X, hit.Y)], variant.Prime, ControlOwner.Human, variant.Posted);
                Thread.Sleep(350); var after = Observe(variant.Name + "-after-click"); Frame(variant.Name + "-after-click");
                var afterEditor = Editor(after); var selection = afterEditor.GetProperty("selection").EnumerateArray().Select(s => s.GetString()).ToArray();
                string marker = "/* " + variant.Name + " */";
                layer.PostKeyboardInput(layer.GetFocusedWindow(main), marker, ControlOwner.Human); Thread.Sleep(200); Chord(0x11, 0x53);
                Thread.Sleep(300); string saved = File.ReadAllText(setup.First); int offset = saved.IndexOf(marker, StringComparison.Ordinal);
                bool inserted = offset > 0 && saved.Remove(offset, marker.Length) == original;
                Observe(variant.Name + "-after-save");
                cases.Add(new { variant.Name, variant.Prime, variant.Posted, x, y, hit = new { hit.Window, hit.X, hit.Y }, delivery, milliseconds = clock.Elapsed.TotalMilliseconds, wrongOwnerRejected = rejected, editorFocused = afterEditor.GetProperty("focused").GetBoolean(), selection, insertionOffset = offset, preservedOriginal = inserted, saved });
                Console.WriteLine(variant.Name + ": caret insertion preserved source = " + inserted);
            }
            layer.ReturnToAutomation(); stage = "complete";
        }
        catch (Exception error)
        {
            failure = error.ToString(); Console.Error.WriteLine(failure);
            if (main != 0 && !layer.Closed) try { Observe("failure"); Frame("failure"); File.WriteAllText(Path.Combine(output, "failure-windows.json"), JsonSerializer.Serialize(layer.GetWindows(), json)); } catch (Exception) { }
        }
        finally
        {
            if (!layer.Closed) { Census(); try { layer.CloseGracefully(TimeSpan.FromSeconds(5)); } catch (Exception) { } }
            layer.Dispose(); Thread.Sleep(300);
        }
        var survivors = ProcessCensus.Read(identities.Keys.ToArray()).Where(now => identities.TryGetValue(now.Pid, out var old) && now.CreationFileTime == old.CreationFileTime).ToArray();
        var physical = monitor.Complete(identities.Keys.ToArray()); var afterSettings = VisualStudioResearch.PersonalSettings();
        bool settingsUnchanged = personal.Count == afterSettings.Count && personal.All(k => afterSettings.TryGetValue(k.Key, out string? value) && k.Value == value);
        bool completed = failure == null && cases.Count == 1 && survivors.Length == 0 && settingsUnchanged && physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output, "pointer-research.json"), JsonSerializer.Serialize(new { completed, stage, failure, pid, cases, identities = identities.Values, survivors, settingsUnchanged, physical, physicalHumanAcceptance = "user reported prior mouse failure; this run is automated", note = "Negative results are research evidence, not usable-pointer acceptance. No physical input, cursor movement, desktop switching, UIA writes or instrumentation." }, json));
        return completed ? 0 : 1;
    }
}

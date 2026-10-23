using System.Diagnostics;
using System.Text.Json;

namespace LayerHost;

// A separate, randomly named experimental profile permits disposable research
// while a person's existing IDE remains open. Never attach to that IDE/profile.
internal static class VisualStudioFreshProfile
{
    record Profile(string Suffix, string Solution, string Purpose);
    const string Purpose = "VIAVW disposable executable workflow v1";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static VisualStudioResearch.Setup Prepare(string output)
    {
        GpuBridgeVerification.PrepareOutput(output);
        var setup = VisualStudioResearch.Prepare(output);
        // Projects created later through the IDE under this disposable root
        // must not inherit VIAVW's own .NET 10 host build properties.
        File.WriteAllText(Path.Combine(output,"Directory.Build.props"),"<Project><PropertyGroup><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings></PropertyGroup></Project>");
        if (VisualStudioResearch.OutsideProfiles(setup.Suffix).Length != 0) throw new InvalidOperationException("Fresh profile already exists.");
        string project = Path.GetDirectoryName(setup.First)!;
        string file = Path.Combine(project, "DisposableProject.csproj");
        string xml = File.ReadAllText(file).Replace("<OutputType>Library</OutputType>", "<OutputType>WinExe</OutputType>")
            .Replace("<Reference Include=\"System\" />", "<Reference Include=\"System\" /><Reference Include=\"System.Drawing\" /><Reference Include=\"System.Windows.Forms\" /><Compile Include=\"Program.cs\" />");
        File.WriteAllText(file, xml);
        File.WriteAllText(Path.Combine(project, "Program.cs"), """
            using System;
            using System.Drawing;
            using System.Windows.Forms;
            namespace ViavwDisposable
            {
                internal static class Program
                {
                    [STAThread]
                    static void Main()
                    {
                        int startupValue = 40;
                        startupValue += 2; // Disposable breakpoint and stepping target.
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.Run(new DemoWindow(startupValue));
                    }
                }
                internal sealed class DemoWindow : Form
                {
                    int clicks;
                    internal DemoWindow(int answer)
                    {
                        Text = "VIAVW disposable running application";
                        ClientSize = new Size(700, 300);
                        var heading = new Label { Text = Greeting.Text + " | " + Details.Status + " | answer=" + answer, AutoSize = true, Location = new Point(20, 20) };
                        var editor = new TextBox { Name = "WorkflowText", Text = "Editable running application", Location = new Point(20, 70), Width = 620 };
                        var button = new Button { Name = "WorkflowButton", Text = "Count: 0", Location = new Point(20, 120), Width = 160 };
                        button.Click += delegate { button.Text = "Count: " + (++clicks); };
                        Controls.AddRange(new Control[] { heading, editor, button });
                    }
                }
            }
            """);
        File.WriteAllText(Path.Combine(output, "isolated-profile.json"), JsonSerializer.Serialize(new Profile(setup.Suffix, setup.Solution, Purpose), Json));
        return setup;
    }
    internal static VisualStudioResearch.Setup PrepareUsingProfile(string output,string seedFile)
    {
        var seed=JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(seedFile))!;VisualStudioResearch.ValidateSetup(seed,seedFile);
        string seedDirectory=Path.GetDirectoryName(seedFile)!;
        var marker=JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(seedDirectory,"isolated-profile.json")))!;
        if(marker!=new Profile(seed.Suffix,seed.Solution,Purpose)||!File.Exists(Path.Combine(seedDirectory,"software-profile-ready.json")))throw new ArgumentException("Use an owned, configured isolated software profile.");
        using var ready=JsonDocument.Parse(File.ReadAllText(Path.Combine(seedDirectory,"software-profile-ready.json")));
        if(ready.RootElement.GetProperty("Suffix").GetString()!=seed.Suffix||!ready.RootElement.GetProperty("configured").GetBoolean())throw new ArgumentException("Software-profile readiness does not match this experimental suffix.");
        using var lease=Lease(seed,seedFile);
        var setup=Prepare(output);
        setup=setup with{Suffix=seed.Suffix,Arguments=["/RootSuffix",seed.Suffix,"/SafeMode","/NoSplash","/Log",Path.Combine(output,"ActivityLog.xml"),setup.Solution]};
        File.WriteAllText(Path.Combine(output,"launch.json"),JsonSerializer.Serialize(setup,Json));
        File.WriteAllText(Path.Combine(output,"isolated-profile.json"),JsonSerializer.Serialize(new Profile(setup.Suffix,setup.Solution,Purpose),Json));
        File.Copy(Path.Combine(seedDirectory,"software-profile-ready.json"),Path.Combine(output,"software-profile-ready.json"));
        VisualStudioResearch.ValidateSetup(setup,Path.Combine(output,"launch.json"));return setup;
    }
    internal static IDisposable Lease(VisualStudioResearch.Setup setup, string setupFile)
    {
        string marker = Path.Combine(Path.GetDirectoryName(setupFile)!, "isolated-profile.json");
        if (!File.Exists(marker))
        {
            if (Process.GetProcessesByName("devenv").Length != 0) throw new InvalidOperationException("An existing Visual Studio instance must not be reused. Use a fresh isolated research profile for concurrent tests.");
            return new ActionLease(() => { });
        }
        var descriptor = JsonSerializer.Deserialize<Profile>(File.ReadAllText(marker))!;
        if (descriptor != new Profile(setup.Suffix, setup.Solution, Purpose)) throw new ArgumentException("Isolated profile marker does not match its validated setup.");
        // A named lease also covers separate viewer/configuration processes using
        // this suffix. Old accepted profiles never acquire isolated markers.
        var mutex = new Mutex(false, @"Local\VIAVW_VS_Profile_" + setup.Suffix);
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new InvalidOperationException("This experimental Visual Studio profile is already in use."); }
        return new ActionLease(() => { mutex.ReleaseMutex(); mutex.Dispose(); });
    }
    sealed class ActionLease(Action close) : IDisposable { public void Dispose() => close(); }

    internal static int Configure(string setupFile, string output)
    {
        var setup = JsonSerializer.Deserialize<VisualStudioResearch.Setup>(File.ReadAllText(setupFile))!;
        VisualStudioResearch.ValidateSetup(setup, setupFile);
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(setupFile)!, "isolated-profile.json"))) throw new ArgumentException("Only a fresh disposable profile can use this command.");
        GpuBridgeVerification.PrepareOutput(output);
        using var lease = Lease(setup, setupFile);
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        var personal = VisualStudioResearch.PersonalSettings(allowUnavailable: true); int[] owned = []; string? failure = null; bool graceful = false; var checks = new List<string>();
        void Wait(Func<bool> test, string reason, int timeout = 90000) { var timer = Stopwatch.StartNew(); do { if (test()) return; Thread.Sleep(100); } while (timer.ElapsedMilliseconds < timeout); throw new TimeoutException(reason); }
        void Click(long root, JsonElement node)
        {
            var origin = layer.ClientOrigin(root);
            var hit = layer.HitTest(root, (int)(node.GetProperty("x").GetDouble() + node.GetProperty("width").GetDouble()/2)-origin.X, (int)(node.GetProperty("y").GetDouble()+node.GetProperty("height").GetDouble()/2)-origin.Y);
            layer.SendQueueMouseSequence(hit.Window, [new(MouseMessage.Move,hit.X,hit.Y),new(MouseMessage.LeftDown,hit.X,hit.Y,1),new(MouseMessage.LeftUp,hit.X,hit.Y)], true); Thread.Sleep(250);
        }
        try
        {
            var arguments = setup.Arguments.Where(a => a != "/SafeMode").Concat(new[] { "/Command", "Tools.Options" }).ToArray();
            layer.LaunchApplication(setup.Executable, arguments, Path.GetDirectoryName(setupFile)!);
            WindowInfo? dialog = null;
            Wait(() => (dialog = layer.GetPresentationWindows().FirstOrDefault(w => w.Parent == 0 && w.Visible && w.Text == "Options")) != null, "Fresh experimental profile did not open Options.");
            foreach (string prefix in new[] { "Automatically adjust visual experience", "Use hardware graphics acceleration" })
            {
                JsonElement Setting() => layer.ObserveAccessible(dialog!.Handle).GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "ControlType.CheckBox" && n.GetProperty("name").GetString()!.StartsWith(prefix, StringComparison.Ordinal)).Clone();
                if (Setting().GetProperty("toggleState").GetString() == "On") Click(dialog!.Handle, Setting());
                if (Setting().GetProperty("toggleState").GetString() != "Off") throw new InvalidOperationException("Could not set " + prefix);
                checks.Add(prefix + " = Off through native private input");
            }
            File.WriteAllText(Path.Combine(output,"options.json"),layer.ObserveAccessible(dialog!.Handle).GetRawText());
            var ok = layer.ObserveAccessible(dialog.Handle).GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "ControlType.Button" && n.GetProperty("name").GetString() == "OK");
            Click(dialog.Handle,ok); Wait(() => !layer.GetPresentationWindows().Any(w => w.Handle == dialog.Handle && w.Visible), "Options did not close.",10000);
            Thread.Sleep(1500);
        }
        catch (Exception e) { failure=e.ToString(); Console.Error.WriteLine(e); }
        finally { if (!layer.Closed) { owned=layer.GetProcessIds(); try { graceful=layer.CloseGracefully(TimeSpan.FromSeconds(10)); } catch {} } layer.Dispose(); }
        var after=VisualStudioResearch.PersonalSettings(allowUnavailable: true); var physical=monitor.Complete(owned);
        bool cleanup=owned.All(id=>!CompatibilityVerification.Alive(id)); bool unchanged=personal.Count==after.Count&&personal.All(kv=>after.GetValueOrDefault(kv.Key)==kv.Value);
        bool passed=failure==null&&cleanup&&unchanged&&physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output,"profile-configuration.json"),JsonSerializer.Serialize(new {passed,failure,checks,graceful,cleanup,owned,readablePersonalSettingsUnchanged=unchanged,personalBefore=personal,personalAfter=after,unavailableSettings=personal.Where(kv=>kv.Value.StartsWith("unavailable:")).Select(kv=>kv.Key),physical,setup.Suffix},Json));
        if(passed)File.WriteAllText(Path.Combine(Path.GetDirectoryName(setupFile)!,"software-profile-ready.json"),JsonSerializer.Serialize(new{setup.Suffix,configured=true},Json));
        return passed?0:1;
    }
}

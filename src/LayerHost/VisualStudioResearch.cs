using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace LayerHost;

internal static class VisualStudioResearch
{
    internal const string Executable = @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe";
    internal record Setup(string Executable, string[] Arguments, Dictionary<string, string> Environment, string Solution, string First, string Second, string Suffix);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static Dictionary<string, string> ReadCompiledConstants(string path)
    {
        using var pe = new PEReader(new MemoryStream(SnapshotFiles.ReadBytes(path)));
        var reader = pe.GetMetadataReader(); var result = new Dictionary<string, string>();
        foreach (var handle in reader.FieldDefinitions)
        {
            var field = reader.GetFieldDefinition(handle); var value = field.GetDefaultValue(); if (value.IsNil) continue;
            var constant = reader.GetConstant(value); if (constant.TypeCode != ConstantTypeCode.String) continue;
            var blob = reader.GetBlobReader(constant.Value); result[reader.GetString(field.Name)] = blob.ReadUTF16(blob.RemainingBytes);
        }
        return result;
    }
    internal static void ValidateSetup(Setup setup, string setupFile)
    {
        string artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts")) + Path.DirectorySeparatorChar;
        bool Inside(string path) => Path.GetFullPath(path).StartsWith(artifacts, StringComparison.OrdinalIgnoreCase);
        if (!Inside(setupFile) || !Inside(setup.Solution) || !Inside(setup.First) || !Inside(setup.Second) || !string.Equals(setup.Executable, Executable, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(setup.Suffix, "^VIAVW[0-9a-f]{32}$")) throw new ArgumentException("Use the installed IDE and a controlled project-owned Visual Studio setup.");
        string[] expected = ["/RootSuffix", setup.Suffix, "/SafeMode", "/NoSplash", "/Log", Path.Combine(Path.GetDirectoryName(setupFile)!, "ActivityLog.xml"), setup.Solution];
        if (!setup.Arguments.SequenceEqual(expected) || setup.Environment.Count != 4 || setup.Environment.Any(kv => !new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" }.Contains(kv.Key) || !Inside(kv.Value))) throw new ArgumentException("The Visual Studio experiment requires its separate profile, Safe Mode and project-local child environment paths.");
    }
    internal static Setup Prepare(string output)
    {
        if (!File.Exists(Executable)) throw new FileNotFoundException("Installed Visual Studio 2022 was not found.", Executable);
        string project = Path.Combine(output, "DisposableProject"); Directory.CreateDirectory(project);
        // Keep this ordinary .NET Framework project independent of the wrapper's
        // repository-wide modern C# settings.
        File.WriteAllText(Path.Combine(project, "Directory.Build.props"), "<Project><PropertyGroup><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings></PropertyGroup></Project>");
        string first = Path.Combine(project, "Greeting.cs"), second = Path.Combine(project, "Details.cs");
        File.WriteAllText(first, "namespace ViavwDisposable\r\n{\r\n    public static class Greeting\r\n    {\r\n        public const string Text = \"Original greeting\";\r\n    }\r\n}\r\n", new UTF8Encoding(true));
        File.WriteAllText(second, "namespace ViavwDisposable\r\n{\r\n    public static class Details\r\n    {\r\n        public const string Status = \"Draft\";\r\n    }\r\n}\r\n", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(project, "DisposableProject.csproj"), """
            <Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><Configuration Condition="'$(Configuration)' == ''">Debug</Configuration><Platform Condition="'$(Platform)' == ''">AnyCPU</Platform><ProjectGuid>{074D955E-C7D3-43D4-AE87-0A32BCB89954}</ProjectGuid><OutputType>Library</OutputType><RootNamespace>ViavwDisposable</RootNamespace><AssemblyName>ViavwDisposable</AssemblyName><TargetFrameworkVersion>v4.8</TargetFrameworkVersion><OutputPath>bin\$(Configuration)\</OutputPath></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'"><DebugSymbols>true</DebugSymbols><DebugType>full</DebugType><Optimize>false</Optimize><OutputPath>bin\Debug\</OutputPath><DefineConstants>DEBUG;TRACE</DefineConstants><PlatformTarget>AnyCPU</PlatformTarget></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|AnyCPU'"><DebugType>pdbonly</DebugType><Optimize>true</Optimize><OutputPath>bin\Release\</OutputPath><DefineConstants>TRACE</DefineConstants><PlatformTarget>AnyCPU</PlatformTarget></PropertyGroup>
              <ItemGroup><Reference Include="System" /><Compile Include="Greeting.cs" /><Compile Include="Details.cs" /></ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);
        string solution = Path.Combine(output, "VIAVWResearch.sln");
        File.WriteAllText(solution, "Microsoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\nVisualStudioVersion = 17.14.36203.30\r\nMinimumVisualStudioVersion = 10.0.40219.1\r\nProject(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"DisposableProject\", \"DisposableProject\\DisposableProject.csproj\", \"{074D955E-C7D3-43D4-AE87-0A32BCB89954}\"\r\nEndProject\r\nGlobal\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n\t\tDebug|Any CPU = Debug|Any CPU\r\n\tEndGlobalSection\r\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n\t\t{074D955E-C7D3-43D4-AE87-0A32BCB89954}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{074D955E-C7D3-43D4-AE87-0A32BCB89954}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n\tEndGlobalSection\r\nEndGlobal\r\n");
        string suffix = "VIAVW" + Guid.NewGuid().ToString("N");
        var environment = new Dictionary<string, string>();
        foreach (string name in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" }) { string directory = Path.Combine(output, "profile", name); Directory.CreateDirectory(directory); environment[name] = directory; }
        var setup = new Setup(Executable, ["/RootSuffix", suffix, "/SafeMode", "/NoSplash", "/Log", Path.Combine(output, "ActivityLog.xml"), solution], environment, solution, first, second, suffix);
        File.WriteAllText(Path.Combine(output, "launch.json"), JsonSerializer.Serialize(setup, Json)); return setup;
    }

    internal static Dictionary<string, string> PersonalSettings(bool allowUnavailable = false)
    {
        var result = new Dictionary<string, string>();
        foreach (var area in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            string root = Path.Combine(Environment.GetFolderPath(area), "Microsoft", "VisualStudio", "17.0_427e2aaf");
            foreach (string name in new[] { "privateregistry.bin", "ApplicationPrivateSettings.xml", "Settings/CurrentSettings.vssettings" })
            {
                string path = Path.Combine(root, name); if (!File.Exists(path)) continue;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    result[path] = Convert.ToHexString(SHA256.HashData(stream));
                }
                catch (Exception e) when (allowUnavailable && e is IOException or UnauthorizedAccessException)
                {
                    // A concurrently running personal IDE may exclusively lock its
                    // hive. Record unavailable evidence without opening/bypassing it.
                    result[path] = "unavailable:" + e.GetType().Name;
                }
            }
        }
        return result;
    }
    internal static string[] OutsideProfiles(string suffix)
    {
        var found = new List<string>();
        foreach (var area in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            string root = Path.Combine(Environment.GetFolderPath(area), "Microsoft", "VisualStudio");
            if (Directory.Exists(root)) found.AddRange(Directory.EnumerateDirectories(root).Where(p => Path.GetFileName(p).Contains(suffix, StringComparison.OrdinalIgnoreCase)));
        }
        return found.ToArray();
    }
    internal static int Startup(string output, string? reuseSetupFile = null, string variant = "baseline")
    {
        if (!new[] { "baseline", "inherited-safe", "inherited-standard" }.Contains(variant) || variant != "baseline" && reuseSetupFile == null) throw new ArgumentException("Use a known Visual Studio comparison and a validated project-owned setup.");
        if (Process.GetProcessesByName("devenv").Length != 0) throw new InvalidOperationException("Close the existing IDE before a disposable Visual Studio comparison; no existing instance will be reused.");
        GpuBridgeVerification.PrepareOutput(output);
        var setup = reuseSetupFile == null ? Prepare(output) : JsonSerializer.Deserialize<Setup>(File.ReadAllText(reuseSetupFile))!;
        if (reuseSetupFile != null)
        {
            ValidateSetup(setup, reuseSetupFile);
            setup = setup with { Arguments = ["/RootSuffix", setup.Suffix, "/SafeMode", "/NoSplash", "/Log", Path.Combine(output, "ActivityLog.xml"), setup.Solution] };
            File.WriteAllText(Path.Combine(output, "launch.json"), JsonSerializer.Serialize(setup, Json));
        }
        ValidateSetup(setup, Path.Combine(output, "launch.json"));
        // Validate the unchanged, controlled input first; only these explicit research
        // variants can remove startup restrictions. Production defaults stay intact.
        if (variant != "baseline")
        {
            string[] arguments = variant switch
            {
                "inherited-safe" => setup.Arguments,
                "inherited-standard" => setup.Arguments.Where(a => a != "/SafeMode").ToArray(),
                _ => throw new ArgumentException("Unknown comparison.")
            };
            setup = setup with { Arguments = arguments, Environment = [] };
            File.WriteAllText(Path.Combine(output, "effective-launch.json"), JsonSerializer.Serialize(setup, Json));
        }
        var personal = PersonalSettings(); string binary = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable)));
        using var manager = new LayerManager(); using var layer = manager.CreateLayer(); using var monitor = new ResearchDesktopMonitor();
        int pid = 0; long main = 0; bool graceful = false, pickerOpened = false, codeOpened = false, editedAndSaved = false; string? failure = null; var observations = new List<object>(); var identities = new Dictionary<int, ProcessIdentity>(); var seen = new HashSet<string>(); var clock = Stopwatch.StartNew();
        try
        {
            pid = layer.LaunchApplication(setup.Executable, setup.Arguments, output, setup.Environment); Console.WriteLine("Started owned Visual Studio PID " + pid + " on " + layer.Id);
            while (clock.ElapsedMilliseconds < 90000)
            {
                foreach (var identity in ProcessCensus.Read(layer.GetProcessIds())) identities[identity.Pid] = identity;
                var windows = layer.GetWindows(); var roots = windows.Where(w => w.Parent == 0 && w.Visible && w.Width > 150 && w.Height > 80).ToArray();
                File.WriteAllText(Path.Combine(output, "latest-windows.json"), JsonSerializer.Serialize(windows, Json));
                foreach (var root in roots)
                {
                    string signature = root.Handle + ":" + root.Text; if (!seen.Add(signature)) continue;
                    string name = "window-" + seen.Count; Console.WriteLine(name + ": " + root.ClassName + " / " + root.Text);
                    var frame = layer.CaptureVisualOutput(root.Handle, CaptureBackend.PrintWindowFullContent); File.WriteAllBytes(Path.Combine(output, name + ".png"), frame.Png);
                    var accessible = layer.ObserveAccessible(root.Handle); File.WriteAllText(Path.Combine(output, name + "-accessible.json"), accessible.GetRawText());
                    observations.Add(new { ms = clock.Elapsed.TotalMilliseconds, root, frame.Width, frame.Height, frame.CaptureMilliseconds, frame.Backend, name });
                    if (root.Text.Contains("VIAVWResearch", StringComparison.Ordinal)) main = root.Handle;
                }
                if (main != 0) { Thread.Sleep(1500); var frame = layer.CaptureVisualOutput(main, CaptureBackend.PrintWindowFullContent); File.WriteAllBytes(Path.Combine(output, "main-settled.png"), frame.Png); File.WriteAllText(Path.Combine(output, "main-accessible.json"), layer.ObserveAccessible(main).GetRawText()); break; }
                if (!CompatibilityVerification.Alive(pid)) { failure = "Visual Studio exited before the disposable solution became ready."; break; }
                Thread.Sleep(500);
            }
            if (main == 0) failure ??= "The disposable solution did not become ready within 90 seconds; inspect startup windows and ActivityLog.";
            if (main != 0 && reuseSetupFile != null)
            {
                var windows = layer.GetWindows(); var warning = windows.FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770");
                if (warning != null)
                {
                    string observation = layer.ObserveAccessible(warning.Handle).GetRawText();
                    if (!observation.Contains("projects in the solution were not loaded correctly", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected startup dialog; not automatically accepted.");
                    var button = windows.Single(w => w.ClassName == "Button" && w.Text.Replace("&", "") == "OK" && LayerWindowSelection.InSubtree(windows, w.Handle, warning.Handle));
                    layer.SendMouseInput(button.Handle, 10, 10); Thread.Sleep(500); Console.WriteLine("Acknowledged owned project-load warning.");
                }
                layer.SendQueueMouseSequence(main, [new(MouseMessage.Move, 350, 130), new(MouseMessage.LeftDown, 350, 130, 1), new(MouseMessage.LeftUp, 350, 130)], primeCapture: true);
                layer.SendQueueStateChord(main, [0x11, 0x4F], posted: true, followFocusOnKeyUp: true); Thread.Sleep(800);
                windows = layer.GetWindows(); File.WriteAllText(Path.Combine(output, "open-attempt-windows.json"), JsonSerializer.Serialize(windows, Json));
                var picker = windows.FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770" && w.Text.Contains("Open", StringComparison.OrdinalIgnoreCase));
                pickerOpened = picker != null;
                if (picker == null) throw new InvalidOperationException("Native Ctrl+O did not open a file dialog after acknowledging the project warning.");
                File.WriteAllBytes(Path.Combine(output, "open-dialog.png"), layer.CaptureVisualOutput(picker.Handle, CaptureBackend.PrintWindowFullContent).Png);
                long field = layer.GetFocusedWindow(picker.Handle); var edit = windows.Single(w => w.Handle == field);
                if (!edit.ClassName.Contains("Edit", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Open dialog did not focus an ordinary filename field.");
                layer.SendQueueStateChord(field, [0x11, 0x41], posted: true, followFocusOnKeyUp: true); layer.SendKeyboardInput(field, setup.First); layer.PostKey(field, 0x0D); Thread.Sleep(1000);
                windows = layer.GetWindows(); File.WriteAllText(Path.Combine(output, "code-open-windows.json"), JsonSerializer.Serialize(windows, Json));
                var remaining = windows.FirstOrDefault(w => w.Parent == 0 && w.Visible && w.ClassName == "#32770");
                if (remaining != null) { File.WriteAllBytes(Path.Combine(output, "code-open-dialog.png"), layer.CaptureVisualOutput(remaining.Handle, CaptureBackend.PrintWindowFullContent).Png); File.WriteAllText(Path.Combine(output, "code-open-dialog-accessible.json"), layer.ObserveAccessible(remaining.Handle).GetRawText()); throw new InvalidOperationException("Opening the code file left a dialog; no editing was attempted."); }
                var accessible = layer.ObserveAccessible(main); File.WriteAllText(Path.Combine(output, "code-open-accessible.json"), accessible.GetRawText()); codeOpened = accessible.GetRawText().Contains("Greeting.cs", StringComparison.Ordinal);
                if (!codeOpened) throw new InvalidOperationException("The code document could not be verified in the ordinary IDE UI.");
                long focused = layer.GetFocusedWindow(main); layer.SendQueueStateChord(focused != 0 ? focused : main, [0x11, 0x41], posted: true, followFocusOnKeyUp: true); layer.SendKeyboardInput(focused != 0 ? focused : main, "// VIAVW disposable editor workflow"); layer.SendQueueStateChord(focused != 0 ? focused : main, [0x11, 0x53], posted: true, followFocusOnKeyUp: true); Thread.Sleep(800);
                editedAndSaved = File.ReadAllText(setup.First) == "// VIAVW disposable editor workflow";
                if (!editedAndSaved) throw new InvalidOperationException("Native editing/save did not produce the expected disposable code file.");
            }
        }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine(failure); }
        finally
        {
            if (!layer.Closed) { foreach (var identity in ProcessCensus.Read(layer.GetProcessIds())) identities[identity.Pid] = identity; try { graceful = layer.CloseGracefully(TimeSpan.FromSeconds(5)); } catch { } }
            layer.Dispose(); Thread.Sleep(250);
        }
        var physical = monitor.Complete(identities.Keys.ToArray()); var after = PersonalSettings();
        bool personalUnchanged = personal.Count == after.Count && personal.All(kv => after.TryGetValue(kv.Key, out var value) && value == kv.Value);
        var survivors = ProcessCensus.Read(identities.Keys.ToArray()).Where(now => identities.TryGetValue(now.Pid, out var old) && now.CreationFileTime == old.CreationFileTime).ToArray();
        bool passed = main != 0 && failure == null && survivors.Length == 0 && personalUnchanged && physical.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output, "visual-studio-startup.json"), JsonSerializer.Serialize(new { passed, failure, pid, main, setup, version = FileVersionInfo.GetVersionInfo(setup.Executable).FileVersion, binarySha256 = binary, binaryUnchanged = binary == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup.Executable))), elapsedMs = clock.Elapsed.TotalMilliseconds, observations, identities = identities.Values, graceful, survivors, personalUnchanged, personalBefore = personal, personalAfter = after, physical, outsideProfilePaths = OutsideProfiles(setup.Suffix), pickerOpened, codeOpened, editedAndSaved, workflowStatus = reuseSetupFile == null ? "Startup discovery only; editing workflow not tested." : editedAndSaved ? "Functional edit/save verified; visual usability must be assessed separately." : "Ordinary editing workflow incomplete.", instrumentation = "none" }, Json));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, failure, main, graceful, survivors = survivors.Length, output })); return passed ? 0 : 1;
    }
}

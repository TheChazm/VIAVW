using System.Reflection;
using System.Text.Json;

// Deliberately no namespace: this is the documented .NET startup-hook contract.
internal static class StartupHook
{
    static void Publish(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
    }
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("VIAVW_WPF_SOFTWARE_OPT_IN") != "1") return;
        string? expected = Environment.GetEnvironmentVariable("VIAVW_WPF_EXPECTED_EXE"), report = Environment.GetEnvironmentVariable("VIAVW_WPF_HOOK_REPORT");
        string project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!, "../../../../../"))) + Path.DirectorySeparatorChar;
        string artifacts = Path.Combine(project, "artifacts") + Path.DirectorySeparatorChar;
        // Process-only changes. Do not propagate this hook to the application's children.
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null, EnvironmentVariableTarget.Process);
        foreach (string name in new[] { "VIAVW_WPF_SOFTWARE_OPT_IN", "VIAVW_WPF_EXPECTED_EXE", "VIAVW_WPF_HOOK_REPORT" }) Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        if (Environment.GetEnvironmentVariable("VIAVW_WPF_DIAGNOSTIC_TRACE") == "1") foreach (string name in new[] { "VIAVW_WPF_DIAGNOSTIC_TRACE", "DOTNET_HOST_TRACE", "DOTNET_HOST_TRACEFILE" }) Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        if (expected == null || report == null) return;
        expected = Path.GetFullPath(expected); report = Path.GetFullPath(report);
        if (!expected.StartsWith(project, StringComparison.OrdinalIgnoreCase) || !report.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase)) return;
        bool selected = string.Equals(Environment.ProcessPath, expected, StringComparison.OrdinalIgnoreCase); bool applied = false; string? before = null, after = null, error = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { Directory.CreateDirectory(Path.GetDirectoryName(report)!); Publish(Path.ChangeExtension(report, ".progress.json"), new { phase = "entered", selected, pid = Environment.ProcessId, utc = DateTime.UtcNow }); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (selected)
        {
            try
            {
                // Public process preference, with no WPF dependency loaded into an unselected app.
                Type type = Type.GetType("System.Windows.Media.RenderOptions, PresentationCore", true)!;
                var property = type.GetProperty("ProcessRenderMode", BindingFlags.Public | BindingFlags.Static)!;
                before = property.GetValue(null)?.ToString(); property.SetValue(null, Enum.Parse(property.PropertyType, "SoftwareOnly"));
                after = property.GetValue(null)?.ToString(); applied = after == "SoftwareOnly";
            }
            catch (Exception e) { error = e.ToString(); } // Optional fallback must not crash the application.
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            Publish(report, new { selected, applied, before, after, error, pid = Environment.ProcessId, executable = Environment.ProcessPath, hookInheritanceCleared = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS") == null, milliseconds = watch.Elapsed.TotalMilliseconds, utc = DateTime.UtcNow });
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

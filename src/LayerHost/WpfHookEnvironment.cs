using System.Text.Json;

namespace LayerHost;

internal static class WpfHookEnvironment
{
    internal static async Task VerifyAsync(string directory, int pid)
    {
        string file = Path.Combine(directory, "hook.json"); var until = System.Diagnostics.Stopwatch.StartNew();
        double? Cpu() { try { using var process = System.Diagnostics.Process.GetProcessById(pid); return process.TotalProcessorTime.TotalMilliseconds; } catch (ArgumentException) { return null; } }
        double? beforeCpu = Cpu();
        while (!File.Exists(file) && until.ElapsedMilliseconds < 12000) { if (!CompatibilityVerification.Alive(pid)) break; await Task.Delay(50); }
        double? cpuMilliseconds = Cpu() - beforeCpu;
        File.WriteAllText(Path.Combine(directory, "hook-wait.json"), JsonSerializer.Serialize(new { pid, milliseconds = until.Elapsed.TotalMilliseconds, sourceCpuMilliseconds = cpuMilliseconds, wholeMachineCpuPercent = cpuMilliseconds / until.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, completed = File.Exists(file), sourceAlive = CompatibilityVerification.Alive(pid), note = "Bounded startup wait includes CLR initialization before the adapter is called, plus adapter initialization. Process-local read-only CPU observation." }));
        if (!File.Exists(file)) throw new TimeoutException("The requested WPF startup adapter did not report within the 12-second startup deadline. Inspect hook-request.json, hook-wait.json and optional host trace.");
        JsonElement result = await Task.Run(() => { using var report = JsonDocument.Parse(SnapshotFiles.ReadText(file)); return report.RootElement.Clone(); });
        if (result.GetProperty("pid").GetInt32() != pid || !result.GetProperty("selected").GetBoolean() || !result.GetProperty("applied").GetBoolean()) throw new InvalidOperationException("The requested WPF startup adapter did not apply. Inspect hook.json in this session's artifact directory.");
    }
    internal static Dictionary<string, string> Create(string executable, string directory)
    {
        string project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"))) + Path.DirectorySeparatorChar;
        executable = Path.GetFullPath(executable); directory = Path.GetFullPath(directory);
        if (!executable.StartsWith(project, StringComparison.OrdinalIgnoreCase) || !directory.StartsWith(Path.Combine(project, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The research WPF adapter is limited to disposable applications and artifacts within this project.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS"))) throw new InvalidOperationException("The research adapter will not replace existing startup hooks.");
        string runtimeConfig = Path.ChangeExtension(executable, ".runtimeconfig.json");
        using var config = JsonDocument.Parse(File.ReadAllText(runtimeConfig)); var runtime = config.RootElement.GetProperty("runtimeOptions");
        if (runtime.GetProperty("tfm").GetString() != "net10.0" || !runtime.GetProperty("frameworks").EnumerateArray().Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App")) throw new ArgumentException("This adapter is verified only for framework-dependent .NET 10 Windows Desktop applications.");
        string hook = Path.Combine(project, "src/WpfRenderHook/bin/Release/net10.0-windows/WpfRenderHook.dll"); if (!File.Exists(hook)) throw new FileNotFoundException("Build the WPF startup adapter first.", hook);
        var environment = new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = hook, ["VIAVW_WPF_SOFTWARE_OPT_IN"] = "1", ["VIAVW_WPF_EXPECTED_EXE"] = executable, ["VIAVW_WPF_HOOK_REPORT"] = Path.Combine(directory, "hook.json") };
        if (Environment.GetEnvironmentVariable("VIAVW_WPF_DIAGNOSTIC_TRACE") == "1") { environment["DOTNET_HOST_TRACE"] = "1"; environment["DOTNET_HOST_TRACEFILE"] = Path.Combine(directory, "dotnet-host-trace.txt"); }
        File.WriteAllText(Path.Combine(directory, "hook-request.json"), JsonSerializer.Serialize(new { executable, environment, note = "Explicit adapter variables only; inherited environment and secrets are not recorded." }));
        return environment;
    }
}

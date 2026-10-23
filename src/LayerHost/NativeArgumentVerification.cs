using System.Runtime.InteropServices;
using System.Text.Json;

namespace LayerHost;

internal static class NativeArgumentVerification
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint memory);
    internal static int Run(string output)
    {
        GpuBridgeVerification.PrepareOutput(output); var results = new List<object>(); bool passed = true;
        foreach (string[] arguments in new[]
        {
            new[] { @"C:\Program Files\Application\app.exe", "/RootSuffix", "VIAVW_test", "/SafeMode", @"C:\Test Project\test.sln" },
            new[] { @"C:\app.exe", "", "two words", "a\tb", "café Ω" },
            new[] { @"C:\app.exe", @"C:\directory with spaces\", @"C:\plain\", "embedded \"quote\"" },
            new[] { @"C:\app.exe", "one\\\"two", "two\\\\\"three", "trailing slash \\" },
            new[] { @"C:\app.exe", "--option=plain", "--option=two words", "literal&value", "literal;value" }
        })
        {
            string command = string.Join(" ", arguments.Select(Native.Quote)); nint argv = CommandLineToArgvW(command, out int count); Native.Check(argv != 0, "Parse research Windows command line");
            try
            {
                var actual = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))).ToArray(); bool exact = arguments.SequenceEqual(actual); passed &= exact;
                results.Add(new { passed = exact, arguments, actual, command });
            }
            finally { LocalFree(argv); }
        }
        File.WriteAllText(Path.Combine(output, "native-arguments.json"), JsonSerializer.Serialize(new { passed, results, note = "Windows parser round trips; no shell execution or application input. The Visual Studio experiment separately verifies native switch recognition through its Safe Mode title and separate settings profile." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, cases = results.Count })); return passed ? 0 : 1;
    }
}

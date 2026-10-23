// Copyright (c) 2026 The Coded Craftsman, LLC. MIT; see LICENSE.
using System.Collections.ObjectModel;
using System.Globalization;

namespace Viavw.Backends;

public sealed record LinuxOptions(string XvncExecutable, string PrivateRoot, string PasswordFile,
    int DisplayNumber = 100, int Width = 1280, int Height = 800,
    int StartupTimeoutMilliseconds = 5000, int StopTimeoutMilliseconds = 3000);

public sealed record CommandPlan(string Executable, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public static class LinuxLaunchPlan
{
    internal static string PosixPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Contains('\0') || path.Contains('\n') ||
            path.Contains('\r') || path.Contains('\\') || path.Length > 1024 ||
            path.Split('/').Skip(1).Any(p => p is "" or "." or ".."))
            throw new BackendException("invalid_path", "Use a canonical absolute POSIX path without dot segments or control characters.");
        return path;
    }

    public static void Validate(LinuxOptions options)
    {
        PosixPath(options.XvncExecutable); PosixPath(options.PrivateRoot); PosixPath(options.PasswordFile);
        if (System.Text.Encoding.UTF8.GetByteCount(options.PrivateRoot) > 56)
            throw new BackendException("invalid_path", "Private root must fit the Linux Unix-socket pathname budget (at most 56 UTF-8 bytes).");
        if (options.DisplayNumber is < 100 or > 999 || options.Width is < 320 or > 3840 ||
            options.Height is < 200 or > 2160 || options.StartupTimeoutMilliseconds is < 100 or > 30000 ||
            options.StopTimeoutMilliseconds is < 100 or > 10000)
            throw new BackendException("invalid_limits", "Display must be 100..999, geometry 320..3840 by 200..2160; startup 100..30000 ms and stop 100..10000 ms.");
    }

    internal static CommandPlan Server(LinuxOptions options, string directory)
    {
        Validate(options); PosixPath(directory);
        return new(options.XvncExecutable, Array.AsReadOnly(new[] {
            ":" + options.DisplayNumber.ToString(CultureInfo.InvariantCulture),
            "-geometry", $"{options.Width.ToString(CultureInfo.InvariantCulture)}x{options.Height.ToString(CultureInfo.InvariantCulture)}",
            "-depth", "24", "-auth", directory + "/Xauthority", "-nolisten", "tcp",
            "-rfbport", "-1", "-rfbunixpath", directory + "/rfb", "-rfbunixmode", "0600",
            "-SecurityTypes", "VncAuth", "-PasswordFile", options.PasswordFile,
            "-AcceptCutText=0", "-SendCutText=0", "-SendPrimary=0", "-SetPrimary=0",
            "-AcceptKeyEvents=0", "-AcceptPointerEvents=0", "-AcceptSetDesktopSize=0",
            "-AllowOverride=" }), new ReadOnlyDictionary<string, string>(new Dictionary<string, string> {
                ["PATH"] = "/usr/bin:/bin", ["LANG"] = "C.UTF-8" }));
    }

    // A plan only: no application is started by this foundation. The caller must
    // verify its isolated-instance profile and broker/single-instance behavior.
    public static CommandPlan Application(string executable, IEnumerable<string> arguments,
        string display, string authorityFile, string privateHome)
    {
        PosixPath(executable); PosixPath(authorityFile); PosixPath(privateHome);
        if (!System.Text.RegularExpressions.Regex.IsMatch(display, @"^:[1-9][0-9]{2}$"))
            throw new BackendException("invalid_display", "Only an explicit non-console private display is accepted.");
        var args = arguments.Take(129).ToArray();
        if (args.Length > 128 || args.Any(a => a is null || a.Length > 4096 || a.Contains('\0')) || args.Sum(a => a.Length) > 32768)
            throw new BackendException("argument_budget", "Arguments exceed the bounded launch contract.");
        return new(executable, Array.AsReadOnly(args), new ReadOnlyDictionary<string, string>(new Dictionary<string, string> {
            ["DISPLAY"] = display, ["XAUTHORITY"] = authorityFile, ["HOME"] = privateHome,
            ["PATH"] = "/usr/bin:/bin", ["LANG"] = "C.UTF-8", ["XDG_SESSION_TYPE"] = "x11" }));
    }
}

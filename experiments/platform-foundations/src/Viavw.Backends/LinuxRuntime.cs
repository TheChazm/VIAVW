// Copyright (c) 2026 The Coded Craftsman, LLC. MIT; see LICENSE.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Runtime.Versioning;

namespace Viavw.Backends;

internal sealed class LinuxRuntime : ILinuxRuntime
{
    public IReadOnlyList<string> Preflight(LinuxOptions options)
    {
        if (!OperatingSystem.IsLinux()) return ["target_os_required"];
        try
        {
            LinuxLaunchPlan.Validate(options);
            RequireRegular(options.XvncExecutable);
            var execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((File.GetUnixFileMode(options.XvncExecutable) & execute) == 0) return ["xvnc_not_executable"];
            RequirePrivate(options.PrivateRoot, true);
            RequirePrivate(options.PasswordFile, false);
            if (new FileInfo(options.PasswordFile).Length is < 8 or > 1024) return ["vnc_password_file_invalid"];
            if (File.Exists($"/tmp/.X{options.DisplayNumber}-lock") || File.Exists($"/tmp/.X11-unix/X{options.DisplayNumber}"))
                return ["display_in_use_or_stale_do_not_remove"];
            // Admission headroom, not a kernel memory/CPU sandbox.
            string? capacity = MemoryBlocker(File.ReadLines("/proc/meminfo"), options.Width, options.Height);
            if (capacity is not null) return [capacity];
            return [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BackendException)
        { return ["preflight_path_or_permission_failed"]; }
    }
    internal static string? MemoryBlocker(IEnumerable<string> lines, int width, int height)
    {
        var line = lines.FirstOrDefault(s => s.StartsWith("MemAvailable:", StringComparison.Ordinal));
        var fields = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields is not { Length: 3 } || fields[2] != "kB" ||
            !ulong.TryParse(fields[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong kib) ||
            kib > ulong.MaxValue / 1024) return "memory_headroom_unknown";
        ulong estimate = checked((ulong)width * (ulong)height * 16);
        return kib * 1024 < 512UL * 1024 * 1024 + estimate ? "machine_capacity" : null;
    }

    [SupportedOSPlatform("linux")]
    static void RequireRegular(string path)
    {
        RequireNoLinks(path);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new IOException("Required regular file unavailable.");
    }
    [SupportedOSPlatform("linux")]
    static void RequirePrivate(string path, bool directory)
    {
        RequireNoLinks(path);
        if (directory ? !Directory.Exists(path) : !File.Exists(path)) throw new IOException("Pre-provisioned path required.");
        var mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                     UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("Private path must deny group/other access.");
        if (directory && (mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) throw new IOException("Private root must be accessible to its owner.");
    }
    static void RequireNoLinks(string path)
    {
        for (string? current = path; current is not null && current != "/"; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlink paths are not accepted.");
    }

    public string Prepare(LinuxOptions options, string id, byte[] cookie)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        RequirePrivate(options.PrivateRoot, true);
        string directory = Path.Combine(options.PrivateRoot, "session-" + id);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Session path collision; never adopt.");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var file = new FileStream(directory + "/Xauthority", new FileStreamOptions {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            byte[] authority = X11Authentication.Authority(Environment.MachineName, options.DisplayNumber, cookie);
            try { file.Write(authority); } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(authority); }
            return directory;
        }
        catch (Exception error)
        {
            try { RemoveOwnedFiles(directory); }
            catch (Exception cleanupError) { throw new BackendException("prepare_cleanup_uncertain", $"Owned authority preparation failed; files at {directory} require reconciliation. Original error: {error.GetType().Name}.", OperationOutcome.Unknown, cleanupError); }
            throw;
        }
    }
    public IOwnedDisplay Start(CommandPlan plan)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var start = new ProcessStartInfo(plan.Executable) { UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear();
        foreach (var pair in plan.Environment) start.Environment.Add(pair.Key, pair.Value);
        foreach (var arg in plan.Arguments) start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("Xvnc did not start.");
            process.StandardInput.Close();
            // Drain without retaining unbounded logs or emitting secrets.
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            return new OwnedProcess(process);
        }
        catch (Exception error)
        {
            // Process.Start failure must not hide a child that did start.
            bool uncertain = false;
            try
            {
                if (process.Id > 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                    uncertain = !process.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { /* no associated process */ }
            catch { uncertain = true; }
            process.Dispose();
            if (uncertain) throw new BackendException("spawn_uncertain", "Xvnc spawn failed after possible launch; no automatic retry is safe.", OperationOutcome.Unknown, error);
            throw;
        }
    }
    public async Task AuthenticateAsync(LinuxOptions options, byte[] cookie, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        byte[] wrongCookie = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        try { await AuthenticateCoreAsync(options, cookie, wrongCookie, cancellationToken); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(wrongCookie); }
    }
    static async Task AuthenticateCoreAsync(LinuxOptions options, byte[] cookie, byte[] wrongCookie, CancellationToken cancellationToken)
    {
        // Retry read-only readiness probes only. Never retry process creation.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(250);
                // Positive authentication alone is insufficient if a different
                // display admits local clients without checking their cookie.
                if (await ProbeAsync(options.DisplayNumber, wrongCookie, attempt.Token))
                    throw new BackendException("display_auth_bypassed", "Display accepted an incorrect cookie; isolation readiness is unproven.");
                if (!await ProbeAsync(options.DisplayNumber, cookie, attempt.Token))
                    throw new BackendException("display_auth_failed", "Private cookie rejected; never adopt another display.");
                return;
            }
            catch (Exception error) when (error is SocketException or IOException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }
        }
    }
    static async Task<bool> ProbeAsync(int display, byte[] cookie, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint($"/tmp/.X11-unix/X{display}"), cancellationToken);
        using var stream = new NetworkStream(socket, false);
        byte[] setup = X11Authentication.Setup(cookie);
        try { await stream.WriteAsync(setup, cancellationToken); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(setup); }
        byte[] prefix = new byte[8]; await stream.ReadExactlyAsync(prefix, cancellationToken);
        if (!X11Authentication.Accepted(prefix)) return false;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(6)) * 4;
        byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, cancellationToken);
        return true;
    }
    public void RemoveOwnedFiles(string directory)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        RequirePrivate(directory, true);
        // No recursive deletion, wildcard, X lock removal or foreign PID cleanup.
        foreach (string name in new[] { "Xauthority", "rfb" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) { RequireNoLinks(path); File.Delete(path); }
        }
        Directory.Delete(directory, false);
    }
    sealed class OwnedProcess(Process process) : IOwnedDisplay
    {
        public bool HasExited => process.HasExited;
        public async Task<bool> StopAsync(TimeSpan timeout)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            using var deadline = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(deadline.Token); return process.HasExited; }
            catch (OperationCanceledException) { return false; }
        }
        public void Dispose() => process.Dispose();
    }
}

internal static class X11Authentication
{
    static readonly byte[] Name = Encoding.ASCII.GetBytes("MIT-MAGIC-COOKIE-1");
    internal static bool Accepted(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length != 8) throw new BackendException("display_auth_failed", "Truncated X11 setup response.");
        if (prefix[0] == 0) return false;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..]) * 4;
        if (prefix[0] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]) != 11 ||
            BinaryPrimitives.ReadUInt16LittleEndian(prefix[4..]) != 0 || length is < 32 or > 65536)
            throw new BackendException("display_auth_failed", "Malformed or oversized X11 setup response.");
        return true;
    }
    internal static byte[] Setup(byte[] cookie)
    {
        if (cookie.Length != 16) throw new ArgumentException("16 byte cookie required.");
        byte[] data = new byte[48]; data[0] = (byte)'l'; data[2] = 11;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 16);
        Name.CopyTo(data, 12); cookie.CopyTo(data, 32); return data;
    }
    internal static byte[] Authority(string hostname, int display, byte[] cookie)
    {
        if (cookie.Length != 16) throw new ArgumentException("16 byte cookie required.");
        using var output = new MemoryStream();
        void Number(ushort number) { byte[] bytes = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, number); output.Write(bytes); }
        void Field(byte[] bytes) { Number(checked((ushort)bytes.Length)); output.Write(bytes); }
        Number(256); // FamilyLocal, hostname for Unix-domain X clients.
        Field(Encoding.UTF8.GetBytes(hostname));
        Field(Encoding.ASCII.GetBytes(display.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Field(Name); Field(cookie); return output.ToArray();
    }
}

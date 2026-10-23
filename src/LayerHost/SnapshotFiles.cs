namespace LayerHost;

internal static class SnapshotFiles
{
    internal static async Task ReplaceAsync(string path, byte[] bytes,int maxRetries=5)
    {
        string temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
        for (int attempt = 0; ; attempt++)
        {
            try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); return; }
            catch (Exception e) when (attempt < maxRetries && CanRetryReplace(e, path))
            { await Task.Delay(20).ConfigureAwait(false); }
        }
    }
    static bool CanRetryReplace(Exception error, string path)
    {
        // ReplaceFile can report 1175 while a short-lived reader changes during
        // replacement; a later sharing probe can miss that reader. Microsoft
        // specifies that both original names remain intact for this error.
        // Retry the same atomic operation within the existing five-retry limit
        // (100 ms total retry delay, excluding filesystem call duration).
        // No ACL/attribute changes, manual deletion or alternate-path fallback.
        if (error is IOException && (error.HResult & 0xffff) is 32 or 33 or 1175) return true;
        if ((error is not IOException && error is not UnauthorizedAccessException) || (error.HResult & 0xffff) is not (5 or 1175 or 1176)) return false;
        // MoveFile can report ACCESS_DENIED for an existing read lock. Confirm
        // delete sharing independently without deleting. ACL/read-only denials
        // are not turned into broad permission retries or altered permissions.
        using var probe=OpenDeleteProbe(path,0x10000,7,0,3,0,0);
        return probe.IsInvalid&&System.Runtime.InteropServices.Marshal.GetLastWin32Error() is 32 or 33;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenDeleteProbe(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
    internal static Task ReplaceTextAsync(string path, string text,int maxRetries=5) => ReplaceAsync(path, System.Text.Encoding.UTF8.GetBytes(text),maxRetries);
    // A name may briefly be unavailable during Windows replacement. The reader
    // holds a shared snapshot handle; retries do not repeat application input.
    static T Read<T>(string path, Func<FileStream, T> read)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return read(file); }
            catch (FileNotFoundException) when (attempt < 5) { Thread.Sleep(10); }
            catch (IOException e) when (attempt < 5 && (e.HResult & 0xffff) is 32 or 33) { Thread.Sleep(10); }
        }
    }
    internal static string ReadText(string path) => Read(path, file => { using var reader = new StreamReader(file); return reader.ReadToEnd(); });
    internal static byte[] ReadBytes(string path) => Read(path, file => { using var stream = new MemoryStream(); file.CopyTo(stream); return stream.ToArray(); });
    internal static Bitmap ReadBitmap(string path)
    { using var stream = new MemoryStream(ReadBytes(path)); using var image = new Bitmap(stream); return new Bitmap(image); }
}

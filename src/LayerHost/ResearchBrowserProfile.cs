using System.Security.Cryptography;
using System.Text;

namespace LayerHost;

// Fixed project-owned disposable profiles, never the user's ordinary browser profile.
// The lease prevents our harnesses from sharing a profile while sources are alive.
internal sealed class ResearchBrowserProfile : IDisposable
{
    readonly Mutex lease;
    internal string Path { get; }
    internal ResearchBrowserProfile(string project, string role = "main")
    {
        if (role.Length is < 1 or > 32 || role.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-'))) throw new ArgumentException("Use a bounded research profile role.");
        string folder = System.IO.Path.Combine(project, "artifacts/browser-keyboard-research");
        Path = role == "main" ? System.IO.Path.Combine(folder, "profile") : System.IO.Path.Combine(folder, role, "profile");
        Directory.CreateDirectory(Path);
        string name = "Local\\VIAVW_ResearchProfile_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.ToUpperInvariant())))[..24];
        lease = new Mutex(false, name); bool held;
        try { held = lease.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { lease.Dispose(); throw new InvalidOperationException("The designated disposable research profile is already leased."); }
    }
    bool disposed;
    public void Dispose() { if (disposed) return; disposed = true; lease.ReleaseMutex(); lease.Dispose(); }
}

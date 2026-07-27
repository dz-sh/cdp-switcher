using System.Diagnostics;
using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Chrome;

public sealed class ManagedChromeSession
{
    internal ManagedChromeSession(
        BrowserProfile profile,
        string profileDirectory,
        Process process,
        ChromeBackend backend)
    {
        Profile = profile;
        ProfileDirectory = profileDirectory;
        Process = process;
        Backend = backend;
    }

    public BrowserProfile Profile { get; }

    public string ProfileDirectory { get; }

    public Process Process { get; }

    public ChromeBackend Backend { get; }

    public bool IsRunning
    {
        get
        {
            try
            {
                return !Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}

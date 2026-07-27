using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Chrome;

internal sealed class ManagedChromeExitedEventArgs : EventArgs
{
    public ManagedChromeExitedEventArgs(
        BrowserProfile profile,
        int processId)
    {
        Profile = profile;
        ProcessId = processId;
    }

    public BrowserProfile Profile { get; }

    public int ProcessId { get; }
}

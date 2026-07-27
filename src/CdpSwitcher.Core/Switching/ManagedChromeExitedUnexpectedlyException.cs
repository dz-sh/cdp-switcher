using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Switching;

public sealed class ManagedChromeExitedUnexpectedlyException : Exception
{
    public ManagedChromeExitedUnexpectedlyException(
        BrowserProfile profile)
        : base(
            $"{profile.Name} closed unexpectedly. Select a profile and " +
            "choose Activate.")
    {
        Profile = profile;
    }

    public BrowserProfile Profile { get; }
}

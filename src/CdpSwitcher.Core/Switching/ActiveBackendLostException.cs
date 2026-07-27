using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Switching;

public sealed class ActiveBackendLostException : Exception
{
    public ActiveBackendLostException(BrowserProfile profile)
        : base(
            $"{profile.Name} is no longer available through Chrome's " +
            "verified CDP endpoint. Select a profile and choose Activate.")
    {
        Profile = profile;
    }

    public BrowserProfile Profile { get; }
}

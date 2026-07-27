using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeProfileInUseException : Exception
{
    public ChromeProfileInUseException(BrowserProfile profile)
        : base(
            $"The profile \"{profile.Name}\" is already open in Chrome. " +
            "Close it, then retry or restart CDP Switcher.")
    {
    }
}

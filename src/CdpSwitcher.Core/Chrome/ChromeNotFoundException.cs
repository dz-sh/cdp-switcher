namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeNotFoundException : Exception
{
    public ChromeNotFoundException()
        : base(
            "Google Chrome was not found. Install Chrome and restart " +
            "CDP Switcher.")
    {
    }
}

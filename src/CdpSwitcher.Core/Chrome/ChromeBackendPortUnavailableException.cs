namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeBackendPortUnavailableException : Exception
{
    public ChromeBackendPortUnavailableException()
        : base(
            "Chrome could not start its private connection. " +
            "Choose Activate to try again.")
    {
    }
}

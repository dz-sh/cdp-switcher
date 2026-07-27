namespace CdpSwitcher.Core.Chrome;

internal sealed class ChromeBackendPortConflictException : Exception
{
    public ChromeBackendPortConflictException()
        : base("The selected Chrome CDP port is owned by another process.")
    {
    }
}

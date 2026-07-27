namespace CdpSwitcher.Core.Chrome;

internal sealed class ChromeBackendNotReadyException : Exception
{
    public ChromeBackendNotReadyException()
        : base("The Chrome CDP listener is not ready.")
    {
    }
}

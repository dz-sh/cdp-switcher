namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeStartException : Exception
{
    public ChromeStartException(Exception? innerException = null)
        : base(
            "Google Chrome could not be started. Check its installation " +
            "and choose Activate to retry.",
            innerException)
    {
    }
}

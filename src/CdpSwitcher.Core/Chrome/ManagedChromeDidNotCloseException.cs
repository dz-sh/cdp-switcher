namespace CdpSwitcher.Core.Chrome;

public sealed class ManagedChromeDidNotCloseException : Exception
{
    public ManagedChromeDidNotCloseException()
        : base("The managed Chrome window did not close.")
    {
    }
}

namespace CdpSwitcher.Core.Gateway;

internal sealed class BackendLostEventArgs : EventArgs
{
    public BackendLostEventArgs(long generation)
    {
        Generation = generation;
    }

    public long Generation { get; }
}

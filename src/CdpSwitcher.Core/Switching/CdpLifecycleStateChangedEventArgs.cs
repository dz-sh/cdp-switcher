namespace CdpSwitcher.Core.Switching;

public sealed class CdpLifecycleStateChangedEventArgs : EventArgs
{
    public CdpLifecycleStateChangedEventArgs(CdpLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    public CdpLifecycleState State { get; }
}

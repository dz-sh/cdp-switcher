using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Switching;

public sealed record CdpLifecycleState
{
    internal CdpLifecycleState(
        CdpLifecycleStatus status,
        BrowserProfile? managedProfile,
        bool operationsAvailable,
        Exception? failure)
    {
        if ((status == CdpLifecycleStatus.Error) !=
            (failure is not null))
        {
            throw new ArgumentException(
                "Only the Error state can contain a failure.");
        }

        if (status == CdpLifecycleStatus.Active &&
            managedProfile is null)
        {
            throw new ArgumentException(
                "Active requires a running managed Chrome.");
        }

        if (status == CdpLifecycleStatus.Stopped &&
            managedProfile is not null)
        {
            throw new ArgumentException(
                "Stopped cannot retain a managed Chrome.");
        }

        if (managedProfile is not null && !operationsAvailable)
        {
            throw new ArgumentException(
                "A managed Chrome requires completed startup checks.");
        }

        Status = status;
        ManagedProfile = managedProfile;
        OperationsAvailable = operationsAvailable;
        Failure = failure;
    }

    public CdpLifecycleStatus Status { get; }

    public BrowserProfile? ManagedProfile { get; }

    public bool IsChromeRunning => ManagedProfile is not null;

    public bool OperationsAvailable { get; }

    public Exception? Failure { get; }
}

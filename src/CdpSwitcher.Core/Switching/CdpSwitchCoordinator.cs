using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Gateway;
using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Switching;

public sealed class CdpSwitchCoordinator
{
    private readonly CdpGateway _gateway;
    private readonly ManagedChromeController _chromeController;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private ActiveLease? _activeLease;
    private CdpLifecycleState _state;
    private bool _operationsAvailable;

    public CdpSwitchCoordinator(
        CdpGateway gateway,
        ManagedChromeController chromeController)
    {
        _gateway = gateway;
        _chromeController = chromeController;
        _state = new CdpLifecycleState(
            CdpLifecycleStatus.Starting,
            managedProfile: null,
            operationsAvailable: false,
            failure: null);
        _chromeController.UnexpectedExit +=
            ChromeController_UnexpectedExit;
        _gateway.BackendLost += Gateway_BackendLost;
    }

    public event EventHandler<CdpLifecycleStateChangedEventArgs>?
        StateChanged;

    public CdpLifecycleState State => Volatile.Read(ref _state);

    public async Task InitializeAsync(
        IReadOnlyCollection<BrowserProfile> profiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        await _switchLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            _operationsAvailable = false;
            _activeLease = null;
            PublishState(CdpLifecycleStatus.Starting);

            try
            {
                try
                {
                    await _gateway.StartAsync(
                        cancellationToken).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    throw new GatewayPortUnavailableException(
                        _gateway.FrontendPort,
                        exception);
                }

                _chromeController.ValidateEnvironment(profiles);
                _operationsAvailable = true;
                PublishState(CdpLifecycleStatus.Stopped);
            }
            catch (Exception exception)
            {
                await EnterErrorAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task ReportInitializationFailureAsync(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        await _switchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _operationsAvailable = false;
            await EnterErrorAsync(exception).ConfigureAwait(false);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task ActivateAsync(
        BrowserProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _switchLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureOperationsAvailable();

            try
            {
                if (IsCurrentLeaseHealthy(profile))
                {
                    await _chromeController.StartAsync(
                        profile,
                        cancellationToken).ConfigureAwait(false);
                    await _gateway.VerifyFrontendAsync(
                        cancellationToken).ConfigureAwait(false);
                    var activeLease = _activeLease!;
                    _activeLease = activeLease with
                    {
                        Profile = profile,
                    };
                    PublishState(CdpLifecycleStatus.Active);
                    return;
                }

                var transition = _activeLease is null
                    ? CdpLifecycleStatus.Starting
                    : CdpLifecycleStatus.Switching;
                _activeLease = null;
                PublishState(transition);

                await _gateway.SuspendAsync(
                    cancellationToken).ConfigureAwait(false);

                if (_chromeController.Current is { } current &&
                    current.Profile.Id != profile.Id &&
                    !await _chromeController.StopAsync(
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new ManagedChromeDidNotCloseException();
                }

                var session = await _chromeController.StartAsync(
                    profile,
                    cancellationToken).ConfigureAwait(false);
                var gatewayGeneration = await _gateway.PublishAsync(
                    session.Backend,
                    cancellationToken).ConfigureAwait(false);
                await _gateway.VerifyFrontendAsync(
                    cancellationToken).ConfigureAwait(false);
                if (!session.IsRunning ||
                    _chromeController.Current?.Process.Id !=
                        session.Process.Id)
                {
                    throw new ManagedChromeExitedUnexpectedlyException(
                        profile);
                }

                _activeLease = new ActiveLease(
                    profile,
                    gatewayGeneration,
                    session.Process.Id);
                PublishState(CdpLifecycleStatus.Active);
            }
            catch (Exception exception)
            {
                await EnterErrorAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _switchLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeLease is null &&
                _chromeController.Current is not { IsRunning: true })
            {
                PublishState(CdpLifecycleStatus.Stopped);
                return;
            }

            _activeLease = null;
            PublishState(CdpLifecycleStatus.Switching);

            try
            {
                await _gateway.SuspendAsync(
                    cancellationToken).ConfigureAwait(false);
                if (!await _chromeController.StopAsync(
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new ManagedChromeDidNotCloseException();
                }

                PublishState(CdpLifecycleStatus.Stopped);
            }
            catch (Exception exception)
            {
                await EnterErrorAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task ForceStopAsync(
        CancellationToken cancellationToken)
    {
        await _switchLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            _activeLease = null;
            PublishState(CdpLifecycleStatus.Switching);

            try
            {
                await _gateway.SuspendAsync(
                    cancellationToken).ConfigureAwait(false);
                await _chromeController.ForceStopAsync(
                    cancellationToken).ConfigureAwait(false);
                PublishState(CdpLifecycleStatus.Stopped);
            }
            catch (Exception exception)
            {
                await EnterErrorAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task UpdateProfileAsync(
        BrowserProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _switchLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            _chromeController.UpdateProfile(profile);
            if (_activeLease is { } activeLease &&
                activeLease.Profile.Id == profile.Id)
            {
                _activeLease = activeLease with
                {
                    Profile = profile,
                };
            }

            PublishState(State.Status, State.Failure);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private bool IsCurrentLeaseHealthy(BrowserProfile profile)
    {
        return _activeLease is { } lease &&
            lease.Profile.Id == profile.Id &&
            _gateway.IsGenerationActive(lease.GatewayGeneration) &&
            _chromeController.Current is { IsRunning: true } current &&
            current.Process.Id == lease.ProcessId;
    }

    private void EnsureOperationsAvailable()
    {
        if (!_operationsAvailable)
        {
            throw new InvalidOperationException(
                "CDP Switcher startup checks have not completed.");
        }
    }

    private async Task EnterErrorAsync(Exception failure)
    {
        _activeLease = null;
        try
        {
            await _gateway.SuspendAsync(
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Removing the backend happens before session draining.
        }

        PublishState(CdpLifecycleStatus.Error, failure);
    }

    private void PublishState(
        CdpLifecycleStatus status,
        Exception? failure = null)
    {
        var current = _chromeController.Current;
        var chromeRunning = current is { IsRunning: true };
        var managedProfile = chromeRunning
            ? current!.Profile
            : null;

        if (status == CdpLifecycleStatus.Active)
        {
            if (_activeLease is null ||
                current is null ||
                !chromeRunning ||
                current.Process.Id != _activeLease.ProcessId ||
                managedProfile?.Id != _activeLease.Profile.Id)
            {
                throw new InvalidOperationException(
                    "The active lifecycle invariant was violated.");
            }
        }
        else if (_activeLease is not null)
        {
            throw new InvalidOperationException(
                "Only Active can retain an active lease.");
        }

        var next = new CdpLifecycleState(
            status,
            managedProfile,
            _operationsAvailable,
            failure);
        if (next == State)
        {
            return;
        }

        Volatile.Write(ref _state, next);
        StateChanged?.Invoke(
            this,
            new CdpLifecycleStateChangedEventArgs(next));
    }

    private async void ChromeController_UnexpectedExit(
        object? sender,
        ManagedChromeExitedEventArgs args)
    {
        try
        {
            await _switchLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_chromeController.Current is { } current &&
                    current.Process.Id != args.ProcessId)
                {
                    return;
                }

                if (_activeLease?.ProcessId == args.ProcessId)
                {
                    var profile = _activeLease.Profile;
                    await EnterErrorAsync(
                        new ManagedChromeExitedUnexpectedlyException(
                            profile)).ConfigureAwait(false);
                    return;
                }

                if (State.Status == CdpLifecycleStatus.Error &&
                    State.IsChromeRunning &&
                    State.ManagedProfile?.Id == args.Profile.Id)
                {
                    PublishState(
                        CdpLifecycleStatus.Error,
                        State.Failure);
                }
            }
            finally
            {
                _switchLock.Release();
            }
        }
        catch
        {
            // A process notification must not terminate the application.
        }
    }

    private async void Gateway_BackendLost(
        object? sender,
        BackendLostEventArgs args)
    {
        try
        {
            await _switchLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_activeLease is not { } lease ||
                    lease.GatewayGeneration != args.Generation)
                {
                    return;
                }

                await EnterErrorAsync(
                    new ActiveBackendLostException(
                        lease.Profile)).ConfigureAwait(false);
            }
            finally
            {
                _switchLock.Release();
            }
        }
        catch
        {
            // A gateway notification must not terminate the application.
        }
    }

    private sealed record ActiveLease(
        BrowserProfile Profile,
        long GatewayGeneration,
        int ProcessId);
}

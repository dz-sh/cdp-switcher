using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Chrome;

public sealed class ManagedChromeController : IDisposable
{
    private static readonly TimeSpan StartupTimeout =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CloseRequestTimeout =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ShutdownTimeout =
        TimeSpan.FromSeconds(10);

    private readonly ChromeLocator _chromeLocator;
    private readonly ManagedProfilePaths _profilePaths;
    private readonly ChromeBackendVerifier _backendVerifier;
    private readonly ChromeProfileUseDetector _profileUseDetector;
    private readonly object _stateLock = new();
    private ManagedChromeSession? _current;
    private int? _expectedExitProcessId;

    public ManagedChromeController(
        ChromeLocator chromeLocator,
        ManagedProfilePaths profilePaths,
        ChromeBackendVerifier backendVerifier,
        ChromeProfileUseDetector profileUseDetector)
    {
        _chromeLocator = chromeLocator;
        _profilePaths = profilePaths;
        _backendVerifier = backendVerifier;
        _profileUseDetector = profileUseDetector;
    }

    internal event EventHandler<ManagedChromeExitedEventArgs>? UnexpectedExit;

    internal ManagedChromeSession? Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    internal void ValidateEnvironment(
        IEnumerable<BrowserProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (_chromeLocator.FindChrome() is null)
        {
            throw new ChromeNotFoundException();
        }

        foreach (var profile in profiles)
        {
            var profileDirectory =
                _profilePaths.GetProfileDirectory(profile);
            if (_profileUseDetector.IsInUse(profileDirectory))
            {
                throw new ChromeProfileInUseException(profile);
            }
        }
    }

    internal void UpdateProfile(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_stateLock)
        {
            if (_current?.Profile.Id != profile.Id)
            {
                return;
            }

            _current = new ManagedChromeSession(
                profile,
                _current.ProfileDirectory,
                _current.Process,
                _current.Backend);
        }
    }

    internal async Task<ManagedChromeSession> StartAsync(
        BrowserProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (Current is { IsRunning: true } current &&
            current.Profile.Id == profile.Id)
        {
            var discovery = new DevToolsActivePort(
                current.Backend.Port,
                current.Backend.BrowserWebSocketUri.AbsolutePath);
            var backend = await _backendVerifier.VerifyAsync(
                discovery,
                current.Process.Id,
                cancellationToken).ConfigureAwait(false);
            current.Process.Refresh();
            if (current.Process.HasExited)
            {
                throw new InvalidOperationException(
                    "Google Chrome exited during CDP verification. " +
                    "Choose Activate to retry.");
            }

            var refreshedSession = new ManagedChromeSession(
                profile,
                current.ProfileDirectory,
                current.Process,
                backend);
            SetCurrent(refreshedSession);
            return refreshedSession;
        }

        if (Current is { IsRunning: true })
        {
            throw new InvalidOperationException(
                "Another managed Chrome profile is still running.");
        }

        ClearCurrent()?.Process.Dispose();

        var chromePath = _chromeLocator.FindChrome();
        if (chromePath is null)
        {
            throw new ChromeNotFoundException();
        }

        var profileDirectory = _profilePaths.GetProfileDirectory(profile);
        Directory.CreateDirectory(profileDirectory);
        if (_profileUseDetector.IsInUse(profileDirectory))
        {
            throw new ChromeProfileInUseException(profile);
        }

        var discoveryFile = Path.Combine(
            profileDirectory,
            "DevToolsActivePort");
        if (File.Exists(discoveryFile))
        {
            File.Delete(discoveryFile);
        }

        var startInfo = new ProcessStartInfo(chromePath)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(
            $"--user-data-dir={profileDirectory}");
        startInfo.ArgumentList.Add("--remote-debugging-port=0");
        startInfo.ArgumentList.Add(
            "--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("about:blank");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception)
        {
            throw new ChromeStartException(exception);
        }

        if (process is null)
        {
            throw new ChromeStartException();
        }

        try
        {
            var discovery = await WaitForDiscoveryAsync(
                process,
                discoveryFile,
                cancellationToken).ConfigureAwait(false);
            var backend = await _backendVerifier.VerifyAsync(
                discovery,
                process.Id,
                cancellationToken).ConfigureAwait(false);
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Google Chrome exited during CDP verification. " +
                    "Choose Activate to retry.");
            }

            var session = new ManagedChromeSession(
                profile,
                profileDirectory,
                process,
                backend);
            SetCurrent(session);
            return session;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(
                    CancellationToken.None).ConfigureAwait(false);
            }

            process.Dispose();
            throw;
        }
    }

    internal async Task<bool> StopAsync(
        CancellationToken cancellationToken)
    {
        var current = Current;
        if (current is null)
        {
            return true;
        }

        if (!MarkExpectedExit(current))
        {
            return true;
        }

        if (current.Process.HasExited)
        {
            ClearCurrent(current);
            current.Process.Dispose();
            return true;
        }

        using (var closeTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken))
        {
            closeTimeout.CancelAfter(CloseRequestTimeout);
            try
            {
                await RequestBrowserCloseAsync(
                    current.Backend.BrowserWebSocketUri,
                    closeTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                ClearExpectedExit(current);
                throw;
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or
                      HttpRequestException or
                      WebSocketException or
                      InvalidOperationException)
            {
                current.Process.CloseMainWindow();
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ShutdownTimeout);

        try
        {
            await current.Process.WaitForExitAsync(
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            ClearExpectedExit(current);
            return false;
        }
        catch (OperationCanceledException)
        {
            ClearExpectedExit(current);
            throw;
        }

        ClearCurrent(current);
        current.Process.Dispose();
        return true;
    }

    internal async Task ForceStopAsync(
        CancellationToken cancellationToken)
    {
        var current = Current;
        if (current is null)
        {
            return;
        }

        if (!MarkExpectedExit(current))
        {
            return;
        }

        current.Process.Refresh();
        if (!current.Process.HasExited)
        {
            try
            {
                current.Process.Kill(entireProcessTree: true);
                await current.Process.WaitForExitAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ClearExpectedExit(current);
                throw;
            }
        }

        ClearCurrent(current);
        current.Process.Dispose();
    }

    private void SetCurrent(ManagedChromeSession session)
    {
        lock (_stateLock)
        {
            if (_current is not null &&
                _current.Process.Id != session.Process.Id)
            {
                _current.Process.Exited -= Process_Exited;
            }

            _current = session;
            _expectedExitProcessId = null;
            session.Process.Exited -= Process_Exited;
            session.Process.Exited += Process_Exited;
            session.Process.EnableRaisingEvents = true;
        }
    }

    private ManagedChromeSession? ClearCurrent(
        ManagedChromeSession? expected = null)
    {
        lock (_stateLock)
        {
            if (expected is not null &&
                !ReferenceEquals(_current, expected))
            {
                return null;
            }

            var current = _current;
            if (current is not null)
            {
                current.Process.Exited -= Process_Exited;
            }

            _current = null;
            _expectedExitProcessId = null;
            return current;
        }
    }

    private bool MarkExpectedExit(ManagedChromeSession session)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_current, session))
            {
                return false;
            }

            _expectedExitProcessId = session.Process.Id;
            return true;
        }
    }

    private void ClearExpectedExit(ManagedChromeSession session)
    {
        lock (_stateLock)
        {
            if (_expectedExitProcessId == session.Process.Id)
            {
                _expectedExitProcessId = null;
            }
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        ManagedChromeSession? exitedSession;
        lock (_stateLock)
        {
            if (_current is null ||
                _current.Process.Id != process.Id ||
                _expectedExitProcessId == process.Id)
            {
                return;
            }

            exitedSession = _current;
            _current = null;
            process.Exited -= Process_Exited;
        }

        UnexpectedExit?.Invoke(
            this,
            new ManagedChromeExitedEventArgs(
                exitedSession.Profile,
                exitedSession.Process.Id));
        process.Dispose();
    }

    private static async Task<DevToolsActivePort> WaitForDiscoveryAsync(
        Process process,
        string discoveryFile,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(StartupTimeout);

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Google Chrome exited before CDP became ready. " +
                    "Choose Activate to retry.");
            }

            try
            {
                if (File.Exists(discoveryFile))
                {
                    var content = await File.ReadAllTextAsync(
                        discoveryFile,
                        timeout.Token).ConfigureAwait(false);
                    return DevToolsActivePort.Parse(content);
                }
            }
            catch (IOException)
            {
                // Chrome may still be replacing the discovery file.
            }
            catch (FormatException)
            {
                // Chrome may have written only the first line so far.
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task RequestBrowserCloseAsync(
        Uri browserWebSocketUri,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            browserWebSocketUri,
            cancellationToken).ConfigureAwait(false);

        var command = Encoding.UTF8.GetBytes(
            """{"id":1,"method":"Browser.close"}""");
        await socket.SendAsync(
            command,
            WebSocketMessageType.Text,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        ClearCurrent()?.Process.Dispose();
        _backendVerifier.Dispose();
    }
}

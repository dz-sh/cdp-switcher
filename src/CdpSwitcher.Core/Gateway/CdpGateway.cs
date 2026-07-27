using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using CdpSwitcher.Core.Chrome;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CdpSwitcher.Core.Gateway;

public sealed class CdpGateway : IAsyncDisposable
{
    private static readonly TimeSpan BackendConnectTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> AllowedDiscoveryPaths =
        new(StringComparer.Ordinal)
        {
            "/json",
            "/json/list",
            "/json/protocol",
            "/json/version",
        };

    private readonly int _frontendPort;
    private readonly HttpClient _httpClient;
    private readonly ITcpPortOwnerResolver _portOwnerResolver;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, GatewaySession> _sessions = [];
    private WebApplication? _application;
    private BackendRegistration? _backend;
    private long _generation;

    public CdpGateway(int frontendPort = 9222)
        : this(
            frontendPort,
            new WindowsTcpPortOwnerResolver())
    {
    }

    public CdpGateway(
        int frontendPort,
        ITcpPortOwnerResolver portOwnerResolver)
    {
        if (frontendPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(frontendPort));
        }

        ArgumentNullException.ThrowIfNull(portOwnerResolver);
        _frontendPort = frontendPort;
        _portOwnerResolver = portOwnerResolver;
        _httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                UseProxy = false,
            })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    internal int FrontendPort => _frontendPort;

    internal event EventHandler<BackendLostEventArgs>? BackendLost;

    internal bool HasActiveBackend =>
        Volatile.Read(ref _backend) is not null;

    internal bool IsGenerationActive(long generation)
    {
        return Volatile.Read(ref _backend)?.Generation == generation;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        try
        {
            if (_application is not null)
            {
                return;
            }

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Listen(IPAddress.Loopback, _frontendPort);
            });

            var application = builder.Build();
            application.UseWebSockets();
            application.Run(HandleRequestAsync);
            await application.StartAsync(
                cancellationToken).ConfigureAwait(false);
            _application = application;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal async Task<long> PublishAsync(
        ChromeBackend backend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backend);
        cancellationToken.ThrowIfCancellationRequested();

        if (_application is null)
        {
            throw new InvalidOperationException(
                "The CDP gateway has not been started.");
        }

        await SuspendAsync(cancellationToken).ConfigureAwait(false);

        var registration = new BackendRegistration(
            backend,
            Interlocked.Increment(ref _generation));
        registration.RegisterWebSocketPath(
            backend.BrowserWebSocketUri.PathAndQuery);
        Volatile.Write(ref _backend, registration);
        return registration.Generation;
    }

    internal async Task VerifyFrontendAsync(
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var versionUri = new Uri(
            $"http://127.0.0.1:{_frontendPort}/json/version");
        using var response = await _httpClient.GetAsync(
            versionUri,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(
            timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: timeout.Token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty(
                "webSocketDebuggerUrl",
                out var webSocketProperty) ||
            !Uri.TryCreate(
                webSocketProperty.GetString(),
                UriKind.Absolute,
                out var webSocketUri) ||
            webSocketUri.Scheme != "ws" ||
            !LoopbackAddress.IsLoopbackHost(webSocketUri.Host) ||
            webSocketUri.Port != _frontendPort)
        {
            throw new InvalidOperationException(
                "The fixed CDP endpoint could not be verified.");
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            webSocketUri,
            timeout.Token).ConfigureAwait(false);
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException(
                "The fixed CDP WebSocket could not be verified.");
        }

        socket.Abort();
    }

    internal async Task SuspendAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _backend, null);
        await CancelSessionsAsync(
            registration: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelSessionsAsync(
        BackendRegistration? registration,
        CancellationToken cancellationToken,
        Guid? excludedSessionId = null)
    {
        var sessions = _sessions
            .Where(
                pair =>
                    pair.Key != excludedSessionId &&
                    (registration is null ||
                     ReferenceEquals(
                         pair.Value.Registration,
                         registration)))
            .Select(pair => pair.Value)
            .ToArray();
        foreach (var session in sessions)
        {
            session.Cancel();
        }

        if (sessions.Length > 0)
        {
            await Task.WhenAll(
                sessions.Select(session => session.Completion.Task))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(context).ConfigureAwait(false);
            return;
        }

        await HandleHttpAsync(context).ConfigureAwait(false);
    }

    private async Task HandleHttpAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (context.Request.Method != HttpMethods.Get ||
            !AllowedDiscoveryPaths.Contains(path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers["Cache-Control"] = "no-store";

        var registration = Volatile.Read(ref _backend);
        if (registration is null)
        {
            await WriteUnavailableAsync(context).ConfigureAwait(false);
            return;
        }

        if (!IsBackendOwnerCurrent(registration))
        {
            await RevokeIfCurrentAsync(
                registration,
                context.RequestAborted).ConfigureAwait(false);
            await WriteUnavailableAsync(context).ConfigureAwait(false);
            return;
        }

        var backendUri = new UriBuilder(
            "http",
            "127.0.0.1",
            registration.Backend.Port,
            path)
        {
            Query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value![1..]
                : string.Empty,
        }.Uri;

        try
        {
            using var response = await _httpClient.GetAsync(
                backendUri,
                context.RequestAborted).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(
                context.RequestAborted).ConfigureAwait(false);

            if (!ReferenceEquals(
                    registration,
                    Volatile.Read(ref _backend)) ||
                !IsBackendOwnerCurrent(registration))
            {
                await RevokeIfCurrentAsync(
                    registration,
                    context.RequestAborted).ConfigureAwait(false);
                await WriteUnavailableAsync(context).ConfigureAwait(false);
                return;
            }

            if ((int)response.StatusCode >= 500)
            {
                await RevokeIfCurrentAsync(
                    registration,
                    CancellationToken.None).ConfigureAwait(false);
                await WriteUnavailableAsync(context).ConfigureAwait(false);
                return;
            }

            if (response.IsSuccessStatusCode)
            {
                body = CdpDiscoveryRewriter.Rewrite(
                    body,
                    registration.Backend.Port,
                    _frontendPort,
                    registration.RegisterWebSocketPath);
            }

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType =
                response.Content.Headers.ContentType?.ToString() ??
                "application/json";
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(
                body,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            // The frontend caller ended the request.
        }
        catch (Exception exception)
            when (exception is HttpRequestException or
                  TaskCanceledException or
                  System.Text.Json.JsonException)
        {
            await RevokeIfCurrentAsync(
                registration,
                CancellationToken.None).ConfigureAwait(false);
            if (!context.Response.HasStarted)
            {
                await WriteUnavailableAsync(context).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        var registration = Volatile.Read(ref _backend);
        var requestedPath =
            (context.Request.Path.Value ?? string.Empty) +
            context.Request.QueryString.Value;

        if (registration is null ||
            !registration.TryResolveWebSocketPath(
                requestedPath,
                out var backendPath))
        {
            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!IsBackendOwnerCurrent(registration))
        {
            await RevokeIfCurrentAsync(
                registration,
                context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var sessionId = Guid.NewGuid();
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted);
        var session = new GatewaySession(
            registration,
            cancellation);
        if (!_sessions.TryAdd(sessionId, session))
        {
            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var backendConnected = false;
        try
        {
            using var backendSocket = new ClientWebSocket();
            var backendUri = new Uri(
                $"ws://127.0.0.1:{registration.Backend.Port}" +
                backendPath);
            using (var connectTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellation.Token))
            {
                connectTimeout.CancelAfter(BackendConnectTimeout);
                await backendSocket.ConnectAsync(
                    backendUri,
                    connectTimeout.Token).ConfigureAwait(false);
            }
            backendConnected = true;

            if (!ReferenceEquals(
                    registration,
                    Volatile.Read(ref _backend)) ||
                !IsBackendOwnerCurrent(registration))
            {
                await RevokeIfCurrentAsync(
                    registration,
                    CancellationToken.None,
                    sessionId).ConfigureAwait(false);
                return;
            }

            using var frontendSocket =
                await context.WebSockets.AcceptWebSocketAsync()
                    .ConfigureAwait(false);
            await ProxyWebSocketsAsync(
                frontendSocket,
                backendSocket,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (WebSocketException)
            when (!backendConnected &&
                  IsBrowserWebSocketPath(backendPath))
        {
            await RevokeIfCurrentAsync(
                registration,
                CancellationToken.None,
                sessionId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!backendConnected &&
                  !cancellation.IsCancellationRequested &&
                  IsBrowserWebSocketPath(backendPath))
        {
            await RevokeIfCurrentAsync(
                registration,
                CancellationToken.None,
                sessionId).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or
                  WebSocketException)
        {
            // A switch, client disconnect, or backend close ends the session.
        }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
            session.Completion.TrySetResult();
        }
    }

    private static bool IsBrowserWebSocketPath(string path)
    {
        return path.StartsWith(
            "/devtools/browser/",
            StringComparison.Ordinal);
    }

    private static async Task ProxyWebSocketsAsync(
        WebSocket frontend,
        WebSocket backend,
        CancellationToken cancellationToken)
    {
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var toBackend = PumpAsync(frontend, backend, linked.Token);
        var toFrontend = PumpAsync(backend, frontend, linked.Token);

        await Task.WhenAny(toBackend, toFrontend).ConfigureAwait(false);
        linked.Cancel();

        try
        {
            await Task.WhenAll(toBackend, toFrontend).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The other direction is expected to stop after the first closes.
        }
    }

    private static async Task PumpAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (source.State is WebSocketState.Open or
               WebSocketState.CloseSent)
        {
            var result = await source.ReceiveAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (destination.State == WebSocketState.Open)
                {
                    await destination.CloseOutputAsync(
                        result.CloseStatus ??
                            WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            await destination.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode =
            StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"error":"cdp_backend_unavailable"}""",
            context.RequestAborted).ConfigureAwait(false);
    }

    private bool IsBackendOwnerCurrent(
        BackendRegistration registration)
    {
        try
        {
            return _portOwnerResolver.FindListeningProcessId(
                registration.Backend.Port) ==
                registration.Backend.OwningProcessId;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or
                  PlatformNotSupportedException)
        {
            return false;
        }
    }

    private async Task RevokeIfCurrentAsync(
        BackendRegistration registration,
        CancellationToken cancellationToken,
        Guid? excludedSessionId = null)
    {
        if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _backend,
                    null,
                    registration),
                registration))
        {
            return;
        }

        try
        {
            await CancelSessionsAsync(
                registration,
                cancellationToken,
                excludedSessionId).ConfigureAwait(false);
        }
        finally
        {
            BackendLost?.Invoke(
                this,
                new BackendLostEventArgs(
                    registration.Generation));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await SuspendAsync(CancellationToken.None).ConfigureAwait(false);

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_application is not null)
            {
                await _application.StopAsync().ConfigureAwait(false);
                await _application.DisposeAsync().ConfigureAwait(false);
                _application = null;
            }
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
            _httpClient.Dispose();
        }
    }

    private sealed class BackendRegistration
    {
        private readonly ConcurrentDictionary<string, string>
            _webSocketRoutes = new(StringComparer.Ordinal);

        public BackendRegistration(
            ChromeBackend backend,
            long generation)
        {
            Backend = backend;
            Generation = generation;
        }

        public ChromeBackend Backend { get; }

        public long Generation { get; }

        public string RegisterWebSocketPath(string backendPath)
        {
            if (!backendPath.StartsWith(
                    "/devtools/",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Chrome returned an invalid WebSocket path.");
            }

            var frontendPath =
                $"/cdp/{Generation:x16}{backendPath}";
            _webSocketRoutes.TryAdd(frontendPath, backendPath);
            return frontendPath;
        }

        public bool TryResolveWebSocketPath(
            string frontendPath,
            out string backendPath)
        {
            if (_webSocketRoutes.TryGetValue(
                    frontendPath,
                    out var resolvedPath))
            {
                backendPath = resolvedPath;
                return true;
            }

            backendPath = string.Empty;
            return false;
        }
    }

    private sealed class GatewaySession
    {
        public GatewaySession(
            BackendRegistration registration,
            CancellationTokenSource cancellation)
        {
            Registration = registration;
            Cancellation = cancellation;
        }

        public BackendRegistration Registration { get; }

        public CancellationTokenSource Cancellation { get; }

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The session completed after the cancellation snapshot.
            }
        }
    }
}

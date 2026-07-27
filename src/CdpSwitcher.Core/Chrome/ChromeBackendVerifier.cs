using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace CdpSwitcher.Core.Chrome;

public sealed class ChromeBackendVerifier : IDisposable
{
    private static readonly TimeSpan VerificationTimeout =
        TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly ITcpPortOwnerResolver _portOwnerResolver;

    public ChromeBackendVerifier()
        : this(new WindowsTcpPortOwnerResolver())
    {
    }

    public ChromeBackendVerifier(
        ITcpPortOwnerResolver portOwnerResolver)
    {
        ArgumentNullException.ThrowIfNull(portOwnerResolver);
        _portOwnerResolver = portOwnerResolver;
        _httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                UseProxy = false,
            })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<ChromeBackend> VerifyAsync(
        int port,
        int expectedProcessId,
        CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (expectedProcessId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedProcessId));
        }

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(VerificationTimeout);

        VerifyPortOwner(port, expectedProcessId);

        var versionUri = new Uri(
            $"http://127.0.0.1:{port}/json/version");
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
            !IsLoopbackHost(webSocketUri.Host) ||
            webSocketUri.Port != port ||
            !IsBrowserWebSocketPath(webSocketUri.AbsolutePath))
        {
            throw new InvalidOperationException(
                "Chrome CDP discovery could not be verified.");
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            webSocketUri,
            timeout.Token).ConfigureAwait(false);
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException(
                "Chrome CDP WebSocket could not be verified.");
        }

        socket.Abort();
        var owningProcessId = VerifyPortOwner(
            port,
            expectedProcessId);
        return new ChromeBackend(
            port,
            webSocketUri,
            owningProcessId);
    }

    private int VerifyPortOwner(
        int port,
        int expectedProcessId)
    {
        var owningProcessId =
            _portOwnerResolver.FindListeningProcessId(port);
        if (owningProcessId is null)
        {
            throw new ChromeBackendNotReadyException();
        }

        if (owningProcessId != expectedProcessId)
        {
            throw new ChromeBackendPortConflictException();
        }

        return expectedProcessId;
    }

    private static bool IsBrowserWebSocketPath(string path)
    {
        return path.StartsWith(
                   "/devtools/browser/",
                   StringComparison.Ordinal) &&
               !path.Contains("..", StringComparison.Ordinal);
    }

    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(
                   host,
                   "localhost",
                   StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out var address) &&
               IPAddress.IsLoopback(address);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

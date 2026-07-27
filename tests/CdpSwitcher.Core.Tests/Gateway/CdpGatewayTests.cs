using System.Net;
using System.Net.Sockets;
using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Gateway;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Gateway;

[TestClass]
public sealed class CdpGatewayTests
{
    [TestMethod]
    public async Task Gateway_revokes_a_backend_with_the_wrong_port_owner()
    {
        var frontendPort = ReserveAvailablePort();
        await using var gateway = new CdpGateway(
            frontendPort,
            new StubPortOwnerResolver(processId: 200));
        using var httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                UseProxy = false,
            });
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        BackendLostEventArgs? backendLoss = null;
        var backendLossCount = 0;
        gateway.BackendLost +=
            (_, args) =>
            {
                backendLoss = args;
                backendLossCount++;
            };
        await gateway.StartAsync(timeout.Token);
        var generation = await gateway.PublishAsync(
            new ChromeBackend(
                Port: 51347,
                BrowserWebSocketUri: new Uri(
                    "ws://127.0.0.1:51347/devtools/browser/example"),
                OwningProcessId: 100),
            timeout.Token);

        using var response = await httpClient.GetAsync(
            $"http://127.0.0.1:{frontendPort}/json/version",
            timeout.Token);

        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.AreEqual(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.IsFalse(gateway.HasActiveBackend);
        Assert.IsNotNull(backendLoss);
        Assert.AreEqual(
            generation,
            backendLoss!.Generation);

        using var secondResponse = await httpClient.GetAsync(
            $"http://127.0.0.1:{frontendPort}/json/version",
            timeout.Token);
        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable,
            secondResponse.StatusCode);
        Assert.AreEqual(1, backendLossCount);
    }

    [TestMethod]
    public async Task Gateway_revokes_an_unreachable_backend()
    {
        var frontendPort = ReserveAvailablePort();
        var backendPort = ReserveAvailablePort();
        while (backendPort == frontendPort)
        {
            backendPort = ReserveAvailablePort();
        }
        await using var gateway = new CdpGateway(
            frontendPort,
            new StubPortOwnerResolver(processId: 100));
        using var httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                UseProxy = false,
            });
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        BackendLostEventArgs? backendLoss = null;
        gateway.BackendLost +=
            (_, args) => backendLoss = args;
        await gateway.StartAsync(timeout.Token);
        var generation = await gateway.PublishAsync(
            new ChromeBackend(
                backendPort,
                new Uri(
                    $"ws://127.0.0.1:{backendPort}" +
                    "/devtools/browser/example"),
                OwningProcessId: 100),
            timeout.Token);

        using var response = await httpClient.GetAsync(
            $"http://127.0.0.1:{frontendPort}/json/version",
            timeout.Token);

        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.IsFalse(gateway.HasActiveBackend);
        Assert.IsNotNull(backendLoss);
        Assert.AreEqual(generation, backendLoss!.Generation);
    }

    private static int ReserveAvailablePort()
    {
        using var listener = new TcpListener(
            IPAddress.Loopback,
            port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubPortOwnerResolver :
        ITcpPortOwnerResolver
    {
        private readonly int _processId;

        public StubPortOwnerResolver(int processId)
        {
            _processId = processId;
        }

        public int? FindListeningProcessId(int port)
        {
            return _processId;
        }
    }
}

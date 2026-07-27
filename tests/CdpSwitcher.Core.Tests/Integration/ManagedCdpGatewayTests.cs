using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Gateway;
using CdpSwitcher.Core.Profiles;
using CdpSwitcher.Core.Switching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Integration;

[TestClass]
public sealed class ManagedCdpGatewayTests
{
    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public async Task Managed_Chrome_is_exposed_only_while_active()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The product supports Windows only.");
            return;
        }

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        var frontendPort = ReserveAvailablePort();
        var gateway = new CdpGateway(frontendPort);
        var chromeController = new ManagedChromeController(
            new ChromeLocator(),
            new ManagedProfilePaths(testRoot),
            new ChromeBackendVerifier(),
            new ChromeProfileUseDetector());
        var coordinator = new CdpSwitchCoordinator(
            gateway,
            chromeController);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(60));
        using var httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                UseProxy = false,
            });
        var versionUri = new Uri(
            $"http://127.0.0.1:{frontendPort}/json/version");
        var profile = BrowserProfile.Create("Integration");

        try
        {
            await coordinator.InitializeAsync(
                [profile],
                timeout.Token);
            Assert.AreEqual(
                CdpLifecycleStatus.Stopped,
                coordinator.State.Status);
            Assert.IsTrue(coordinator.State.OperationsAvailable);
            Assert.IsFalse(coordinator.State.IsChromeRunning);

            using (var inactiveResponse = await httpClient.GetAsync(
                versionUri,
                timeout.Token))
            {
                Assert.AreEqual(
                    HttpStatusCode.ServiceUnavailable,
                    inactiveResponse.StatusCode);
            }

            await coordinator.ActivateAsync(profile, timeout.Token);
            Assert.AreEqual(
                CdpLifecycleStatus.Active,
                coordinator.State.Status);
            Assert.AreEqual(
                profile.Id,
                coordinator.State.ManagedProfile?.Id);
            Assert.IsTrue(coordinator.State.IsChromeRunning);

            Uri firstWebSocketUri;
            using (var activeResponse = await httpClient.GetAsync(
                versionUri,
                timeout.Token))
            {
                Assert.AreEqual(
                    HttpStatusCode.OK,
                    activeResponse.StatusCode);
                await using var content =
                    await activeResponse.Content.ReadAsStreamAsync(
                        timeout.Token);
                using var document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: timeout.Token);
                var webSocketUrl = document.RootElement
                    .GetProperty("webSocketDebuggerUrl")
                    .GetString();
                Assert.IsNotNull(webSocketUrl);
                firstWebSocketUri = new Uri(webSocketUrl!);
                Assert.AreEqual(
                    "127.0.0.1",
                    firstWebSocketUri.Host);
                Assert.AreEqual(
                    frontendPort,
                    firstWebSocketUri.Port);

                using var webSocket = new ClientWebSocket();
                await webSocket.ConnectAsync(
                    firstWebSocketUri,
                    timeout.Token);
                Assert.AreEqual(
                    WebSocketState.Open,
                    webSocket.State);
                webSocket.Abort();
            }

            var originalProcessId =
                chromeController.Current?.Process.Id;
            Assert.IsNotNull(originalProcessId);

            using (var metadataSession = new ClientWebSocket())
            {
                await metadataSession.ConnectAsync(
                    firstWebSocketUri,
                    timeout.Token);
                profile = profile.Edit(
                    "Renamed integration",
                    [ProfileTag.Create("Work", "#2563EB")]);

                await coordinator.UpdateProfileAsync(
                    profile,
                    timeout.Token);

                Assert.AreEqual(
                    originalProcessId,
                    chromeController.Current?.Process.Id);
                Assert.AreEqual(
                    CdpLifecycleStatus.Active,
                    coordinator.State.Status);
                Assert.AreEqual(
                    "Renamed integration",
                    coordinator.State.ManagedProfile?.Name);
                Assert.AreEqual(
                    "Work",
                    coordinator.State.ManagedProfile?.Tags[0].Name);
                Assert.IsTrue(gateway.HasActiveBackend);

                var command =
                    """{"id":1,"method":"Browser.getVersion"}"""u8
                    .ToArray();
                await metadataSession.SendAsync(
                    command,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    timeout.Token);
                var responseBuffer = new byte[4096];
                var result = await metadataSession.ReceiveAsync(
                    responseBuffer,
                    timeout.Token);
                Assert.AreEqual(
                    WebSocketMessageType.Text,
                    result.MessageType);
                Assert.IsTrue(result.Count > 0);
            }

            await gateway.PublishAsync(
                chromeController.Current!.Backend,
                timeout.Token);
            await gateway.VerifyFrontendAsync(timeout.Token);

            using (var staleWebSocket = new ClientWebSocket())
            {
                await Assert.ThrowsExactlyAsync<WebSocketException>(
                    () => staleWebSocket.ConnectAsync(
                        firstWebSocketUri,
                        timeout.Token));
            }

            await coordinator.ActivateAsync(profile, timeout.Token);

            Assert.AreEqual(
                originalProcessId,
                chromeController.Current?.Process.Id);
            Assert.IsTrue(gateway.HasActiveBackend);
            Assert.AreEqual(
                CdpLifecycleStatus.Active,
                coordinator.State.Status);

            await coordinator.StopAsync(timeout.Token);
            Assert.AreEqual(
                CdpLifecycleStatus.Stopped,
                coordinator.State.Status);
            Assert.IsNull(coordinator.State.ManagedProfile);
            Assert.IsFalse(coordinator.State.IsChromeRunning);

            using var stoppedResponse = await httpClient.GetAsync(
                versionUri,
                timeout.Token);
            Assert.AreEqual(
                HttpStatusCode.ServiceUnavailable,
                stoppedResponse.StatusCode);
        }
        finally
        {
            try
            {
                await coordinator.ForceStopAsync(CancellationToken.None);
            }
            finally
            {
                try
                {
                    await gateway.DisposeAsync();
                }
                finally
                {
                    chromeController.Dispose();
                    await DeleteTestDirectoryAsync(testRoot);
                }
            }
        }
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

    private static async Task DeleteTestDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }
    }
}

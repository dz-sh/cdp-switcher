using System.Net;
using System.Net.Sockets;
using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class WindowsTcpPortOwnerResolverTests
{
    [TestMethod]
    public void Resolver_finds_the_process_that_owns_a_listener()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The product supports Windows only.");
            return;
        }

        using var listener = new TcpListener(
            IPAddress.Loopback,
            port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var resolver = new WindowsTcpPortOwnerResolver();

        var processId = resolver.FindListeningProcessId(endpoint.Port);

        Assert.AreEqual(
            Environment.ProcessId,
            processId);
    }
}

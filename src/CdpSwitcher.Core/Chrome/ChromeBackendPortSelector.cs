using System.Net;
using System.Net.Sockets;

namespace CdpSwitcher.Core.Chrome;

internal sealed class ChromeBackendPortSelector :
    IChromeBackendPortSelector
{
    public int Select(int excludedPort)
    {
        if (excludedPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(excludedPort));
        }

        while (true)
        {
            using var listener = new TcpListener(
                IPAddress.Loopback,
                port: 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            if (port != excludedPort)
            {
                return port;
            }
        }
    }
}

using System.Net;

namespace CdpSwitcher.Core.Gateway;

internal static class LoopbackAddress
{
    public static bool IsLoopbackHost(string host)
    {
        return string.Equals(
                   host,
                   "localhost",
                   StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out var address) &&
               IPAddress.IsLoopback(address);
    }
}

namespace CdpSwitcher.Core.Gateway;

public sealed class GatewayPortUnavailableException : IOException
{
    public GatewayPortUnavailableException(
        int port,
        IOException innerException)
        : base(
            $"Port {port} is unavailable. Close the app using it and restart.",
            innerException)
    {
        Port = port;
    }

    public int Port { get; }
}

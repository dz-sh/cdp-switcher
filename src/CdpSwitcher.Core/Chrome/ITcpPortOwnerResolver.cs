namespace CdpSwitcher.Core.Chrome;

public interface ITcpPortOwnerResolver
{
    int? FindListeningProcessId(int port);
}

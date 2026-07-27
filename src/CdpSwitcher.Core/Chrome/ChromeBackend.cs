namespace CdpSwitcher.Core.Chrome;

public sealed record ChromeBackend(
    int Port,
    Uri BrowserWebSocketUri,
    int OwningProcessId)
{
    public string BrowserWebSocketPath =>
        BrowserWebSocketUri.PathAndQuery;
}

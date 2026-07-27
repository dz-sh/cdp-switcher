namespace CdpSwitcher.Core.Chrome;

internal interface IChromeBackendPortSelector
{
    int Select(int excludedPort);
}

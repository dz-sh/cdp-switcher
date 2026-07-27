namespace CdpSwitcher.Core.Profiles;

public sealed record ProfileConfiguration(
    int FrontendPort,
    IReadOnlyList<ProfileCatalogEntry> Entries)
{
    public IReadOnlyList<BrowserProfile> VisibleProfiles =>
        Entries
            .Where(entry => entry.State == ProfileCatalogState.Visible)
            .Select(entry => entry.Profile)
            .ToArray();
}

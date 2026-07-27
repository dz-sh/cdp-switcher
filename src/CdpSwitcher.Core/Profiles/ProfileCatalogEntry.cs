namespace CdpSwitcher.Core.Profiles;

public sealed record ProfileCatalogEntry(
    BrowserProfile Profile,
    ProfileCatalogState State)
{
    public static ProfileCatalogEntry Create(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProfileCatalogEntry(
            profile,
            ProfileCatalogState.Visible);
    }

    public ProfileCatalogEntry WithProfile(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id != Profile.Id)
        {
            throw new ArgumentException(
                "The profile identifier cannot change.",
                nameof(profile));
        }

        return this with { Profile = profile };
    }

    public ProfileCatalogEntry WithState(ProfileCatalogState state)
    {
        return this with { State = state };
    }
}

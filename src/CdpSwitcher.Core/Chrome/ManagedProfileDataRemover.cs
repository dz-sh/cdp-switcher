namespace CdpSwitcher.Core.Chrome;

public sealed class ManagedProfileDataRemover
{
    private readonly ManagedProfilePaths _profilePaths;
    private readonly ChromeProfileUseDetector _profileUseDetector;

    public ManagedProfileDataRemover(
        ManagedProfilePaths profilePaths,
        ChromeProfileUseDetector profileUseDetector)
    {
        ArgumentNullException.ThrowIfNull(profilePaths);
        ArgumentNullException.ThrowIfNull(profileUseDetector);
        _profilePaths = profilePaths;
        _profileUseDetector = profileUseDetector;
    }

    public void Delete(Guid profileId)
    {
        var directory = _profilePaths.GetProfileDirectory(profileId);
        if (!Directory.Exists(directory))
        {
            return;
        }

        if (_profileUseDetector.IsInUse(directory))
        {
            throw new IOException(
                "This profile is open in Chrome. Close it and try again.");
        }

        Directory.Delete(directory, recursive: true);
    }
}

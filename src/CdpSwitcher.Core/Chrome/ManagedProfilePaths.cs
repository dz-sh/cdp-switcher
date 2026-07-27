using CdpSwitcher.Core.Profiles;

namespace CdpSwitcher.Core.Chrome;

public sealed class ManagedProfilePaths
{
    private readonly string _profileRootWithSeparator;

    public ManagedProfilePaths(string profileRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);

        ProfileRoot = Path.GetFullPath(profileRoot);
        _profileRootWithSeparator =
            Path.TrimEndingDirectorySeparator(ProfileRoot) +
            Path.DirectorySeparatorChar;
    }

    public string ProfileRoot { get; }

    public static ManagedProfilePaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return new ManagedProfilePaths(
            Path.Combine(localAppData, "CdpSwitcher", "Profiles"));
    }

    public string GetProfileDirectory(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetProfileDirectory(profile.Id);
    }

    public string GetProfileDirectory(Guid profileId)
    {
        var directory = Path.GetFullPath(
            Path.Combine(ProfileRoot, profileId.ToString("N")));

        if (!directory.StartsWith(
                _profileRootWithSeparator,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The managed profile directory is invalid.");
        }

        return directory;
    }

    public IReadOnlyList<UnlinkedProfileData> FindUnlinkedProfileData(
        IEnumerable<Guid> knownProfileIds)
    {
        ArgumentNullException.ThrowIfNull(knownProfileIds);
        if (!Directory.Exists(ProfileRoot))
        {
            return [];
        }

        var known = knownProfileIds.ToHashSet();
        var result = new List<UnlinkedProfileData>();
        foreach (var directory in Directory.EnumerateDirectories(
                     ProfileRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 32 ||
                !Guid.TryParseExact(name, "N", out var id) ||
                known.Contains(id))
            {
                continue;
            }

            result.Add(
                new UnlinkedProfileData(
                    id,
                    Directory.GetLastWriteTimeUtc(directory)));
        }

        return result
            .OrderByDescending(item => item.LastModifiedAt)
            .ToArray();
    }
}

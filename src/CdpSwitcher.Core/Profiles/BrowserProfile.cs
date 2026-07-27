namespace CdpSwitcher.Core.Profiles;

public sealed record BrowserProfile
{
    private readonly IReadOnlyList<ProfileTag> _tags;

    private BrowserProfile(
        Guid id,
        string name,
        IReadOnlyList<ProfileTag> tags)
    {
        Id = id;
        Name = name;
        _tags = tags;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<ProfileTag> Tags => _tags;

    public static BrowserProfile Create(string name)
    {
        return Create(name, []);
    }

    public static BrowserProfile Create(
        string name,
        IEnumerable<ProfileTag> tags)
    {
        return Restore(Guid.NewGuid(), name, tags);
    }

    public static BrowserProfile Restore(
        Guid id,
        string name)
    {
        return Restore(id, name, []);
    }

    public static BrowserProfile Restore(
        Guid id,
        string name,
        IEnumerable<ProfileTag> tags)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "A profile identifier cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tags);

        var normalizedTags = new List<ProfileTag>();
        var tagNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            if (!tagNames.Add(tag.Name))
            {
                throw new ArgumentException(
                    "Tag names must be unique within a profile.",
                    nameof(tags));
            }

            normalizedTags.Add(tag);
        }

        return new BrowserProfile(
            id,
            name.Trim(),
            normalizedTags.AsReadOnly());
    }

    public BrowserProfile Edit(
        string name,
        IEnumerable<ProfileTag> tags)
    {
        return Restore(Id, name, tags);
    }
}

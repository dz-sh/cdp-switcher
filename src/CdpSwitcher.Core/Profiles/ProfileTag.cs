namespace CdpSwitcher.Core.Profiles;

public sealed record ProfileTag
{
    private ProfileTag(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; }

    public string Color { get; }

    public static ProfileTag Create(string name, string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);

        var normalizedColor = color.Trim().ToUpperInvariant();
        if (normalizedColor.Length != 7 ||
            normalizedColor[0] != '#' ||
            normalizedColor
                .Skip(1)
                .Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A tag color must use #RRGGBB.",
                nameof(color));
        }

        return new ProfileTag(name.Trim(), normalizedColor);
    }
}

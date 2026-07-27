using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpSwitcher.Core.Profiles;

public sealed class ProfileStore
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultFrontendPort = 9222;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _configurationPath;

    public ProfileStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
    }

    public string ConfigurationPath => _configurationPath;

    public static ProfileStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new ProfileStore(
            Path.Combine(localAppData, "CdpSwitcher", "config.json"));
    }

    public async Task<ProfileConfiguration> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_configurationPath))
        {
            return new ProfileConfiguration(
                DefaultFrontendPort,
                []);
        }

        await using var stream = new FileStream(
            _configurationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        ConfigurationFile document;
        try
        {
            document =
                await JsonSerializer.DeserializeAsync<ConfigurationFile>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false) ??
                throw InvalidConfiguration();
        }
        catch (JsonException exception)
        {
            throw InvalidConfiguration(exception);
        }

        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.FrontendPort != DefaultFrontendPort ||
            document.Profiles is null)
        {
            throw InvalidConfiguration();
        }

        var entries =
            new List<ProfileCatalogEntry>(document.Profiles.Count);
        foreach (var storedProfile in document.Profiles)
        {
            if (storedProfile is null ||
                !Guid.TryParse(storedProfile.Id, out var id))
            {
                throw InvalidConfiguration();
            }

            try
            {
                if (storedProfile.Tags is null)
                {
                    throw InvalidConfiguration();
                }

                var tags = storedProfile.Tags
                    .Select(
                        tag => tag is null
                            ? throw InvalidConfiguration()
                            : ProfileTag.Create(
                                tag.Name ?? string.Empty,
                                tag.Color ?? string.Empty))
                    .ToArray();
                entries.Add(
                    new ProfileCatalogEntry(
                        BrowserProfile.Restore(
                            id,
                            storedProfile.Name ?? string.Empty,
                            tags),
                        ParseState(storedProfile.State)));
            }
            catch (ArgumentException)
            {
                throw InvalidConfiguration();
            }
        }

        ValidateEntries(entries);
        return new ProfileConfiguration(
            document.FrontendPort,
            entries);
    }

    public async Task SaveAsync(
        IReadOnlyCollection<ProfileCatalogEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ValidateEntries(entries);

        var directory = Path.GetDirectoryName(_configurationPath) ??
            throw new InvalidOperationException(
                "The configuration directory is invalid.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_configurationPath)}.{Guid.NewGuid():N}.tmp");
        var document = new ConfigurationFile
        {
            SchemaVersion = CurrentSchemaVersion,
            FrontendPort = DefaultFrontendPort,
            Profiles = entries
                .Select(
                    entry =>
                        (StoredProfile?)new StoredProfile
                        {
                            Id = entry.Profile.Id.ToString("N"),
                            Name = entry.Profile.Name,
                            State = FormatState(entry.State),
                            Tags = entry.Profile.Tags
                                .Select(
                                    tag =>
                                        (StoredTag?)new StoredTag
                                        {
                                            Name = tag.Name,
                                            Color = tag.Color,
                                        })
                                .ToList(),
                        })
                .ToList(),
        };

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(
                    cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_configurationPath))
            {
                File.Replace(
                    temporaryPath,
                    _configurationPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _configurationPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateEntries(
        IEnumerable<ProfileCatalogEntry> entries)
    {
        var identifiers = new HashSet<Guid>();
        var names = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry is null ||
                !Enum.IsDefined(entry.State) ||
                entry.Profile is null ||
                !identifiers.Add(entry.Profile.Id) ||
                !names.Add(entry.Profile.Name))
            {
                throw InvalidConfiguration();
            }
        }
    }

    private static ProfileCatalogState ParseState(string? value)
    {
        return value switch
        {
            "visible" => ProfileCatalogState.Visible,
            "removed" => ProfileCatalogState.Removed,
            _ => throw InvalidConfiguration(),
        };
    }

    private static string FormatState(ProfileCatalogState state)
    {
        return state switch
        {
            ProfileCatalogState.Visible => "visible",
            ProfileCatalogState.Removed => "removed",
            _ => throw InvalidConfiguration(),
        };
    }

    private static InvalidDataException InvalidConfiguration(
        Exception? innerException = null)
    {
        return new InvalidDataException(
            "The profile configuration is invalid.",
            innerException);
    }

    private sealed class ConfigurationFile
    {
        public ConfigurationFile()
        {
        }

        public int SchemaVersion { get; init; }

        public int FrontendPort { get; init; }

        public List<StoredProfile?>? Profiles { get; init; }
    }

    private sealed class StoredProfile
    {
        public StoredProfile()
        {
        }

        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? State { get; init; }

        public List<StoredTag?>? Tags { get; init; }
    }

    private sealed class StoredTag
    {
        public StoredTag()
        {
        }

        public string? Name { get; init; }

        public string? Color { get; init; }
    }
}

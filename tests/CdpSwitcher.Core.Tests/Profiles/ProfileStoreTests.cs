using System.Text.Json;
using CdpSwitcher.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Profiles;

[TestClass]
public sealed class ProfileStoreTests
{
    private string _testDirectory = null!;
    private string _configurationPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        _configurationPath = Path.Combine(
            _testDirectory,
            "config.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Missing_file_loads_an_empty_configuration()
    {
        var store = new ProfileStore(_configurationPath);

        var configuration = await store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileStore.DefaultFrontendPort,
            configuration.FrontendPort);
        Assert.AreEqual(0, configuration.Entries.Count);
    }

    [TestMethod]
    public async Task Save_and_load_preserve_profile_fields_and_tags()
    {
        var store = new ProfileStore(_configurationPath);
        var profiles = new[]
        {
            BrowserProfile.Create(
                "Automation",
                [
                    ProfileTag.Create("Work", "#2563EB"),
                    ProfileTag.Create("Admin", "#DC2626"),
                ]),
            BrowserProfile.Create("Personal"),
        };

        var entries = profiles
            .Select(ProfileCatalogEntry.Create)
            .ToArray();
        await store.SaveAsync(entries, CancellationToken.None);
        var configuration = await store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(2, configuration.Entries.Count);
        AssertProfileEqual(
            profiles[0],
            configuration.Entries[0].Profile);
        AssertProfileEqual(
            profiles[1],
            configuration.Entries[1].Profile);
        Assert.AreEqual(
            ProfileCatalogState.Visible,
            configuration.Entries[0].State);
        Assert.AreEqual(
            0,
            Directory.GetFiles(_testDirectory, "*.tmp").Length);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(_configurationPath));
        Assert.AreEqual(
            ProfileStore.CurrentSchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.IsFalse(
            document.RootElement
                .GetProperty("profiles")[0]
                .TryGetProperty("sensitivity", out _));
        Assert.AreEqual(
            "visible",
            document.RootElement
                .GetProperty("profiles")[0]
                .GetProperty("state")
                .GetString());
    }

    [TestMethod]
    public async Task Load_rejects_duplicate_profile_names()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "schemaVersion": 1,
              "frontendPort": 9222,
              "profiles": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Automation",
                  "state": "visible",
                  "tags": []
                },
                {
                  "id": "22222222222222222222222222222222",
                  "name": "automation",
                  "state": "removed",
                  "tags": []
                }
              ]
            }
            """);
        var store = new ProfileStore(_configurationPath);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Load_rejects_duplicate_tags_and_invalid_colors()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "schemaVersion": 1,
              "frontendPort": 9222,
              "profiles": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Automation",
                  "state": "visible",
                  "tags": [
                    { "name": "Work", "color": "#2563EB" },
                    { "name": "work", "color": "blue" }
                  ]
                }
              ]
            }
            """);
        var store = new ProfileStore(_configurationPath);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Load_rejects_a_null_profile_entry()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "schemaVersion": 1,
              "frontendPort": 9222,
              "profiles": [null]
            }
            """);
        var store = new ProfileStore(_configurationPath);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Load_reports_malformed_json_as_invalid_configuration()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """{"schemaVersion": 1, "profiles": [""");
        var store = new ProfileStore(_configurationPath);

        var exception =
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => store.LoadAsync(CancellationToken.None));

        Assert.IsInstanceOfType<JsonException>(
            exception.InnerException);
    }

    [TestMethod]
    public async Task Save_rejects_duplicate_profile_names()
    {
        var store = new ProfileStore(_configurationPath);
        var entries = new[]
        {
            ProfileCatalogEntry.Create(
                BrowserProfile.Create("Automation")),
            ProfileCatalogEntry
                .Create(BrowserProfile.Create("automation"))
                .WithState(ProfileCatalogState.Removed),
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.SaveAsync(
                entries,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Load_rejects_an_unknown_schema_version()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "schemaVersion": 2,
              "frontendPort": 9222,
              "profiles": []
            }
            """);
        var store = new ProfileStore(_configurationPath);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Save_and_load_preserve_removed_state()
    {
        var profile = BrowserProfile.Create("Archived");
        var store = new ProfileStore(_configurationPath);
        await store.SaveAsync(
            [
                ProfileCatalogEntry
                    .Create(profile)
                    .WithState(ProfileCatalogState.Removed),
            ],
            CancellationToken.None);

        var configuration = await store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileCatalogState.Removed,
            configuration.Entries[0].State);
        Assert.AreEqual(0, configuration.VisibleProfiles.Count);
    }

    [TestMethod]
    public async Task Load_rejects_an_invalid_catalog_state()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "schemaVersion": 1,
              "frontendPort": 9222,
              "profiles": [
                {
                  "id": "11111111111111111111111111111111",
                  "name": "Primary",
                  "state": "archived",
                  "tags": []
                }
              ]
            }
            """);
        var store = new ProfileStore(_configurationPath);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }

    private static void AssertProfileEqual(
        BrowserProfile expected,
        BrowserProfile actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.Tags.Count, actual.Tags.Count);
        for (var index = 0; index < expected.Tags.Count; index++)
        {
            Assert.AreEqual(
                expected.Tags[index],
                actual.Tags[index]);
        }
    }
}

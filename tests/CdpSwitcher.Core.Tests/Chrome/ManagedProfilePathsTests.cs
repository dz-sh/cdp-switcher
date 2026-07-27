using CdpSwitcher.Core.Chrome;
using CdpSwitcher.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ManagedProfilePathsTests
{
    [TestMethod]
    public void Profile_directory_is_derived_below_the_managed_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        var paths = new ManagedProfilePaths(root);
        var profile = BrowserProfile.Create("Automation");

        var directory = paths.GetProfileDirectory(profile);

        Assert.IsTrue(
            directory.StartsWith(
                Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            profile.Id.ToString("N"),
            Path.GetFileName(directory));
    }

    [TestMethod]
    public void Discovery_returns_only_unreferenced_guid_directories()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        var paths = new ManagedProfilePaths(root);
        var knownId = Guid.NewGuid();
        var unlinkedId = Guid.NewGuid();
        Directory.CreateDirectory(
            paths.GetProfileDirectory(knownId));
        Directory.CreateDirectory(
            paths.GetProfileDirectory(unlinkedId));
        Directory.CreateDirectory(
            Path.Combine(root, "not-a-profile"));

        try
        {
            var result = paths.FindUnlinkedProfileData([knownId]);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(unlinkedId, result[0].Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Discovery_does_not_create_the_managed_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        var paths = new ManagedProfilePaths(root);

        var result = paths.FindUnlinkedProfileData([]);

        Assert.AreEqual(0, result.Count);
        Assert.IsFalse(Directory.Exists(root));
    }
}

using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ManagedProfileDataRemoverTests
{
    [TestMethod]
    public void Delete_removes_only_the_derived_managed_directory()
    {
        var root = CreateTestRoot();
        var paths = new ManagedProfilePaths(root);
        var removedId = Guid.NewGuid();
        var retainedId = Guid.NewGuid();
        Directory.CreateDirectory(
            paths.GetProfileDirectory(removedId));
        Directory.CreateDirectory(
            paths.GetProfileDirectory(retainedId));
        File.WriteAllText(
            Path.Combine(
                paths.GetProfileDirectory(removedId),
                "marker.txt"),
            "test");
        var remover = new ManagedProfileDataRemover(
            paths,
            new ChromeProfileUseDetector());

        try
        {
            remover.Delete(removedId);

            Assert.IsFalse(
                Directory.Exists(
                    paths.GetProfileDirectory(removedId)));
            Assert.IsTrue(
                Directory.Exists(
                    paths.GetProfileDirectory(retainedId)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Delete_refuses_profile_data_held_by_chrome_lock()
    {
        var root = CreateTestRoot();
        var paths = new ManagedProfilePaths(root);
        var id = Guid.NewGuid();
        var directory = paths.GetProfileDirectory(id);
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, "lockfile");
        File.WriteAllText(lockPath, string.Empty);
        var remover = new ManagedProfileDataRemover(
            paths,
            new ChromeProfileUseDetector());

        try
        {
            using (new FileStream(
                       lockPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                Assert.ThrowsExactly<IOException>(
                    () => remover.Delete(id));
            }

            Assert.IsTrue(Directory.Exists(directory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Delete_is_a_no_op_when_data_does_not_exist()
    {
        var root = CreateTestRoot();
        var remover = new ManagedProfileDataRemover(
            new ManagedProfilePaths(root),
            new ChromeProfileUseDetector());

        remover.Delete(Guid.NewGuid());

        Assert.IsFalse(Directory.Exists(root));
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
    }
}

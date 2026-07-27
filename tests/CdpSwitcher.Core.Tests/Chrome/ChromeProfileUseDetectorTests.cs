using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ChromeProfileUseDetectorTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
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
    public void Detector_reports_a_Chrome_style_locked_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The product supports Windows only.");
            return;
        }

        var lockFile = Path.Combine(_testDirectory, "lockfile");
        using var chromeLock = new FileStream(
            lockFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.DeleteOnClose);
        var detector = new ChromeProfileUseDetector();

        Assert.IsTrue(detector.IsInUse(_testDirectory));
    }

    [TestMethod]
    public void Detector_ignores_an_unlocked_stale_file()
    {
        File.WriteAllText(
            Path.Combine(_testDirectory, "lockfile"),
            string.Empty);
        var detector = new ChromeProfileUseDetector();

        Assert.IsFalse(detector.IsInUse(_testDirectory));
    }
}

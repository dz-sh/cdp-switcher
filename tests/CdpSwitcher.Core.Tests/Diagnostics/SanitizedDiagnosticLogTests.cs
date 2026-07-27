using CdpSwitcher.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Diagnostics;

[TestClass]
public sealed class SanitizedDiagnosticLogTests
{
    private string _testDirectory = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CdpSwitcherTests",
            Guid.NewGuid().ToString("N"));
        _logPath = Path.Combine(
            _testDirectory,
            "diagnostics.jsonl");
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
    public void Log_records_only_sanitized_failure_fields()
    {
        var profileId = Guid.NewGuid();
        var log = new SanitizedDiagnosticLog(_logPath);

        var written = log.TryWrite(
            DiagnosticEvent.OperationFailed,
            profileId,
            TimeSpan.FromMilliseconds(123),
            new InvalidOperationException(
                "secret-token and private profile name"));

        Assert.IsTrue(written);
        var content = File.ReadAllText(_logPath);
        StringAssert.Contains(content, "\"event\":\"operation_failed\"");
        StringAssert.Contains(
            content,
            $"\"profileId\":\"{profileId:N}\"");
        StringAssert.Contains(
            content,
            "\"durationMilliseconds\":123");
        StringAssert.Contains(
            content,
            "\"errorCategory\":\"invalid_state\"");
        Assert.IsFalse(content.Contains(
            "secret-token",
            StringComparison.Ordinal));
        Assert.IsFalse(content.Contains(
            "private profile name",
            StringComparison.Ordinal));
        Assert.IsFalse(content.Contains(
            "InvalidOperationException",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Log_records_backend_loss_without_backend_details()
    {
        var profileId = Guid.NewGuid();
        var log = new SanitizedDiagnosticLog(_logPath);

        var written = log.TryWrite(
            DiagnosticEvent.BackendLost,
            profileId);

        Assert.IsTrue(written);
        var content = File.ReadAllText(_logPath);
        StringAssert.Contains(content, "\"event\":\"backend_lost\"");
        StringAssert.Contains(
            content,
            $"\"profileId\":\"{profileId:N}\"");
        Assert.IsFalse(content.Contains(
            "errorCategory",
            StringComparison.Ordinal));
    }
}

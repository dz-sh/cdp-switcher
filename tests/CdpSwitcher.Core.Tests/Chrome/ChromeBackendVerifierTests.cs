using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ChromeBackendVerifierTests
{
    [TestMethod]
    public async Task Verify_rejects_a_listener_owned_by_another_process()
    {
        using var verifier = new ChromeBackendVerifier(
            new StubPortOwnerResolver(processId: 42));

        await Assert.ThrowsExactlyAsync<ChromeBackendPortConflictException>(
            () => verifier.VerifyAsync(
                port: 51347,
                expectedProcessId: 43,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_waits_when_the_listener_is_not_ready()
    {
        using var verifier = new ChromeBackendVerifier(
            new StubPortOwnerResolver(processId: null));

        await Assert.ThrowsExactlyAsync<ChromeBackendNotReadyException>(
            () => verifier.VerifyAsync(
                port: 51347,
                expectedProcessId: 43,
                CancellationToken.None));
    }

    private sealed class StubPortOwnerResolver :
        ITcpPortOwnerResolver
    {
        private readonly int? _processId;

        public StubPortOwnerResolver(int? processId)
        {
            _processId = processId;
        }

        public int? FindListeningProcessId(int port)
        {
            return _processId;
        }
    }
}

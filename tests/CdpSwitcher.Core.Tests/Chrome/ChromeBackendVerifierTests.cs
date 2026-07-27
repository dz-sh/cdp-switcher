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
        var discovery = new DevToolsActivePort(
            51347,
            "/devtools/browser/example");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => verifier.VerifyAsync(
                discovery,
                expectedProcessId: 43,
                CancellationToken.None));
    }

    private sealed class StubPortOwnerResolver :
        ITcpPortOwnerResolver
    {
        private readonly int _processId;

        public StubPortOwnerResolver(int processId)
        {
            _processId = processId;
        }

        public int? FindListeningProcessId(int port)
        {
            return _processId;
        }
    }
}

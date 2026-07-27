using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ChromeBackendPortRetryTests
{
    [TestMethod]
    public async Task Execute_retries_only_port_conflicts()
    {
        var selector = new StubPortSelector(51001, 51002, 51003);
        var attemptedPorts = new List<int>();

        var result = await ChromeBackendPortRetry.ExecuteAsync(
            selector,
            excludedPort: 9222,
            (port, _) =>
            {
                attemptedPorts.Add(port);
                return attemptedPorts.Count < 3
                    ? Task.FromException<string>(
                        new ChromeBackendPortConflictException())
                    : Task.FromResult("ready");
            },
            CancellationToken.None);

        Assert.AreEqual("ready", result);
        CollectionAssert.AreEqual(
            new[] { 51001, 51002, 51003 },
            attemptedPorts);
    }

    [TestMethod]
    public async Task Execute_stops_after_three_port_conflicts()
    {
        var selector = new StubPortSelector(51001, 51002, 51003);

        await Assert.ThrowsExactlyAsync<
            ChromeBackendPortUnavailableException>(
            () => ChromeBackendPortRetry.ExecuteAsync(
                selector,
                excludedPort: 9222,
                (_, _) => Task.FromException<string>(
                    new ChromeBackendPortConflictException()),
                CancellationToken.None));

        Assert.AreEqual(3, selector.SelectionCount);
    }

    [TestMethod]
    public async Task Execute_does_not_retry_an_unrelated_failure()
    {
        var selector = new StubPortSelector(51001, 51002);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ChromeBackendPortRetry.ExecuteAsync(
                selector,
                excludedPort: 9222,
                (_, _) => Task.FromException<string>(
                    new InvalidOperationException("discovery failed")),
                CancellationToken.None));

        Assert.AreEqual(1, selector.SelectionCount);
    }

    private sealed class StubPortSelector : IChromeBackendPortSelector
    {
        private readonly Queue<int> _ports;

        public StubPortSelector(params int[] ports)
        {
            _ports = new Queue<int>(ports);
        }

        public int SelectionCount { get; private set; }

        public int Select(int excludedPort)
        {
            SelectionCount++;
            var port = _ports.Dequeue();
            Assert.AreNotEqual(excludedPort, port);
            return port;
        }
    }
}

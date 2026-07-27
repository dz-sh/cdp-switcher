using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ChromeBackendPortSelectorTests
{
    [TestMethod]
    public void Select_returns_a_non_zero_port_other_than_the_frontend()
    {
        var selector = new ChromeBackendPortSelector();

        var port = selector.Select(excludedPort: 9222);

        Assert.IsTrue(port is > 0 and <= 65535);
        Assert.AreNotEqual(9222, port);
    }
}

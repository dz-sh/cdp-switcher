using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class DevToolsActivePortTests
{
    [TestMethod]
    public void Parse_reads_port_and_browser_path()
    {
        var discovery = DevToolsActivePort.Parse(
            "51347\r\n/devtools/browser/example\r\n");

        Assert.AreEqual(51347, discovery.Port);
        Assert.AreEqual(
            "/devtools/browser/example",
            discovery.BrowserWebSocketPath);
    }

    [TestMethod]
    public void Parse_rejects_an_invalid_port()
    {
        Assert.ThrowsExactly<FormatException>(
            () => DevToolsActivePort.Parse(
                "70000\n/devtools/browser/example\n"));
    }

    [TestMethod]
    public void Parse_rejects_a_non_browser_path()
    {
        Assert.ThrowsExactly<FormatException>(
            () => DevToolsActivePort.Parse(
                "51347\n/devtools/page/example\n"));
    }
}

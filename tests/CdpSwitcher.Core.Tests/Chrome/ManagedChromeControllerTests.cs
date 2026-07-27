using CdpSwitcher.Core.Chrome;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Chrome;

[TestClass]
public sealed class ManagedChromeControllerTests
{
    [TestMethod]
    public void Launch_arguments_use_only_the_browser_fidelity_allowlist()
    {
        var startInfo = ManagedChromeController.CreateStartInfo(
            "chrome.exe",
            @"C:\profiles\example",
            backendPort: 51347);

        CollectionAssert.AreEqual(
            new[]
            {
                @"--user-data-dir=C:\profiles\example",
                "--remote-debugging-port=51347",
                "--remote-debugging-address=127.0.0.1",
                "--no-first-run",
                "--no-default-browser-check",
                "about:blank",
            },
            startInfo.ArgumentList.ToArray());
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsFalse(startInfo.ArgumentList.Any(
            argument =>
                argument.Contains(
                    "automation",
                    StringComparison.OrdinalIgnoreCase) ||
                argument.Contains(
                    "headless",
                    StringComparison.OrdinalIgnoreCase)));
    }
}

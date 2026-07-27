using System.Text;
using System.Text.Json;
using CdpSwitcher.Core.Gateway;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CdpSwitcher.Core.Tests.Gateway;

[TestClass]
public sealed class CdpDiscoveryRewriterTests
{
    [TestMethod]
    public void Rewrite_replaces_private_authority_and_registers_path()
    {
        const string path = "/devtools/browser/example";
        var registeredPaths = new List<string>();
        var input = Encoding.UTF8.GetBytes(
            $$"""{"webSocketDebuggerUrl":"ws://127.0.0.1:51347{{path}}"}""");

        var output = CdpDiscoveryRewriter.Rewrite(
            input,
            51347,
            9222,
            backendPath =>
            {
                registeredPaths.Add(backendPath);
                return $"/cdp/0000000000000001{backendPath}";
            });

        using var document = JsonDocument.Parse(output);
        Assert.AreEqual(
            $"ws://127.0.0.1:9222/cdp/0000000000000001{path}",
            document.RootElement
                .GetProperty("webSocketDebuggerUrl")
                .GetString());
        CollectionAssert.AreEqual(
            new[] { path },
            registeredPaths);
    }

    [TestMethod]
    public void Rewrite_handles_target_arrays()
    {
        const string path = "/devtools/page/target-1";
        var registeredPaths = new List<string>();
        var input = Encoding.UTF8.GetBytes(
            $$"""[{"id":"target-1","webSocketDebuggerUrl":"ws://localhost:51347{{path}}"}]""");

        var output = CdpDiscoveryRewriter.Rewrite(
            input,
            51347,
            9222,
            backendPath =>
            {
                registeredPaths.Add(backendPath);
                return $"/cdp/0000000000000002{backendPath}";
            });

        using var document = JsonDocument.Parse(output);
        Assert.AreEqual(
            $"ws://127.0.0.1:9222/cdp/0000000000000002{path}",
            document.RootElement[0]
                .GetProperty("webSocketDebuggerUrl")
                .GetString());
    }

    [TestMethod]
    public void Rewrite_rejects_a_different_backend_port()
    {
        var input = Encoding.UTF8.GetBytes(
            """{"webSocketDebuggerUrl":"ws://127.0.0.1:60000/devtools/browser/example"}""");

        Assert.ThrowsExactly<JsonException>(
            () => CdpDiscoveryRewriter.Rewrite(
                input,
                51347,
                9222,
                path => path));
    }
}

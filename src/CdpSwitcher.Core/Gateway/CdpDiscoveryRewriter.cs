using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpSwitcher.Core.Gateway;

public static class CdpDiscoveryRewriter
{
    public static byte[] Rewrite(
        ReadOnlySpan<byte> json,
        int backendPort,
        int frontendPort,
        Func<string, string> registerWebSocketPath)
    {
        ArgumentNullException.ThrowIfNull(registerWebSocketPath);

        var node = JsonNode.Parse(json) ??
            throw new JsonException("CDP discovery returned empty JSON.");
        RewriteNode(
            node,
            backendPort,
            frontendPort,
            registerWebSocketPath);

        return JsonSerializer.SerializeToUtf8Bytes(node);
    }

    private static void RewriteNode(
        JsonNode node,
        int backendPort,
        int frontendPort,
        Func<string, string> registerWebSocketPath)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (string.Equals(
                        property.Key,
                        "webSocketDebuggerUrl",
                        StringComparison.Ordinal) &&
                    property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var webSocketValue))
                {
                    jsonObject[property.Key] = RewriteWebSocketUrl(
                        webSocketValue,
                        backendPort,
                        frontendPort,
                        registerWebSocketPath);
                }
                else if (property.Value is not null)
                {
                    RewriteNode(
                        property.Value,
                        backendPort,
                        frontendPort,
                        registerWebSocketPath);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RewriteNode(
                        item,
                        backendPort,
                        frontendPort,
                        registerWebSocketPath);
                }
            }
        }
    }

    private static string RewriteWebSocketUrl(
        string value,
        int backendPort,
        int frontendPort,
        Func<string, string> registerWebSocketPath)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != "ws" ||
            !LoopbackAddress.IsLoopbackHost(uri.Host) ||
            uri.Port != backendPort ||
            !uri.AbsolutePath.StartsWith(
                "/devtools/",
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "CDP discovery returned an unexpected WebSocket URL.");
        }

        var frontendPath =
            registerWebSocketPath(uri.PathAndQuery);
        if (!frontendPath.StartsWith("/", StringComparison.Ordinal) ||
            frontendPath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The gateway returned an invalid WebSocket path.");
        }

        return $"ws://127.0.0.1:{frontendPort}{frontendPath}";
    }
}

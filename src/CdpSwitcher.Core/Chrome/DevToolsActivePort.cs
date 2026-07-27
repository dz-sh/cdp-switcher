using System.Globalization;

namespace CdpSwitcher.Core.Chrome;

public sealed record DevToolsActivePort(
    int Port,
    string BrowserWebSocketPath)
{
    public static DevToolsActivePort Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var lines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2 ||
            !int.TryParse(
                lines[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port) ||
            port is < 1 or > 65535)
        {
            throw new FormatException(
                "Chrome returned an invalid DevTools port.");
        }

        var path = lines[1].Trim();
        if (!path.StartsWith("/devtools/browser/", StringComparison.Ordinal) ||
            path.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new FormatException(
                "Chrome returned an invalid browser WebSocket path.");
        }

        return new DevToolsActivePort(port, path);
    }
}

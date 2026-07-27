using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CdpSwitcher.Core.Chrome;

namespace CdpSwitcher.Core.Diagnostics;

public sealed class SanitizedDiagnosticLog
{
    private const long MaximumLogBytes = 256 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _logPath;
    private readonly object _writeLock = new();

    public SanitizedDiagnosticLog(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _logPath = Path.GetFullPath(logPath);
    }

    public string LogPath => _logPath;

    public static SanitizedDiagnosticLog CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new SanitizedDiagnosticLog(
            Path.Combine(
                localAppData,
                "CdpSwitcher",
                "diagnostics.jsonl"));
    }

    public bool TryWrite(
        DiagnosticEvent diagnosticEvent,
        Guid? profileId = null,
        TimeSpan? duration = null,
        Exception? exception = null)
    {
        if (!Enum.IsDefined(diagnosticEvent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(diagnosticEvent));
        }

        var entry = new DiagnosticEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = GetEventName(diagnosticEvent),
            ProfileId = profileId?.ToString("N"),
            DurationMilliseconds = duration is null
                ? null
                : Math.Max(
                    0,
                    (long)duration.Value.TotalMilliseconds),
            ErrorCategory = GetErrorCategory(exception),
        };
        var line = JsonSerializer.Serialize(
            entry,
            SerializerOptions) + Environment.NewLine;

        lock (_writeLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logPath) ??
                    throw new InvalidOperationException(
                        "The diagnostics directory is invalid.");
                Directory.CreateDirectory(directory);

                var additionalBytes = Encoding.UTF8.GetByteCount(line);
                if (File.Exists(_logPath) &&
                    new FileInfo(_logPath).Length + additionalBytes >
                    MaximumLogBytes)
                {
                    File.WriteAllText(_logPath, string.Empty);
                }

                File.AppendAllText(_logPath, line);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    private static string GetEventName(DiagnosticEvent diagnosticEvent)
    {
        return diagnosticEvent switch
        {
            DiagnosticEvent.GatewayStopped => "gateway_stopped",
            DiagnosticEvent.GatewayStarting => "gateway_starting",
            DiagnosticEvent.GatewayActive => "gateway_active",
            DiagnosticEvent.GatewaySwitching => "gateway_switching",
            DiagnosticEvent.GatewayError => "gateway_error",
            DiagnosticEvent.OperationFailed => "operation_failed",
            DiagnosticEvent.ChromeExitedUnexpectedly =>
                "chrome_exited_unexpectedly",
            DiagnosticEvent.BackendLost => "backend_lost",
            _ => throw new ArgumentOutOfRangeException(
                nameof(diagnosticEvent)),
        };
    }

    private static string? GetErrorCategory(Exception? exception)
    {
        return exception switch
        {
            null => null,
            ChromeNotFoundException => "chrome_not_found",
            ChromeStartException => "chrome_start_failed",
            ChromeProfileInUseException => "profile_in_use",
            ManagedChromeDidNotCloseException => "chrome_close_timeout",
            OperationCanceledException => "timeout",
            UnauthorizedAccessException => "access_denied",
            InvalidDataException => "configuration_invalid",
            HttpRequestException => "cdp_unavailable",
            System.Net.WebSockets.WebSocketException =>
                "cdp_unavailable",
            System.Text.Json.JsonException =>
                "cdp_invalid_response",
            IOException => "io_failure",
            InvalidOperationException => "invalid_state",
            _ => "unexpected",
        };
    }

    private sealed class DiagnosticEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }

        public string Event { get; init; } = string.Empty;

        public string? ProfileId { get; init; }

        public long? DurationMilliseconds { get; init; }

        public string? ErrorCategory { get; init; }
    }
}

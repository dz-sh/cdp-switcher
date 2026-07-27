namespace CdpSwitcher.Core.Diagnostics;

public enum DiagnosticEvent
{
    GatewayStopped,
    GatewayStarting,
    GatewayActive,
    GatewaySwitching,
    GatewayError,
    OperationFailed,
    ChromeExitedUnexpectedly,
    BackendLost,
}

namespace CdpSwitcher.Core.Chrome;

internal static class ChromeBackendPortRetry
{
    internal const int MaximumAttempts = 3;

    internal static async Task<T> ExecuteAsync<T>(
        IChromeBackendPortSelector portSelector,
        int excludedPort,
        Func<int, CancellationToken, Task<T>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portSelector);
        ArgumentNullException.ThrowIfNull(attempt);

        for (var attemptNumber = 1;
             attemptNumber <= MaximumAttempts;
             attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var port = portSelector.Select(excludedPort);
            try
            {
                return await attempt(
                    port,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ChromeBackendPortConflictException)
            {
                // The completed attempt has already closed its Chrome process.
                if (attemptNumber == MaximumAttempts)
                {
                    break;
                }
            }
        }

        throw new ChromeBackendPortUnavailableException();
    }
}

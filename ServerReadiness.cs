namespace GgufLauncher;

/// <summary>
/// Waits for llama-server's /health endpoint and opens the browser when it answers. The wait
/// ends as soon as the server process exits or the user cancels, so a launch that fails in the
/// first seconds does not keep polling for the full deadline.
/// </summary>
public static class ServerReadiness
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultStillWaitingAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Builds the health URL relative to the browser base URL. Using Uri instead of string
    /// concatenation keeps the result correct whether or not the base URL ends with a slash.
    /// </summary>
    public static Uri HealthUri(Uri baseUri)
    {
        return new Uri(baseUri, "health");
    }

    /// <summary>
    /// Polls <paramref name="baseUri"/> + health until the server answers, the server exits,
    /// the caller cancels, or the deadline passes. <paramref name="handler"/> lets tests script
    /// the HTTP responses; production callers leave it null.
    /// </summary>
    public static async Task OpenWhenReadyAsync(
        Uri baseUri,
        Func<bool> isServerAlive,
        Func<Uri, bool> openBrowser,
        Action<string> log,
        HttpMessageHandler? handler = null,
        TimeSpan? deadline = null,
        TimeSpan? pollInterval = null,
        TimeSpan? stillWaitingAfter = null,
        CancellationToken cancellationToken = default)
    {
        Uri healthUri = HealthUri(baseUri);
        TimeSpan limit = deadline ?? DefaultDeadline;
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        TimeSpan waitingAfter = stillWaitingAfter ?? DefaultStillWaitingAfter;
        DateTime startedUtc = DateTime.UtcNow;
        bool announcedWaiting = false;

        using HttpClient client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = RequestTimeout;

        while (DateTime.UtcNow - startedUtc < limit)
        {
            if (cancellationToken.IsCancellationRequested || !isServerAlive())
            {
                return;
            }

            if (!announcedWaiting && DateTime.UtcNow - startedUtc >= waitingAfter)
            {
                log("still waiting for the server to answer /health ...");
                announcedWaiting = true;
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync(healthUri, cancellationToken);
                if (response.IsSuccessStatusCode && isServerAlive())
                {
                    openBrowser(baseUri);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Server is not accepting connections yet.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TaskCanceledException)
            {
                // Per-request timeout: the server is not ready yet.
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Null when the app answers at all (any status, a login redirect included); otherwise why it does not.
    private static async Task<string?> ProbeAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await http.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"tidak menjawab dalam {ProbeTimeout.TotalSeconds:0} detik";
        }
        catch (HttpRequestException ex)
        {
            // Shown inside parentheses that end the sentence: "(… actively refused it)."
            return (ex.InnerException?.Message ?? ex.Message).Trim().TrimEnd('.');
        }
    }
}

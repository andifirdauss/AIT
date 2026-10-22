namespace AIThread.Service.Testing;

public sealed partial class AppLauncher
{
    // Any response counts, a 404 or a redirect to the login page included: the app is up.
    private static async Task<bool> AnswersAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await http.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

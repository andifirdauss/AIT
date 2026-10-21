namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Null when the app answers at all (any status, a login redirect included); otherwise what the developer should do.
    private static async Task<string?> ProbeAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        string reason;
        try
        {
            using var response = await http.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            reason = $"tidak menjawab dalam {ProbeTimeout.TotalSeconds:0} detik";
        }
        catch (HttpRequestException ex)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
        }
        return $"Aplikasi tidak bisa dihubungi di {baseUrl} ({reason}). Jalankan aplikasinya dulu (mis. dotnet watch / npm run dev), lalu ulangi testing.";
    }
}

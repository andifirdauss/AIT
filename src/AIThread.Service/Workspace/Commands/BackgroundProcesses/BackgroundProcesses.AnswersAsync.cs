namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // Any HTTP answer counts: a 404 or 401 still means the server is up.
    private static async Task<bool> AnswersAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

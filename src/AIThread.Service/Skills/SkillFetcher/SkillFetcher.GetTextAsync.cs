namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response, url);
        if (response.Content.Headers.ContentLength > MaxTextBytes) throw new InvalidDataException("File atau halaman lebih dari 3 MB.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return text.Length > MaxTextBytes ? text[..MaxTextBytes] : text;
    }
}

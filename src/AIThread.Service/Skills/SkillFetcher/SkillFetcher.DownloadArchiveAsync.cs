using AIThread.Common.Localization;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private async Task DownloadArchiveAsync(string url, string destination, string? subPath, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response, url);
        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
            throw new InvalidDataException(Lang.T("Arsip repository lebih dari 200 MB.", "The repository archive is larger than 200 MB."));

        // ZipArchive needs a seekable stream; the archive goes to a temp file first.
        var temp = Path.Combine(Path.GetTempPath(), $"aithread-skill-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var file = File.Create(temp))
            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidDataException(Lang.T("Arsip repository lebih dari 200 MB.", "The repository archive is larger than 200 MB."));
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await using var zip = File.OpenRead(temp);
            SkillArchive.Extract(zip, destination, subPath);
        }
        finally
        {
            TryDelete(temp);
        }
    }
}

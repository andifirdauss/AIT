using AiThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    // Returns the page for a Web source (nothing is written then); null for the others.
    public async Task<WebPage?> FetchAsync(SkillSourceLocation location, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        switch (location.Kind)
        {
            case SkillSourceKind.GitHub:
            case SkillSourceKind.GitLab:
                try
                {
                    await DownloadArchiveAsync(location.DownloadUrl!, destination, location.SubPath, cancellationToken);
                }
                catch (HttpRequestException ex) when (location.CloneUrl is not null)
                {
                    // e.g. the API is rate limited or blocked by a proxy while git still works.
                    logger.LogInformation(ex, "Archive download of {Url} failed; trying git clone", location.Url);
                    ClearDirectory(destination);
                    await CloneAsync(location.CloneUrl!, location.Ref, location.SubPath, destination, cancellationToken);
                }
                return null;

            case SkillSourceKind.Git:
                await CloneAsync(location.CloneUrl!, location.Ref, location.SubPath, destination, cancellationToken);
                return null;

            case SkillSourceKind.RawFile:
                var text = await GetTextAsync(location.DownloadUrl!, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(destination, SkillDiscovery.EntryFileName), text, cancellationToken);
                return null;

            default:
                var html = await GetTextAsync(location.Url, cancellationToken);
                return new WebPage(HtmlToText(html), ExtractLinks(html, location.Url));
        }
    }
}

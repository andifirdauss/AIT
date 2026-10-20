using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Skills;

// Text and repository links of a web page source, for the skill orchestrator to read.
public sealed record WebPage(string Text, IReadOnlyList<string> Links);

// Downloads a source into an empty folder: ZIP archives from GitHub/GitLab (no git needed), git clone as the fallback
// and for other git hosts, a single Markdown file, or the text of a web page.
public sealed partial class SkillFetcher(IHttpClientFactory httpClientFactory, ILogger<SkillFetcher> logger) : ISkillFetcher
{
    public const string HttpClientName = "skills";

    private const long MaxArchiveBytes = 200L * 1024 * 1024;
    private const int MaxTextBytes = 3 * 1024 * 1024;
    private const int MaxPageChars = 60_000;
    private const int MaxPageLinks = 60;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(5);
}

using AiThread.Service.Skills;

namespace AiThread.Service.Interfaces;

public interface ISkillFetcher
{
    Task<WebPage?> FetchAsync(SkillSourceLocation location, string destination, CancellationToken cancellationToken);
}

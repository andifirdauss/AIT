using AIThread.Service.Skills;

namespace AIThread.Service.Interfaces;

public interface ISkillFetcher
{
    Task<WebPage?> FetchAsync(SkillSourceLocation location, string destination, CancellationToken cancellationToken);
}

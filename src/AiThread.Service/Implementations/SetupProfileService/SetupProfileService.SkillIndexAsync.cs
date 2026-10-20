namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Every skill of the store with the link of its source: how a profile names a skill.
    private async Task<IReadOnlyList<SkillEntry>> SkillIndexAsync(CancellationToken cancellationToken)
    {
        var urls = (await skillService.GetSourcesAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Url);
        return (await skillService.GetSkillsAsync(null, cancellationToken))
            .Select(s => new SkillEntry(s.Id, s.Name, urls.GetValueOrDefault(s.SourceId, string.Empty)))
            .ToList();
    }
}

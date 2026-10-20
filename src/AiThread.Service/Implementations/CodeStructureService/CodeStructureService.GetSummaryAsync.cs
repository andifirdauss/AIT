using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<CodeStructureSummaryDto> GetSummaryAsync(Guid projectId, bool rescan, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, rescan, cancellationToken);
        var parsed = snapshot.Files.Values.Where(f => f.Outline is not null).ToList();
        var languages = parsed
            .GroupBy(f => f.Language!)
            .Select(g => new StructureLanguageDto(g.Key, g.Count(), g.Sum(f => f.Outline!.Types.Count)))
            .OrderByDescending(l => l.Files)
            .ToList();
        return new CodeStructureSummaryDto(
            snapshot.Folders,
            snapshot.Files.Count,
            parsed.Count,
            snapshot.Files.Values.Count(f => f.ThirdParty),
            parsed.Sum(f => f.Outline!.Types.Count),
            parsed.Sum(f => f.Outline!.MemberCount),
            languages,
            snapshot.ScanMs,
            snapshot.Reparsed,
            snapshot.ScannedAt,
            snapshot.Truncated,
            await calls.CountAsync(projectId, cancellationToken));
    }
}

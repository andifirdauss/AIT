using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // Types first, then members, then file names; within each, exact name, then prefix, then substring.
    public async Task<IReadOnlyList<StructureSearchHitDto>> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default)
    {
        var text = query.Trim();
        if (text.Length < MinSearchLength) return [];
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var hits = new List<(int Rank, StructureSearchHitDto Hit)>();
        foreach (var file in snapshot.Files.Values)
        {
            if (Rank(Path.GetFileName(file.Path), text) is { } fileRank)
                hits.Add((20 + fileRank, new StructureSearchHitDto("file", Path.GetFileName(file.Path), file.Language, file.Path, 1)));
            if (file.Outline is not { } outline) continue;
            foreach (var type in outline.Types)
            {
                if (Rank(type.Name, text) is { } typeRank)
                    hits.Add((typeRank, new StructureSearchHitDto("type", type.Name, $"{type.Kind}{(type.Namespace is null ? string.Empty : " · " + type.Namespace)}", file.Path, type.Line)));
                foreach (var member in type.Members)
                {
                    if (Rank(member.Name, text) is { } memberRank)
                        hits.Add((10 + memberRank, new StructureSearchHitDto("member", member.Name, $"{type.Name} · {member.Kind}", file.Path, member.Line)));
                }
            }
            foreach (var function in outline.Functions)
            {
                if (Rank(function.Name, text) is { } functionRank)
                    hits.Add((10 + functionRank, new StructureSearchHitDto("member", function.Name, function.Kind, file.Path, function.Line)));
            }
        }
        return hits
            .OrderBy(h => h.Rank)
            .ThenBy(h => h.Hit.Name.Length)
            .ThenBy(h => h.Hit.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchHits)
            .Select(h => h.Hit)
            .ToList();
    }
}

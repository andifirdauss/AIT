using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Skills;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // One change entry from the commit (short memory, sourced by the commit's ingest id), and the text the business
    // wiki writer reads about it. Nulls when the model found nothing worth keeping. Throws when the model or the store
    // fails.
    private async Task<(Guid? MemoryId, string? Digest)> SummarizeAsync(ProjectContext project, CommitInfo commit, Guid ingestId,
        CancellationToken cancellationToken)
    {
        var show = await GitProcess.RunAsync(project.RootPath, ["show", "--format=", "--patch", "--no-color", "--no-ext-diff", "-U2", "--relative", commit.Sha],
            cancellationToken);
        var diff = show.Output.Length > MaxDiffChars ? show.Output[..MaxDiffChars] + "\n… (diff cut)" : show.Output;
        // The commit leaves this machine for the model: secrets in it stay masked, like everywhere else in AIT.
        diff = secrets.Redact(diff);
        var masked = commit with { Message = secrets.Redact(commit.Message) };

        IReadOnlyList<CodeMapHit> map = [];
        try
        {
            map = await codeMapper.MapQuicklyAsync(project, commit.Files.Select(f => f.Path).Take(MaxMappedFiles), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map for commit {Commit} failed", commit.Sha);
        }
        var modules = await registry.ListAsync(project.Key!, cancellationToken: cancellationToken);
        var existing = await memoryService.GetModuleMemoryAsync(project.ProjectId,
            map.Select(h => h.Module).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(), 2, cancellationToken);
        var language = TextLanguage.Guess([masked.Message]) ?? TextLanguage.Ui;

        var (json, _) = await ai.AskJsonAsync(AgentPrompts.CommitExtractor, BuildInput(masked, diff, language, map, modules, existing), MaxTokens, cancellationToken);
        if (json?["entry"] is not JsonObject item || ChangeLog.ParseEntry(item) is not { } entry) return (null, null);

        var module = SkillAi.Text(item["module"])
            ?? map.GroupBy(h => h.Module, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (module is null) return (null, null);
        var otherModules = MemoryAi.Strings(item["other_modules"]);
        var memoryModules = otherModules.Prepend(module).ToList();
        var fromMap = map.Where(h => memoryModules.Contains(h.Module, StringComparer.OrdinalIgnoreCase)).ToList();
        var areas = fromMap.Select(h => h.Area).Distinct().ToList();
        var changed = commit.Files.Select(f => new ChangeLogFile(ChangeLog.NormalizePath(f.Path), f.Removed ? ChangeLog.Deleted : ChangeLog.Modified, null)).ToList();
        entry = entry with { Files = ChangeLog.MergeFiles(changed, entry.Files) };
        var keywords = MemoryAi.Strings(item["keywords"]) is { Count: > 0 } words ? words : MemoryLabels.Tokens(entry.Title, 8);
        var description = ChangeLog.Render(entry, language == TextLanguage.English);
        if (TextLanguage.IsForeign(description)) return (null, null);

        var saved = await memoryService.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, project.ProjectId, module, MemoryKind.Change, keywords,
            secrets.Redact(description), fromMap.Select(h => h.Layer).Distinct().ToList(), areas is [{ } area] ? area : null, otherModules), ingestId, cancellationToken);

        var digest = $"Commit {commit.Sha[..Math.Min(10, commit.Sha.Length)]} by {commit.Author} on {commit.CommittedAt:yyyy-MM-dd}, made outside AIT.\n"
                     + $"Message: {Clip(masked.Message, 1_000)}\n"
                     + $"Change log entry:\n{Clip(secrets.Redact(description), 5_000)}\n";
        return (saved.Id, Clip(digest, Common.Entities.IngestedCommit.MaxDigestLength));
    }
}

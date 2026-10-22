using System.Text;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class ImpersonationDetector
{
    public async Task<ImpersonationInfo?> DetectAsync(ProjectContext project, string by, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(project.Key)) return null;
        var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
        var hits = await Task.Run(() => ImpersonationScanner.Scan(scope, cancellationToken), cancellationToken);
        var evidence = hits.Select(h => $"{h.File}:{h.Line}").Take(MaxEvidence).ToList();

        ImpersonationInfo info;
        if (hits.Count == 0)
        {
            info = new ImpersonationInfo(ImpersonationInfo.None, null, null, [], [], ImpersonationInfo.FromScan, false, DateTime.UtcNow, by);
        }
        else if (!await ai.IsAvailableAsync(cancellationToken))
        {
            info = new ImpersonationInfo(ImpersonationInfo.Unknown, null, null, [], evidence, ImpersonationInfo.FromScan, false, DateTime.UtcNow, by);
        }
        else
        {
            var input = new StringBuilder();
            foreach (var hit in hits) input.Append($"## {hit.File} (line {hit.Line})\n```\n{hit.Excerpt}```\n\n");
            JsonNode? json = null;
            try
            {
                (json, _) = await ai.AskJsonAsync(AgentPrompts.ImpersonationDetector, input.ToString(), MaxAnswerTokens, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Login-as detection of {Project}: the cheap model did not answer.", project.Name);
            }
            info = Read(json, evidence, by);
        }

        await store.SaveImpersonationAsync(project.Key, info, by, cancellationToken);
        return info;
    }

    // The model's answer; one that is unreadable, or claims "available" without saying how, stays unknown.
    private static ImpersonationInfo Read(JsonNode? json, IReadOnlyList<string> scanned, string by)
    {
        if (json is not JsonObject answer)
            return new ImpersonationInfo(ImpersonationInfo.Unknown, null, null, [], scanned, ImpersonationInfo.FromScan, false, DateTime.UtcNow, by);

        var available = answer["available"] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        var how = PlannerToolRunner.Str(answer, "how")?.Trim();
        var back = PlannerToolRunner.Str(answer, "back")?.Trim();
        var roles = MemoryAi.Strings(answer["roles"]).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var evidence = MemoryAi.Strings(answer["evidence"]).Select(e => e.Trim()).Where(e => e.Length > 0).Take(MaxEvidence).ToList();
        if (evidence.Count == 0) evidence = [.. scanned];

        var status = !available ? ImpersonationInfo.None : string.IsNullOrEmpty(how) ? ImpersonationInfo.Unknown : ImpersonationInfo.Available;
        return new ImpersonationInfo(status, status == ImpersonationInfo.Available ? how : null, string.IsNullOrEmpty(back) ? null : back, roles, evidence,
            ImpersonationInfo.FromScan, false, DateTime.UtcNow, by);
    }
}

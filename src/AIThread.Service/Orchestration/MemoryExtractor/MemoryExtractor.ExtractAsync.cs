using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    public async Task<MemoryExtraction> ExtractAsync(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        // The change log belongs to a project; a thread whose project was deleted meanwhile keeps nothing.
        if (thread.ProjectId is not { } projectId) return new MemoryExtraction(0, 0);
        var tokens = 0;
        try
        {
            var project = await projectService.GetContextAsync(projectId, cancellationToken);
            var changed = await ChangedFilesAsync(thread.Id, execution, cancellationToken);
            var map = await MapFilesAsync(project, thread, plan, changed, cancellationToken);
            var modules = string.IsNullOrEmpty(project?.Key) ? [] : await registry.ListAsync(project.Key, cancellationToken: cancellationToken);
            var changedModules = map.Where(h => changed.Any(f => string.Equals(f.Path, h.Path, StringComparison.OrdinalIgnoreCase))).Select(h => h.Module);
            var existing = await memoryService.GetModuleMemoryAsync(projectId,
                changedModules.Concat(map.Select(h => h.Module)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(), ExistingChanges, cancellationToken);

            // Counts against the node's Max paralel like any other call to it (usually the wrapper node), and falls
            // back like it.
            // The words of the request when they tell, else the UI language.
            var language = TextLanguage.Guess([thread.Prompt, plan.Summary]) ?? TextLanguage.Ui;
            var input = BuildInput(thread, plan, execution, finalText, changed, map, modules, existing, language);
            var reply = await caller.ChatAsync(node, candidate => Task.FromResult(new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages =
                [
                    ChatMessages.System(AgentPrompts.MemoryExtractor),
                    ChatMessages.User(input)
                ],
                Temperature = 0.2,
                MaxTokens = Math.Min(candidate.MaxTokens, 4096)
            }), cancellationToken, onSwitch: s =>
            {
                activityLog.Warn("memory", Lang.T($"Ekstraksi memory: `{s.From.Model}` tidak tersedia ({s.Reason}), beralih ke `{s.To.Model}`.", $"Memory extraction: `{s.From.Model}` not available ({s.Reason}), switching to `{s.To.Model}`."), thread.Id, thread.ProjectId);
                return Task.CompletedTask;
            });

            tokens = reply.Result.TotalTokens;
            if (Parse(reply.Result.Content) is not { } extracted) return new MemoryExtraction(0, tokens);

            // The module from the model when it gave one, else the module most changed files belong to.
            var module = extracted.Module
                ?? map.Where(h => changed.Any(f => string.Equals(f.Path, h.Path, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(h => h.Module, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
            if (module is null) return new MemoryExtraction(0, tokens);

            var memoryModules = extracted.OtherModules.Prepend(module).ToList();
            var fromMap = map.Where(h => memoryModules.Contains(h.Module, StringComparer.OrdinalIgnoreCase)).ToList();
            var layers = fromMap.Select(h => h.Layer).Distinct().ToList();
            var areas = fromMap.Select(h => h.Area).Distinct().ToList();
            var area = areas is [{ } single] ? single : null;
            var entry = extracted.Entry with { Files = ChangeLog.MergeFiles(changed, extracted.Entry.Files) };
            var keywords = extracted.Keywords.Count > 0 ? extracted.Keywords : MemoryLabels.Tokens(entry.Title, 8);
            var description = ChangeLog.Render(entry, language == TextLanguage.English);
            if (TextLanguage.IsForeign(description))
            {
                activityLog.Warn("memory", Lang.T("Catatan perubahan tidak disimpan: model menulisnya dalam bahasa lain.", "Change entry not saved: the model wrote it in another language."), thread.Id, thread.ProjectId);
                return new MemoryExtraction(0, tokens);
            }
            try
            {
                // Memory outlives the thread and may be shared with the team: a secret the model repeated stays masked.
                var saved = await memoryService.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, projectId, module, MemoryKind.Change, keywords,
                    secrets.Redact(description), layers, area, extracted.OtherModules), thread.Id, cancellationToken);
                activityLog.Info("memory",
                    Lang.T($"Catatan perubahan baru dari thread ini ({(saved.Level == MemoryLevel.Short ? "short" : "long")} memory): {saved.Label} — {entry.Title}", $"New change entry from this thread ({(saved.Level == MemoryLevel.Short ? "short" : "long")} memory): {saved.Label} — {entry.Title}"),
                    thread.Id, thread.ProjectId);
                return new MemoryExtraction(1, tokens);
            }
            catch (AppValidationException ex)
            {
                logger.LogInformation("Extracted change entry skipped: {Reason}", ex.Message);
                return new MemoryExtraction(0, tokens);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory extraction failed for thread {ThreadId}", thread.Id);
            activityLog.Warn("memory", Lang.T($"Ekstraksi memory gagal: {ex.Message}", $"Memory extraction failed: {ex.Message}"), thread.Id, thread.ProjectId);
            return new MemoryExtraction(0, tokens, Failed: true);
        }
    }
}

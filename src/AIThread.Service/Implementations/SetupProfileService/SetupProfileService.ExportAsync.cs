using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    public async Task<SetupProfileFile> ExportAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(string.Empty, cancellationToken);
        var nodes = await nodeService.GetAllAsync(cancellationToken);
        var storage = await memoryStorage.GetAsync(cancellationToken);
        var business = await businessMemory.GetSettingsAsync(cancellationToken);
        var skillSettings = await skillService.GetSettingsAsync(cancellationToken);
        var sources = await skillService.GetSourcesAsync(cancellationToken);
        var skills = await SkillIndexAsync(cancellationToken);

        var profileNodes = nodes
            .OrderBy(n => n.Role).ThenBy(n => n.Priority).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => new SetupProfileNode(n.Name, n.Role, n.Model, n.Temperature, n.MaxTokens, n.MaxConcurrency, n.Priority, n.SystemPrompt,
                n.IsActive,
                n.FallbackNodeName,
                n.Role == NodeRole.Planner && n.ToolDelegations is { Count: > 0 } delegations
                    ? delegations.Select(d => new SetupProfileDelegation(d.Tool, d.ToolsNodeName)).ToList()
                    : null,
                n.Skills is { Count: > 0 } nodeSkills
                    ? nodeSkills.Select(s => new SetupProfileSkillRef(s.Name, skills.FirstOrDefault(k => k.Id == s.Id)?.SourceUrl)).ToList()
                    : null,
                n.Role == NodeRole.Planner ? n.AutoTesting : null))
            .ToList();

        return new SetupProfileFile(
            SetupProfileFile.FormatName,
            SetupProfileFile.CurrentVersion,
            DateTime.UtcNow,
            AppVersion(),
            new SetupProfileRouter(settings.NineRouterBaseUrl),
            new SetupProfileOrchestration(settings.OrchestrationEnabled, settings.StreamProgressToClient, settings.PlannerCanAsk,
                settings.PassthroughModel, settings.PlannerMaxSteps, settings.ExecutorMaxRetries, settings.MaxParallelTasks,
                settings.RequestTimeoutSeconds, settings.BuildEnabled, settings.BuildTimeoutSeconds, settings.MaxFixRounds),
            profileNodes,
            new SetupProfileMemory(ToProfileStore(storage.ShortMemory), ToProfileStore(storage.LongMemory), ToProfileStore(storage.BusinessMemory)),
            new SetupProfileBusiness(business.Enabled, business.IntervalHours, business.Model),
            new SetupProfileSkills(skillSettings.Model, skillSettings.MaxPromptChars, sources.Select(s => s.Url).ToList()));
    }
}

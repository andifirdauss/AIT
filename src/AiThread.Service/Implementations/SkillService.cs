using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed class SkillService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ISettingService settingService,
    SkillPaths paths,
    SkillImportQueue queue,
    SkillImportProgress progress,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<SkillService> logger) : ISkillService
{
    private const int MaxDetailFiles = 200;

    public async Task<SkillSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return new SkillSettingsDto(settings.SkillOrchestratorModel, settings.SkillPromptMaxChars, paths.Root);
    }

    public async Task<SkillSettingsDto> UpdateSettingsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateSkillsAsync(request, cancellationToken);
        activityLog.Info("skill", $"Setting Skill Orchestrator disimpan (model: {settings.SkillOrchestratorModel ?? "otomatis — node Planner"}).");
        return new SkillSettingsDto(settings.SkillOrchestratorModel, settings.SkillPromptMaxChars, paths.Root);
    }

    public async Task<IReadOnlyList<SkillSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.SkillSources.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        return sources.Select(ToDto).ToList();
    }

    public async Task<SkillSourceDto> AddSourceAsync(AddSkillSourceRequest request, CancellationToken cancellationToken = default)
    {
        var location = SkillSourceLocator.Locate(request.Url);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.SkillSources.AnyAsync(s => s.Url == location.Url, cancellationToken))
            throw new AppValidationException("Link ini sudah ada di gudang skill. Klik Sync untuk menarik ulang.");

        var source = new SkillSource
        {
            Url = location.Url,
            Name = location.Name,
            Kind = location.Kind,
            Ref = location.Ref,
            SubPath = location.SubPath
        };
        db.SkillSources.Add(source);
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(source.Id);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = source.Id, status = source.Status.ToString(), action = "added" }));
        activityLog.Info("skill", $"Sumber skill {source.Name} ditambahkan dan masuk antrean.");
        return ToDto(source);
    }

    public async Task<SkillSourceDto?> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null) return null;
        if (source.Status is SkillSourceStatus.Queued or SkillSourceStatus.Fetching or SkillSourceStatus.Analyzing)
            throw new AppValidationException("Sumber ini sedang dalam antrean atau sedang ditarik.");

        source.Status = SkillSourceStatus.Queued;
        source.Error = null;
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(source.Id);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = source.Id, status = source.Status.ToString() }));
        return ToDto(source);
    }

    public async Task<bool> DeleteSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null) return false;

        var attached = await db.NodeSkills.CountAsync(ns => ns.Skill!.SkillSourceId == id, cancellationToken);
        db.SkillSources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            SkillPaths.DeleteDirectory(paths.SourceDir(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Folder of skill source {SourceId} could not be deleted", id);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = id, action = "deleted" }));
        if (attached > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { action = "skills" }));
        activityLog.Info("skill", $"Sumber skill {source.Name} dihapus{(attached > 0 ? $" (dilepas dari {attached} lampiran node)" : string.Empty)}.");
        return true;
    }

    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skills = db.Skills.AsNoTracking();
        foreach (var term in (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            skills = skills.Where(s => s.Name.Contains(term) || s.Description.Contains(term)
                                      || (s.Tags != null && s.Tags.Contains(term)) || s.SkillSource!.Name.Contains(term));
        }

        return await skills
            .OrderBy(s => s.SkillSource!.Name).ThenBy(s => s.Name)
            .Select(s => new SkillDto(s.Id, s.SkillSourceId, s.SkillSource!.Name, s.Name, s.Description, s.Tags, s.RelativeDir, s.Nodes.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<SkillDetailDto?> GetSkillAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skill = await db.Skills.AsNoTracking()
            .Include(s => s.SkillSource)
            .Include(s => s.Nodes).ThenInclude(n => n.AiNode)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (skill is null) return null;

        var folder = paths.SkillDir(skill.SkillSourceId, skill.RelativeDir);
        return new SkillDetailDto(
            skill.Id,
            skill.SkillSourceId,
            skill.SkillSource?.Name ?? string.Empty,
            skill.SkillSource?.Url ?? string.Empty,
            skill.Name,
            skill.Description,
            skill.Tags,
            folder,
            skill.EntryFile,
            skill.Content,
            SkillDiscovery.ListFiles(folder, MaxDetailFiles),
            skill.Nodes.Select(n => n.AiNode?.Name).OfType<string>().OrderBy(n => n).ToList());
    }

    private SkillSourceDto ToDto(SkillSource s) =>
        new(s.Id, s.Url, s.Name, s.Kind, s.Ref, s.SubPath, s.Status, s.Error, s.SkillCount, s.LastPulledAt, s.CreatedAt, progress.Get(s.Id));
}

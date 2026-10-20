using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Skills;

// Pulls one source: download into a staging folder, find the skills (SKILL.md files, or the skill orchestrator's
// pick when there are none), swap the staging folder in and upsert the skills. Skills keep their id across re-pulls
// (keyed by folder and entry file), so the nodes they are attached to keep them.
public sealed partial class SkillImporter(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ISkillPaths paths,
    ISkillFetcher fetcher,
    ISkillCatalogAgent catalog,
    ISkillImportQueue queue,
    ISkillImportProgress progress,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<SkillImporter> logger) : ISkillImporter
{
    private const int MaxChildSources = 5;
}

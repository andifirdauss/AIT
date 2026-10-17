using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

// Brings memory written before the structured label (or by an older AIT sharing the store) to the new label, in this
// order: backup (once per label version) → business wiki modules and labels normalized (they seed the module list) →
// code map scan of the projects registered here → relabeling in batches by the model → duplicate module check.
// Every row is claimed before it is sent (RelabelAttempts, conditional update), so several machines sharing a store
// never label the same row twice; after MaxAttempts failures a row waits in Memory → Perlu review instead.
public sealed class MemoryRelabeler(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    ModuleRegistry registry,
    CodeMapper codeMapper,
    ModuleMerger merger,
    MemoryAi ai,
    IMemoryTransferService transfer,
    IProjectService projectService,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<MemoryRelabeler> logger)
{
    public const int MaxAttempts = 3;
    private const int BatchSize = 20;
    private const int MaxBatchesPerRun = 25;
    private const int ScanMaxFiles = 3_000;
    private const int MaxTokens = 6_000;

    private static readonly JsonSerializerOptions BackupJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _phase;
    private volatile string? _lastMessage;
    private volatile string? _backupPath;
    private DateTime? _lastRunAt;

    public static string BackupDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread", "backup");

    public async Task<MemoryRelabelStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        int pending = 0, labeled = 0, review = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            pending = await db.LongMemories.CountAsync(m => m.LabelVersion == null && m.ReviewReason == null, cancellationToken);
            labeled = await db.LongMemories.CountAsync(m => m.LabelVersion != null, cancellationToken);
            review = await db.LongMemories.CountAsync(m => m.ReviewReason != null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories for the relabel status");
        }
        foreach (var record in await ShortRecordsAsync())
        {
            if (record.ReviewReason is not null) review++;
            else if (record.LabelVersion is null) pending++;
            else labeled++;
        }
        return new MemoryRelabelStatusDto(_gate.CurrentCount == 0, _phase, pending, labeled, review, _lastRunAt, _lastMessage, _backupPath ?? ExistingBackup());
    }

    // One full pass. modulesOnly: only the duplicate check of modules created since the last pass.
    public async Task RunAsync(bool modulesOnly, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)) return;
        try
        {
            Publish("relabel");
            var relabeledKeys = new HashSet<string>(StringComparer.Ordinal);
            if (!modulesOnly)
            {
                var pending = await CountPendingAsync(cancellationToken);
                if (pending > 0)
                {
                    _phase = "Backup";
                    if (!await BackupAsync(cancellationToken)) return;
                }

                _phase = "Wiki bisnis";
                await NormalizeWikiAsync(cancellationToken);

                if (pending > 0)
                {
                    _phase = "Peta kode";
                    await ScanProjectsAsync(cancellationToken);

                    _phase = "Relabel";
                    var (labeled, reviewed, keys) = await RelabelAsync(cancellationToken);
                    if (labeled + reviewed > 0)
                    {
                        _lastMessage = $"{labeled} memory dilabeli ulang, {reviewed} perlu review.";
                        activityLog.Info("memory", $"Migrasi label memory: {_lastMessage}");
                    }
                    relabeledKeys.UnionWith(keys);
                }
            }

            _phase = "Cek modul ganda";
            await CheckModulesAsync(relabeledKeys, cancellationToken);
            _lastRunAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastMessage = $"Migrasi label berhenti: {ex.Message}";
            logger.LogWarning(ex, "Memory relabeling stopped.");
        }
        finally
        {
            _phase = null;
            _gate.Release();
            Publish("relabel");
        }
    }

    private async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        var count = (await ShortRecordsAsync()).Count(r => r.LabelVersion is null && r.ReviewReason is null);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            count += await db.LongMemories.CountAsync(m => m.LabelVersion == null && m.ReviewReason == null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories waiting for a label");
        }
        return count;
    }

    // F1: everything as it is before the first relabeling, once per label version. No backup, no relabeling.
    private async Task<bool> BackupAsync(CancellationToken cancellationToken)
    {
        if (ExistingBackup() is { } existing)
        {
            _backupPath = existing;
            return true;
        }

        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var file = await transfer.ExportAsync(new MemoryQuery(shortStore.IsConnected ? null : MemoryLevel.Long, null, null, null), cancellationToken);

            List<object> pages;
            await using (var business = await businessStore.CreateAsync(cancellationToken))
                pages = (await business.BusinessPages.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Module, p.Labels }).ToListAsync(cancellationToken))
                    .Cast<object>().ToList();

            var memoryPath = Path.Combine(BackupDirectory, $"memory-before-labels-v{MemoryLabels.CurrentVersion}-{stamp}.json");
            await File.WriteAllBytesAsync(memoryPath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(file, BackupJson), cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(BackupDirectory, $"business-labels-before-v{MemoryLabels.CurrentVersion}-{stamp}.json"),
                JsonSerializer.SerializeToUtf8Bytes(pages, BackupJson), cancellationToken);
            File.Move(memoryPath + ".tmp", memoryPath);

            _backupPath = memoryPath;
            activityLog.Info("memory", $"Backup memory sebelum migrasi label: {file.Items.Count} memory → {memoryPath}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastMessage = $"Backup memory gagal, migrasi label ditunda: {ex.Message}";
            activityLog.Warn("memory", _lastMessage);
            logger.LogWarning(ex, "Memory backup before relabeling failed.");
            return false;
        }
    }

    private static string? ExistingBackup()
    {
        if (!Directory.Exists(BackupDirectory)) return null;
        return Directory.GetFiles(BackupDirectory, $"memory-before-labels-v{MemoryLabels.CurrentVersion}-*.json").OrderDescending().FirstOrDefault();
    }

    // F3b (and the seed of the module list): every wiki page's module resolved against the registry, its labels in
    // PascalCase. Only metadata changes, so no revision is written.
    private async Task NormalizeWikiAsync(CancellationToken cancellationToken)
    {
        List<BusinessPage> pages;
        try
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            pages = await business.BusinessPages.AsNoTracking()
                .Select(p => new BusinessPage { Id = p.Id, ProjectKey = p.ProjectKey, Module = p.Module, Labels = p.Labels, Summary = p.Summary, Title = p.Title })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the business wiki for label normalization.");
            return;
        }

        var changed = 0;
        foreach (var group in pages.GroupBy(p => p.ProjectKey))
        {
            var modules = await registry.ListAsync(group.Key, includeMerged: true, cancellationToken);
            foreach (var page in group)
            {
                var module = ModuleRegistry.Resolve(modules, page.Module);
                if (module is null)
                {
                    (module, var created) = await registry.EnsureAsync(group.Key, page.Module, page.Summary, cancellationToken: cancellationToken);
                    if (created) modules = await registry.ListAsync(group.Key, includeMerged: true, cancellationToken);
                }
                var labels = string.Join(", ", MemoryLabels.Keywords(MemoryLabels.SplitCsv(page.Labels)));
                if (labels.Length > 500) labels = labels[..500];
                if (module == page.Module && labels == page.Labels) continue;

                await using var business = await businessStore.CreateAsync(cancellationToken);
                var id = page.Id;
                await business.BusinessPages.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, module).SetProperty(p => p.Labels, labels), cancellationToken);
                changed++;
            }
        }
        if (changed > 0) activityLog.Info("memory", $"Migrasi label: modul dan label {changed} halaman wiki bisnis dinormalisasi.");
    }

    // F2b: the projects registered here whose key has no code map yet.
    private async Task ScanProjectsAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, string Key, string Name)> projects;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            projects = (await db.Projects.AsNoTracking().Where(p => p.ProjectKey != "").Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(cancellationToken))
                .Select(p => (p.Id, p.ProjectKey, p.Name)).ToList();

        foreach (var project in projects.DistinctBy(p => p.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await codeMapper.HasEntriesAsync(project.Key, cancellationToken)) continue;
                var context = await projectService.GetContextAsync(project.Id, cancellationToken);
                if (context is null) continue;
                var (files, mapped) = await codeMapper.ScanAsync(context, ScanMaxFiles, cancellationToken);
                if (files > 0) activityLog.Info("memory", $"Peta kode {project.Name}: {mapped} dari {files} file dipetakan ke modul.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Code map scan of {Project} failed.", project.Name);
            }
        }
    }

    // F3. Returns the counts and the keys whose memory changed.
    private async Task<(int Labeled, int Reviewed, HashSet<string> Keys)> RelabelAsync(CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!await ai.IsAvailableAsync(cancellationToken))
        {
            _lastMessage = "Migrasi label menunggu: belum ada node Tools node, Wrapper, atau Planner yang aktif.";
            return (0, 0, keys);
        }

        var projects = await ProjectsAsync(cancellationToken);
        int labeled = 0, reviewed = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var candidates = await ClaimAsync(projects, cancellationToken);
            if (candidates.Count == 0) break;

            foreach (var group in candidates.GroupBy(c => c.Key))
            {
                var (l, r) = await LabelGroupAsync(group.Key, group.ToList(), projects, cancellationToken);
                labeled += l;
                reviewed += r;
                if (group.Key is { } key) keys.Add(key);
            }
            Publish("relabel");
        }
        return (labeled, reviewed, keys);
    }

    // Claims up to BatchSize rows (long memory first): a row is only taken when its attempt count is still the one
    // read, so two machines never take the same row.
    private async Task<List<Candidate>> ClaimAsync(IReadOnlyDictionary<Guid, (string? Key, string Name)> projects, CancellationToken cancellationToken)
    {
        var claimed = new List<Candidate>();
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await db.LongMemories.AsNoTracking()
                .Where(m => m.LabelVersion == null && m.ReviewReason == null && (m.RelabelAttempts == null || m.RelabelAttempts < MaxAttempts))
                .OrderBy(m => m.ProjectKey).ThenBy(m => m.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var attempts = row.RelabelAttempts;
                var id = row.Id;
                var taken = await db.LongMemories
                    .Where(m => m.Id == id && m.LabelVersion == null && m.RelabelAttempts == attempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.RelabelAttempts, (attempts ?? 0) + 1), cancellationToken);
                if (taken == 1)
                    claimed.Add(new Candidate(row.Id, MemoryLevel.Long, row.Type, KeyOf(row.Type, row.ProjectKey, row.ProjectId, projects),
                        row.ProjectName, row.Label, row.Description, (attempts ?? 0) + 1));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not claim long memories for relabeling");
        }

        if (claimed.Count < BatchSize)
        {
            foreach (var record in (await ShortRecordsAsync())
                         .Where(r => r.LabelVersion is null && r.ReviewReason is null && (r.RelabelAttempts ?? 0) < MaxAttempts)
                         .Take(BatchSize - claimed.Count))
            {
                var attempts = (record.RelabelAttempts ?? 0) + 1;
                try
                {
                    await shortStore.SaveAsync(record with { RelabelAttempts = attempts });
                    claimed.Add(new Candidate(record.Id, MemoryLevel.Short, record.Type, KeyOf(record.Type, record.ProjectKey, record.ProjectId, projects),
                        record.ProjectName, record.Label, record.Description, attempts));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not claim short memory {Id} for relabeling.", record.Id);
                }
            }
        }
        return claimed;
    }

    private async Task<(int Labeled, int Reviewed)> LabelGroupAsync(string? key, List<Candidate> candidates,
        IReadOnlyDictionary<Guid, (string? Key, string Name)> projects, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            var reviewed = 0;
            foreach (var candidate in candidates)
                if (await SaveReviewAsync(candidate, "Project memory tanpa project key dan tanpa project yang terdaftar.", cancellationToken)) reviewed++;
            return (0, reviewed);
        }

        var knowledge = key == MemoryLabels.KnowledgeKey;
        var modules = await registry.ListAsync(key, cancellationToken: cancellationToken);
        var knowledgeModules = knowledge ? modules : await registry.ListAsync(MemoryLabels.KnowledgeKey, cancellationToken: cancellationToken);
        var hints = new Dictionary<Guid, IReadOnlyList<CodeMapEntry>>();
        foreach (var candidate in candidates)
            hints[candidate.Id] = knowledge ? [] : await codeMapper.LookupAsync(key, candidate.Description, 8, cancellationToken);

        var ids = candidates.Select((c, i) => (Short: $"M{i + 1}", c)).ToDictionary(x => x.Short, x => x.c, StringComparer.OrdinalIgnoreCase);
        JsonNode? json = null;
        string? failure = null;
        try
        {
            (json, _) = await ai.AskJsonAsync(AgentPrompts.MemoryRelabeler, Input(key, candidates.First().ProjectName, modules, knowledgeModules, ids, hints), MaxTokens, cancellationToken);
            if (json is null) failure = "model tidak menjawab JSON";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex.Message;
            logger.LogWarning(ex, "Relabel model call for {Key} failed.", key);
        }

        var answers = (json?["memories"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(o => SkillAi.Text(o["id"]) is not null)
            .GroupBy(o => SkillAi.Text(o["id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int labeledCount = 0, reviewedCount = 0;
        foreach (var (shortId, candidate) in ids)
        {
            answers.TryGetValue(shortId, out var answer);
            if (answer is not null && SkillAi.Text(answer["review"]) is { } review)
            {
                if (await SaveReviewAsync(candidate, review, cancellationToken)) reviewedCount++;
                continue;
            }

            var label = answer is null ? null : await BuildLabelAsync(key, candidate, answer, hints[candidate.Id], cancellationToken);
            if (label is not null)
            {
                if (await SaveLabelAsync(candidate, label, cancellationToken)) labeledCount++;
            }
            else if (candidate.Attempts >= MaxAttempts)
            {
                var reason = $"Gagal dilabeli otomatis {MaxAttempts} kali" + (failure is null ? "." : $": {failure}");
                if (await SaveReviewAsync(candidate, reason, cancellationToken)) reviewedCount++;
            }
        }
        return (labeledCount, reviewedCount);
    }

    private async Task<NewLabel?> BuildLabelAsync(string key, Candidate candidate, JsonObject answer, IReadOnlyList<CodeMapEntry> hints, CancellationToken cancellationToken)
    {
        var moduleText = SkillAi.Text(answer["module"]);
        if (moduleText is null || !MemoryLabels.TryParseKind(SkillAi.Text(answer["kind"]), out var kind)) return null;

        var type = candidate.Key == MemoryLabels.KnowledgeKey ? MemoryType.Knowledge : candidate.Type;
        if (!MemoryLabels.KindAllowedFor(type, kind)) kind = MemoryKind.Pattern;
        var (module, _) = await registry.EnsureAsync(key, moduleText, cancellationToken: cancellationToken);

        var layers = MemoryAi.Strings(answer["layers"])
            .Select(l => MemoryLabels.TryParseLayer(l, out var layer) ? layer : (MemoryLayer?)null)
            .OfType<MemoryLayer>().Distinct().Order().ToList();
        var ofModule = hints.Where(h => string.Equals(h.Module, module, StringComparison.OrdinalIgnoreCase)).ToList();
        if (layers.Count == 0) layers = ofModule.Select(h => h.Layer).Distinct().Order().ToList();
        var areas = ofModule.Select(h => h.Area).Distinct().ToList();

        var keywords = MemoryLabels.Keywords(MemoryAi.Strings(answer["keywords"]), module);
        if (keywords.Count == 0) keywords = MemoryLabels.Keywords(MemoryLabels.SplitCsv(candidate.Label), module);
        if (keywords.Count == 0) return null;
        return new NewLabel(module, layers, kind, areas is [{ } area] ? area : null, keywords);
    }

    private async Task<bool> SaveLabelAsync(Candidate candidate, NewLabel label, CancellationToken cancellationToken)
    {
        var composed = MemoryLabels.Compose(MemoryLabels.Display(label.Area, label.Module, label.Layers, label.Kind), label.Keywords);
        try
        {
            if (candidate.Level == MemoryLevel.Short)
            {
                if (await shortStore.GetAsync(candidate.Id) is not { } record) return false;
                await shortStore.SaveAsync(record with
                {
                    Label = composed, LegacyLabel = record.LegacyLabel ?? record.Label, Module = label.Module, Layers = MemoryLabels.LayersCsv(label.Layers),
                    Kind = label.Kind, Area = label.Area, Keywords = MemoryLabels.JoinCsv(label.Keywords), LabelVersion = MemoryLabels.CurrentVersion,
                    ReviewReason = null
                });
                return true;
            }

            await using var db = await longStore.CreateAsync(cancellationToken);
            var row = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == candidate.Id && m.LabelVersion == null, cancellationToken);
            if (row is null) return false;
            row.LegacyLabel ??= row.Label;
            row.Label = composed;
            row.Module = label.Module;
            row.Layers = MemoryLabels.LayersCsv(label.Layers);
            row.Kind = label.Kind;
            row.Area = label.Area;
            row.Keywords = MemoryLabels.JoinCsv(label.Keywords);
            row.LabelVersion = MemoryLabels.CurrentVersion;
            row.ReviewReason = null;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not save the new label of memory {Id}.", candidate.Id);
            return false;
        }
    }

    private async Task<bool> SaveReviewAsync(Candidate candidate, string reason, CancellationToken cancellationToken)
    {
        var clipped = reason.Length > 500 ? reason[..500] : reason;
        try
        {
            if (candidate.Level == MemoryLevel.Short)
            {
                if (await shortStore.GetAsync(candidate.Id) is not { } record) return false;
                await shortStore.SaveAsync(record with { ReviewReason = clipped });
                return true;
            }

            await using var db = await longStore.CreateAsync(cancellationToken);
            var id = candidate.Id;
            return await db.LongMemories.Where(m => m.Id == id && m.LabelVersion == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReviewReason, clipped), cancellationToken) > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not mark memory {Id} for review.", candidate.Id);
            return false;
        }
    }

    // F4: a full duplicate check of the keys whose memory was just relabeled (with the duplicate memory report), and
    // for every other key only the modules created since the last pass. Nothing is re-checked on a plain restart.
    private async Task CheckModulesAsync(IReadOnlySet<string> relabeledKeys, CancellationToken cancellationToken)
    {
        var created = registry.DrainCreated();
        var keys = new HashSet<string>(created.Keys, StringComparer.Ordinal);
        keys.UnionWith(relabeledKeys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (relabeledKeys.Contains(key))
                {
                    var found = await merger.CheckAsync(key, null, cancellationToken);
                    var duplicates = await CountDuplicateMemoriesAsync(key, cancellationToken);
                    if (found > 0 || duplicates > 0)
                        activityLog.Info("memory", $"Cek modul {key}: {found} modul ganda ditangani, {duplicates} pasang kandidat memory duplikat (lihat Memory → Perlu review).");
                }
                else if (created.TryGetValue(key, out var modules))
                {
                    await merger.CheckAsync(key, modules, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Module check of {Key} failed.", key);
            }
        }
    }

    private async Task<int> CountDuplicateMemoriesAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await longStore.CreateAsync(cancellationToken);
        var knowledge = key == MemoryLabels.KnowledgeKey;
        var rows = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
            .Where(m => m.LabelVersion != null && m.Module != null && m.Kind != null)
            .Select(m => new { m.Id, m.Type, m.Label, m.CreatedAt, m.Module, m.Kind, m.Keywords })
            .ToListAsync(cancellationToken);
        var entries = rows.Select(r => new MemoryIndexEntry(r.Id, MemoryLevel.Long, r.Type, r.Label, r.CreatedAt, r.Module, null, r.Kind, null,
            MemoryLabels.SplitCsv(r.Keywords))).ToList();
        return MemoryRanker.FindDuplicates(entries).Count;
    }

    private static string Input(string key, string? projectName, IReadOnlyList<ProjectModule> modules, IReadOnlyList<ProjectModule> knowledgeModules,
        IReadOnlyDictionary<string, Candidate> ids, IReadOnlyDictionary<Guid, IReadOnlyList<CodeMapEntry>> hints)
    {
        var builder = new StringBuilder();
        builder.Append("# Project\n").Append(key == MemoryLabels.KnowledgeKey ? "(knowledge memory, not tied to a project)" : projectName ?? key).Append("\n\n");
        builder.Append("# Modules\n");
        AppendModules(builder, key == MemoryLabels.KnowledgeKey ? [] : modules);
        builder.Append("\n# Knowledge modules\n");
        AppendModules(builder, knowledgeModules);
        builder.Append("\n# Memories\n");
        foreach (var (shortId, candidate) in ids)
        {
            builder.Append("## ").Append(shortId).Append(" type=").Append(candidate.Key == MemoryLabels.KnowledgeKey ? "knowledge" : "project").Append('\n');
            builder.Append("old label: ").Append(candidate.Label).Append('\n');
            builder.Append("description: ").Append(candidate.Description.Length > 2000 ? candidate.Description[..2000] : candidate.Description).Append('\n');
            if (hints.TryGetValue(candidate.Id, out var list) && list.Count > 0)
                builder.Append("code map: ").Append(string.Join("; ", list.Select(h => $"{h.Path}: {h.Module} · {h.Layer}"))).Append('\n');
        }
        return builder.ToString();
    }

    private static void AppendModules(StringBuilder builder, IReadOnlyList<ProjectModule> modules)
    {
        if (modules.Count == 0) builder.Append("(none yet)\n");
        foreach (var module in modules.Take(200))
        {
            builder.Append("- ").Append(module.Name);
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append(" (aliases: ").Append(module.Aliases.Replace(",", ", ")).Append(')');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append(": ").Append(module.Description);
            builder.Append('\n');
        }
    }

    // Registry key of a memory: knowledge, its project key, or the key of its project registered here.
    private static string? KeyOf(MemoryType type, string? projectKey, Guid? projectId, IReadOnlyDictionary<Guid, (string? Key, string Name)> projects) =>
        type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey
        : !string.IsNullOrEmpty(projectKey) ? projectKey
        : projectId is { } id && projects.TryGetValue(id, out var project) && !string.IsNullOrEmpty(project.Key) ? project.Key
        : null;

    private async Task<IReadOnlyDictionary<Guid, (string? Key, string Name)>> ProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => ((string?)p.ProjectKey, p.Name));
    }

    private async Task<IReadOnlyList<ShortMemoryRecord>> ShortRecordsAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories for relabeling.");
            return [];
        }
    }

    private void Publish(string action) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action }));

    private sealed record Candidate(Guid Id, MemoryLevel Level, MemoryType Type, string? Key, string? ProjectName, string Label, string Description, int Attempts);

    private sealed record NewLabel(string Module, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind, string? Area, IReadOnlyList<string> Keywords);
}

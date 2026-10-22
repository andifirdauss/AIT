using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Data.Cache;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    public async Task<MemoryImportResultDto> ImportAsync(MemoryExportFile file, MemoryImportMode mode, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Format != MemoryExportFile.FormatName)
            throw new AppValidationException($"Bukan file export memory AIT (format harus \"{MemoryExportFile.FormatName}\").");
        if (file.Version > MemoryExportFile.CurrentVersion)
            throw new AppValidationException($"File dibuat oleh AIT yang lebih baru (format v{file.Version}) — perbarui AIT dulu.");
        if (!Enum.IsDefined(mode)) throw new AppValidationException("Mode import tidak valid.");

        var items = file.Items ?? [];
        if (items.Count > MaxItems) throw new AppValidationException($"File berisi {items.Count} memory; maksimal {MaxItems} per import.");

        var projects = await LocalProjectsAsync(cancellationToken);
        var shortConnected = shortStore.IsConnected;
        var existingShort = shortConnected ? (await shortStore.GetAllAsync()).ToDictionary(r => r.Id) : new Dictionary<Guid, ShortMemoryRecord>();

        List<ExistingLong> existingLong;
        await using (var db = await longStore.CreateAsync(cancellationToken))
        {
            existingLong = await db.LongMemories.AsNoTracking()
                .Select(m => new ExistingLong(m.Id, m.ProjectKey, m.ProjectId, m.Type, m.Label, m.Description))
                .ToListAsync(cancellationToken);
        }
        var existingLongIds = existingLong.Select(m => m.Id).ToHashSet();

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in existingShort.Values) fingerprints.Add(Fingerprint(ScopeOf(projects, r.Type, r.ProjectKey, r.ProjectId), r.Type, r.Label, r.Description));
        foreach (var m in existingLong) fingerprints.Add(Fingerprint(ScopeOf(projects, m.Type, m.ProjectKey, m.ProjectId), m.Type, m.Label, m.Description));

        var seenIds = new HashSet<Guid>();
        var adds = new List<Prepared>();
        var updates = new List<Prepared>();
        var invalid = new List<string>();
        int skipped = 0, duplicates = 0, orphaned = 0, foreignLocal = 0;
        var keys = new Dictionary<string, (string? Name, int Count, bool Registered)>(StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Invalid(item) is { } reason)
            {
                invalid.Add($"Memory #{i + 1}: {reason}");
                continue;
            }

            var prepared = Prepare(item, projects);
            if (prepared.Orphaned) orphaned++;
            if (prepared.Key is { } key)
            {
                var registered = projects.FindByKey(key) is not null;
                if (!registered && ProjectKeyResolver.IsLocal(key)) foreignLocal++;
                var entry = keys.GetValueOrDefault(key, (Name: prepared.Name, Count: 0, Registered: registered));
                keys[key] = (entry.Name ?? prepared.Name, entry.Count + 1, registered);
            }

            if (!seenIds.Add(item.Id))
            {
                duplicates++;
                continue;
            }

            var fingerprint = Fingerprint(prepared.Key ?? prepared.ProjectId?.ToString(), prepared.Type, prepared.Label, prepared.Description);
            if (existingShort.ContainsKey(item.Id) || existingLongIds.Contains(item.Id))
            {
                if (mode == MemoryImportMode.Overwrite) updates.Add(prepared);
                else skipped++;
            }
            else if (!fingerprints.Add(fingerprint))
            {
                // Same memory under another id, e.g. from a file exported on another machine.
                duplicates++;
            }
            else
            {
                adds.Add(prepared);
            }
        }

        var warnings = invalid.Take(MaxListedWarnings).ToList();
        if (invalid.Count > MaxListedWarnings) warnings.Add($"… dan {invalid.Count - MaxListedWarnings} memory tidak valid lainnya.");
        if (orphaned > 0) warnings.Add($"{orphaned} project memory tanpa project key dan tanpa project yang terdaftar di sini dijadikan knowledge memory.");
        if (foreignLocal > 0) warnings.Add($"{foreignLocal} memory memakai key lokal (local:…) mesin lain, jadi tidak cocok dengan project mana pun di sini. Beri project key bersama di mesin asal (remote git, key manual, atau .aithread/project.json), lalu ekspor ulang.");
        var shortToLong = shortConnected ? 0 : adds.Count(a => a.Level == MemoryLevel.Short);
        if (shortToLong > 0) warnings.Add($"Penyimpanan short memory tidak terhubung — {shortToLong} short memory disimpan langsung sebagai long memory.");

        var projectSummary = keys
            .Select(k => new MemoryImportProjectDto(k.Key, k.Value.Name, k.Value.Count, k.Value.Registered))
            .OrderByDescending(p => p.Count)
            .ToList();

        UnitTestImport unitTests;
        try
        {
            unitTests = await ImportUnitTestsAsync(file.UnitTests ?? [], dryRun, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unit test import stopped.");
            throw new AppValidationException($"Import unit test berhenti: {MemoryStoreTester.ErrorMessage(ex)}. Unit test yang sudah masuk tidak akan digandakan bila import diulang.");
        }
        warnings.AddRange(unitTests.Warnings.Take(MaxListedWarnings));
        if (unitTests.Renumbered > 0)
            warnings.Add($"{unitTests.Renumbered} unit test mendapat nomor baru karena nomornya sudah dipakai test lain di project yang sama.");
        var result = new MemoryImportResultDto(dryRun, items.Count, adds.Count, updates.Count, skipped, duplicates, invalid.Count, projectSummary, warnings,
            unitTests.Added, unitTests.Updated, unitTests.Renumbered, unitTests.Skipped);
        if (!dryRun && unitTests.Added + unitTests.Updated > 0)
            activityLog.Info("memory", $"Import unit test: {unitTests.Added} ditambahkan, {unitTests.Updated} diperbarui, {unitTests.Skipped} dilewati.");
        if (dryRun || (adds.Count == 0 && updates.Count == 0)) return result;

        try
        {
            await RegisterModulesAsync(adds.Concat(updates), cancellationToken);
            await WriteAsync(adds, updates, shortConnected, existingShort, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory import stopped.");
            throw new AppValidationException($"Import berhenti: {MemoryStoreTester.ErrorMessage(ex)}. Memory yang sudah masuk tidak akan digandakan bila import diulang.");
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "imported" }));
        activityLog.Info("memory", $"Import memory: {adds.Count} ditambahkan, {updates.Count} ditimpa, {skipped + duplicates} dilewati.");
        return result;
    }
}

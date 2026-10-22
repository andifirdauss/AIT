using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    // A filled sheet into its scenario. expected: the scenario the sheet must belong to (the project file of a slug); null
    // takes the id the sheet carries. A sheet with errors changes nothing. The version goes up only when the content
    // changed, so importing the same file again is harmless.
    private async Task<ScenarioImportResultDto> ImportSheetAsync(ProjectContext project, Stream content, Guid? expected, string by, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxSheetBytes) throw new AppValidationException($"File skenario maksimal {MaxSheetBytes / 1024 / 1024} MB.");
        buffer.Position = 0;

        var sheet = ScenarioSheet.Read(buffer);
        if (sheet.Errors.Count > 0)
            throw new AppValidationException("File skenario belum bisa dipakai:\n- " + string.Join("\n- ", sheet.Errors.Take(MaxListedErrors)) +
                                             (sheet.Errors.Count > MaxListedErrors ? $"\n- … {sheet.Errors.Count - MaxListedErrors} lainnya" : string.Empty));
        var id = expected ?? sheet.ScenarioId ?? throw new AppValidationException("File tidak menyebut skenario mana (sheet _AIT rusak).");
        if (expected is not null && sheet.ScenarioId is { } fileId && fileId != expected)
            throw new AppValidationException("File ini milik skenario lain; unduh ulang file skenario ini dari AIT.");

        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenario = await db.TestingScenarios.FirstOrDefaultAsync(s => s.Id == id && s.ProjectKey == key, cancellationToken)
                       ?? throw new AppValidationException("Skenario di file ini tidak ada di project ini (mungkin sudah dihapus).");

        var warnings = new List<string>();
        if (sheet.Version is { } version && version < scenario.Version)
            warnings.Add($"File dibuat dari versi {version}, sedangkan skenario sudah versi {scenario.Version}; isi file ini yang dipakai.");

        // New usernames become accounts of the project; existing ones are left as they are.
        var added = new List<string>();
        var set = await accounts.GetAsync(key, cancellationToken);
        foreach (var account in sheet.Accounts.Where(a => a.Username is not null))
        {
            if (set.Find(account.Username) is { } known)
            {
                if (!string.Equals(known.Role, account.Role, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Akun {known.Username} sudah ada dengan peran {known.Role ?? "–"}; untuk skenario ini dipakai sebagai {account.Role}.");
                continue;
            }
            try
            {
                await accounts.AddAsync(key, new SaveTestAccountRequest(account.Username!, account.Role, new Dictionary<string, string>(account.Attributes), null), by, cancellationToken);
                added.Add(account.Username!);
            }
            catch (AppValidationException ex)
            {
                warnings.Add($"Akun {account.Username}: {ex.Message}");
            }
        }

        var roles = ScenarioText.Write(sheet.Accounts.Select(a => new ScenarioRoleDto(a.Role, a.Username)).ToList());
        var steps = ScenarioText.Write(sheet.Steps);
        var fields = ScenarioText.Write(sheet.Fields);
        if (roles != scenario.Roles || steps != scenario.Steps || fields != scenario.Fields)
        {
            scenario.Roles = roles;
            scenario.Steps = steps;
            scenario.Fields = fields;
            scenario.Version++;
            scenario.UpdatedBy = by;
            scenario.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            activityLog.Info(LogCategory, $"Skenario test \"{scenario.Title}\" diperbarui dari file (versi {scenario.Version}, {sheet.Steps.Count} langkah)" +
                                          (added.Count == 0 ? "." : $"; akun baru: {string.Join(", ", added)}."), projectId: project.ProjectId);
        }
        return new ScenarioImportResultDto(ScenarioText.ToDto(scenario), added, warnings);
    }
}

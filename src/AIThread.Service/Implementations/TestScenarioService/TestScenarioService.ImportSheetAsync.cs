using AIThread.Common.DTOs;
using AIThread.Common.Localization;
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
        if (buffer.Length > MaxSheetBytes) throw new AppValidationException(Lang.T($"File skenario maksimal {MaxSheetBytes / 1024 / 1024} MB.", $"The scenario file is at most {MaxSheetBytes / 1024 / 1024} MB."));
        buffer.Position = 0;

        var sheet = ScenarioSheet.Read(buffer);
        if (sheet.Errors.Count > 0)
            throw new AppValidationException(Lang.T("File skenario belum bisa dipakai:\n- ", "The scenario file cannot be used yet:\n- ") + string.Join("\n- ", sheet.Errors.Take(MaxListedErrors)) +
                                             (sheet.Errors.Count > MaxListedErrors ? Lang.T($"\n- … {sheet.Errors.Count - MaxListedErrors} lainnya", $"\n- … {sheet.Errors.Count - MaxListedErrors} more") : string.Empty));
        var id = expected ?? sheet.ScenarioId ?? throw new AppValidationException(Lang.T("File tidak menyebut skenario mana (sheet _AIT rusak).", "The file does not say which scenario it is (the _AIT sheet is damaged)."));
        if (expected is not null && sheet.ScenarioId is { } fileId && fileId != expected)
            throw new AppValidationException(Lang.T("File ini milik skenario lain; unduh ulang file skenario ini dari AIT.", "This file belongs to another scenario; download this scenario's file from AIT again."));

        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenario = await db.TestingScenarios.FirstOrDefaultAsync(s => s.Id == id && s.ProjectKey == key, cancellationToken)
                       ?? throw new AppValidationException(Lang.T("Skenario di file ini tidak ada di project ini (mungkin sudah dihapus).", "The scenario in this file is not in this project (it may have been deleted)."));

        var warnings = new List<string>();
        if (sheet.Version is { } version && version < scenario.Version)
            warnings.Add(Lang.T($"File dibuat dari versi {version}, sedangkan skenario sudah versi {scenario.Version}; isi file ini yang dipakai.", $"The file was made from version {version}, but the scenario is already at version {scenario.Version}; the content of this file is used."));

        // New usernames become accounts of the project; existing ones are left as they are.
        var added = new List<string>();
        var set = await accounts.GetAsync(key, cancellationToken);
        foreach (var account in sheet.Accounts.Where(a => a.Username is not null))
        {
            if (set.Find(account.Username) is { } known)
            {
                if (!string.Equals(known.Role, account.Role, StringComparison.OrdinalIgnoreCase))
                    warnings.Add(Lang.T($"Akun {known.Username} sudah ada dengan peran {known.Role ?? "–"}; untuk skenario ini dipakai sebagai {account.Role}.", $"Account {known.Username} already exists with role {known.Role ?? "–"}; for this scenario it is used as {account.Role}."));
                continue;
            }
            try
            {
                await accounts.AddAsync(key, new SaveTestAccountRequest(account.Username!, account.Role, new Dictionary<string, string>(account.Attributes), null), by, cancellationToken);
                added.Add(account.Username!);
            }
            catch (AppValidationException ex)
            {
                warnings.Add(Lang.T($"Akun {account.Username}: {ex.Message}", $"Account {account.Username}: {ex.Message}"));
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
            activityLog.Info(LogCategory, Lang.T($"Skenario test \"{scenario.Title}\" diperbarui dari file (versi {scenario.Version}, {sheet.Steps.Count} langkah)", $"Test scenario \"{scenario.Title}\" updated from a file (version {scenario.Version}, {sheet.Steps.Count} steps)") +
                                          (added.Count == 0 ? "." : Lang.T($"; akun baru: {string.Join(", ", added)}.", $"; new accounts: {string.Join(", ", added)}.")), projectId: project.ProjectId);
        }
        return new ScenarioImportResultDto(ScenarioText.ToDto(scenario), added, warnings);
    }
}

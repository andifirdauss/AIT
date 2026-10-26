using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Entries of the project read when the module's own text does not tell its language.
    private const int LanguageSampleEntries = 20;

    // The language the summary is written in, decided by AIT rather than left to the model: that of the entries and
    // facts being folded, else of the project's latest change entries, else of the current summary, else the UI language.
    private static async Task<string> LanguageAsync(ILongMemoryDb db, string key, IEnumerable<string> texts, LongMemory? current,
        CancellationToken cancellationToken)
    {
        if (TextLanguage.Guess(texts) is { } language) return language;
        var recent = await db.LongMemories.AsNoTracking()
            .Where(m => m.ProjectKey == key && m.Kind == MemoryKind.Change)
            .OrderByDescending(m => m.CreatedAt)
            .Take(LanguageSampleEntries)
            .Select(m => m.Description)
            .ToListAsync(cancellationToken);
        return TextLanguage.Guess(recent) ?? TextLanguage.Guess([current?.Description]) ?? TextLanguage.Ui;
    }
}

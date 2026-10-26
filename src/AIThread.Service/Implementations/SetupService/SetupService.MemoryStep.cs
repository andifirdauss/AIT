using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Optional: a developer working alone keeps the built-in stores. Shared = long or business memory in an external
    // database, which is what a team needs.
    private static SetupStepDto MemoryStep(MemoryStorageSettingsDto? storage)
    {
        const string id = "memory";
        var title = Lang.T("Memory bersama tim", "Team-shared memory");
        if (storage is null) return new SetupStepDto(id, title, false, false, Lang.T("Status penyimpanan memory tidak tersedia.", "The memory storage status is not available."));

        var shared = storage.LongMemory.Provider != MemoryStoreProvider.Default || storage.BusinessMemory.Provider != MemoryStoreProvider.Default;
        return new SetupStepDto(id, title, false, shared, shared
            ? $"Long: {storage.LongStatus.Description} · Business: {storage.BusinessStatus.Description}."
            : Lang.T("Bawaan: memory hanya ada di komputer ini. Untuk tim, import profil dari ketua tim, atau arahkan Long & Business memory ke database bersama.", "Built-in: memory exists on this computer only. For a team, import the profile from the team lead, or point Long & Business memory to a shared database."));
    }
}

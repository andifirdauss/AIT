using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Optional: a developer working alone keeps the built-in stores. Shared = long or business memory in an external
    // database, which is what a team needs.
    private static SetupStepDto MemoryStep(MemoryStorageSettingsDto? storage)
    {
        const string id = "memory", title = "Memory bersama tim";
        if (storage is null) return new SetupStepDto(id, title, false, false, "Status penyimpanan memory tidak tersedia.");

        var shared = storage.LongMemory.Provider != MemoryStoreProvider.Default || storage.BusinessMemory.Provider != MemoryStoreProvider.Default;
        return new SetupStepDto(id, title, false, shared, shared
            ? $"Long: {storage.LongStatus.Description} · Business: {storage.BusinessStatus.Description}."
            : "Bawaan: memory hanya ada di komputer ini. Untuk tim, import profil dari ketua tim, atau arahkan Long & Business memory ke database bersama.");
    }
}

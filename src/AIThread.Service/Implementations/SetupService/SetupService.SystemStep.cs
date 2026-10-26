using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto SystemStep(bool databaseConnected, bool shortMemoryConnected, MemoryStorageSettingsDto? storage)
    {
        var shortName = storage?.ShortStatus.Description ?? "short memory";
        var detail = (databaseConnected, shortMemoryConnected) switch
        {
            (true, true) => Lang.T($"Database dan short memory ({shortName}) terhubung.", $"Database and short memory ({shortName}) connected."),
            (false, _) => Lang.T("Database tidak dapat diakses. Periksa ConnectionStrings:Default di appsettings.json (bawaan: LocalDB) lalu restart AIT.", "The database cannot be accessed. Check ConnectionStrings:Default in appsettings.json (default: LocalDB), then restart AIT."),
            _ => Lang.T($"Short memory tidak terhubung ({shortName}). Periksa Pengaturan → Penyimpanan memory.", $"Short memory is not connected ({shortName}). Check Setting → Memory storage.")
        };
        return new SetupStepDto("system", "Database & short memory", true, databaseConnected && shortMemoryConnected, detail);
    }
}

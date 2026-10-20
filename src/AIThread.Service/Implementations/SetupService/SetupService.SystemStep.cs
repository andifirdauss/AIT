using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto SystemStep(bool databaseConnected, bool shortMemoryConnected, MemoryStorageSettingsDto? storage)
    {
        var shortName = storage?.ShortStatus.Description ?? "short memory";
        var detail = (databaseConnected, shortMemoryConnected) switch
        {
            (true, true) => $"Database dan short memory ({shortName}) terhubung.",
            (false, _) => "Database tidak dapat diakses. Periksa ConnectionStrings:Default di appsettings.json (bawaan: LocalDB) lalu restart AIT.",
            _ => $"Short memory tidak terhubung ({shortName}). Periksa Setting → Penyimpanan memory."
        };
        return new SetupStepDto("system", "Database & short memory", true, databaseConnected && shortMemoryConnected, detail);
    }
}

using AiThread.Common.Entities;
using AiThread.Service.Models;

namespace AiThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private static BusinessPageConflictException Conflict(BusinessPage? current) => new(current is null
        ? "Halaman ini baru saja diubah orang lain. Muat ulang halaman, lalu terapkan perubahan Anda lagi."
        : $"Halaman ini sudah diubah {current.UpdatedBy ?? "orang lain"} (versi {current.Version}) sejak Anda membukanya. Muat ulang halaman, lalu terapkan perubahan Anda lagi.");
}

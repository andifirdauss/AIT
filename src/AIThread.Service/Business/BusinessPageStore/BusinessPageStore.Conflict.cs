using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private static BusinessPageConflictException Conflict(BusinessPage? current) => new(current is null
        ? Lang.T("Halaman ini baru saja diubah orang lain. Muat ulang halaman, lalu terapkan perubahan Anda lagi.", "Someone else just changed this page. Reload the page, then apply your changes again.")
        : Lang.T($"Halaman ini sudah diubah {current.UpdatedBy ?? "orang lain"} (versi {current.Version}) sejak Anda membukanya. Muat ulang halaman, lalu terapkan perubahan Anda lagi.", $"This page was changed by {current.UpdatedBy ?? "someone else"} (version {current.Version}) since you opened it. Reload the page, then apply your changes again."));
}

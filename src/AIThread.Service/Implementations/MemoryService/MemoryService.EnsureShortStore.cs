using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private void EnsureShortStore()
    {
        if (!shortStore.IsConnected)
            throw new AppValidationException(Lang.T($"Penyimpanan short memory ({shortStore.Description}) tidak terhubung — short memory tidak dapat diakses.", $"The short memory storage ({shortStore.Description}) is not connected — short memory cannot be accessed."));
    }
}

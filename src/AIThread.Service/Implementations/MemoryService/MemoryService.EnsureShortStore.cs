using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private void EnsureShortStore()
    {
        if (!shortStore.IsConnected)
            throw new AppValidationException($"Penyimpanan short memory ({shortStore.Description}) tidak terhubung — short memory tidak dapat diakses.");
    }
}

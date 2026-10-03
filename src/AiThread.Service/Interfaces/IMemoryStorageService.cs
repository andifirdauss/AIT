using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// Where short and long memory are kept (menu Setting → Penyimpanan memory).
public interface IMemoryStorageService
{
    Task<MemoryStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    // Refuses a store that cannot be reached; creates the memory table in a new store and switches to it right away.
    Task<MemoryStorageUpdateResultDto> UpdateAsync(UpdateMemoryStorageRequest request, CancellationToken cancellationToken = default);

    // Uses the form values without saving them; an empty password falls back to the saved one.
    Task<MemoryStoreTestResultDto> TestAsync(TestMemoryStoreRequest request, CancellationToken cancellationToken = default);

    // Puts the saved stores in use. Called once at startup.
    Task ApplySavedAsync(CancellationToken cancellationToken = default);
}

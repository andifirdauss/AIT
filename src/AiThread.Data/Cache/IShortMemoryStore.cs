namespace AiThread.Data.Cache;

public interface IShortMemoryStore
{
    bool IsConnected { get; }

    Task SaveAsync(ShortMemoryRecord record);
    Task<ShortMemoryRecord?> GetAsync(Guid id);
    Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync();
    Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc);
    Task<bool> DeleteAsync(Guid id);
}

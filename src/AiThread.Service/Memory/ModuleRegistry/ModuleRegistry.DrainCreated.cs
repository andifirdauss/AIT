namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // Modules created since the last call, per key.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DrainCreated()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (_created.TryDequeue(out var item))
        {
            if (!result.TryGetValue(item.Key, out var list)) result[item.Key] = list = [];
            if (!list.Contains(item.Module)) list.Add(item.Module);
        }
        return result.ToDictionary(r => r.Key, r => (IReadOnlyList<string>)r.Value);
    }
}

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The values back in place of their placeholders; unknown placeholders stay and are listed.
    public SecretRestore Restore(string text)
    {
        if (string.IsNullOrEmpty(text)) return new SecretRestore(text, 0, []);
        var unknown = new List<string>();
        var restored = 0;
        var result = SecretRules.ReplacePlaceholders(text, (placeholder, id) =>
        {
            if (_values.TryGetValue(id, out var value))
            {
                restored++;
                return value;
            }
            unknown.Add(placeholder);
            return placeholder;
        });
        if (restored > 0) Interlocked.Add(ref _restored, restored);
        return new SecretRestore(result, restored, unknown);
    }
}

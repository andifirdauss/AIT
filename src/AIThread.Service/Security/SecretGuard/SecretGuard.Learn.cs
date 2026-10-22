namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Remembers the secrets of a file without counting them as masked, so the placeholders a model copied from it
    // (possibly before a restart) can be written back. Runs whether masking is on or not.
    public void Learn(string path, string content, IReadOnlyCollection<string>? extraKeys = null)
    {
        if (string.IsNullOrEmpty(content)) return;
        SecretRules.Redact(content, SecretRules.IsConfigFile(path), extraKeys, (label, value) => Mask(label, value, count: false));
    }
}

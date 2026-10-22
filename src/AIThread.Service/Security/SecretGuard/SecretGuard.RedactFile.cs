namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // A file's content (or part of it): config files (.env, .yml, .ini, .properties, ...) also get their unquoted
    // "key: value" lines masked. extraKeys: the project's own secret key names (SecretPolicy.Keys).
    public string RedactFile(string path, string content, IReadOnlyCollection<string>? extraKeys = null) =>
        !_enabled || string.IsNullOrEmpty(content)
            ? content
            : MaskKnown(SecretRules.Redact(content, SecretRules.IsConfigFile(path), extraKeys, (label, value) => Mask(label, value, count: true)), count: true);
}

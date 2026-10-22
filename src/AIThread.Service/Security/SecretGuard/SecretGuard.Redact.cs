namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Any text on its way to a model or into storage: prompts, tool output, logs. Config-file rules stay off here,
    // since "key: value" in prose or code is rarely a secret (RedactFile knows the file).
    public string Redact(string text) =>
        !_enabled || string.IsNullOrEmpty(text)
            ? text
            : MaskKnown(SecretRules.Redact(text, config: false, null, (label, value) => Mask(label, value, count: true)), count: true);
}

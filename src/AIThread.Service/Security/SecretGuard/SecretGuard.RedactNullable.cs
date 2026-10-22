namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    public string? RedactNullable(string? text) => text is null ? null : Redact(text);
}

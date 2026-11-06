namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    public string Protect(string label, string value) =>
        string.IsNullOrEmpty(value) ? value : Mask(label, value, count: true);
}

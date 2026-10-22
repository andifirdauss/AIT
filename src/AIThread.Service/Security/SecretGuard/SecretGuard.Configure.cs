namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // From the settings (SettingService, SecurityService). Off: nothing is masked any more; placeholders already sent
    // still come back.
    public void Configure(bool enabled, bool maskPassthrough)
    {
        _enabled = enabled;
        _maskPassthrough = maskPassthrough;
    }
}

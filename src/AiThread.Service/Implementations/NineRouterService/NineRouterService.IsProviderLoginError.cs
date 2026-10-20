namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // 9router passes the provider's own failure on inside its message, e.g. HTTP 503 with "[claude/claude-opus-5-5]
    // [401]: {"type":"error","error":{"type":"authentication_error","message":"OAuth access token has expired. ..."}}".
    public static bool IsProviderLoginError(string error) => ProviderLoginPattern().IsMatch(error);
}

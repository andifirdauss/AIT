namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";
}

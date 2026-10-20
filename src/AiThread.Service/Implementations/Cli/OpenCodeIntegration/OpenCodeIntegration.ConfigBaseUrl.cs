namespace AiThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";
}

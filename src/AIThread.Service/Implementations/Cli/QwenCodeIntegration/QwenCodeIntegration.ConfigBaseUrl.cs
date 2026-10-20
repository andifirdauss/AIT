namespace AIThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";
}

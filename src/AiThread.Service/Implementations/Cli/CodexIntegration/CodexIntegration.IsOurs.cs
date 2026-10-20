namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    // model_providers.aithread / profiles.aithread, or a key below either.
    private static bool IsOurs(string dotted) =>
        new[] { $"model_providers.{ProviderId}", $"profiles.{ProviderId}" }.Any(t => dotted == t || dotted.StartsWith(t + "."));
}

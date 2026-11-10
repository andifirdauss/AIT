using System.Security.Cryptography;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // What a warm process is fixed to when it starts: the program, the model and the system prompt.
    private static string WarmKey(CliLaunch launch, string model, string system) =>
        Convert.ToHexString(SHA256.HashData(CliProcess.Utf8.GetBytes($"{launch.FileName}\n{string.Join('\n', launch.Prefix)}\n{model}\n{system}")));
}

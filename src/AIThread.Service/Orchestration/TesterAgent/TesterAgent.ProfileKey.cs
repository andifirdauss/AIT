using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // A short, file-name safe stand-in for a project key in browser profile names.
    private static string ProfileKey(string projectKey) =>
        string.IsNullOrEmpty(projectKey) ? "local" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectKey)))[..10].ToLowerInvariant();
}

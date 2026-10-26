using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // The same id on every machine, so two machines sharing a store cannot both create the summary of a module.
    private static Guid SummaryId(string key, string module)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"summary\n{key}\n{module.ToLowerInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}

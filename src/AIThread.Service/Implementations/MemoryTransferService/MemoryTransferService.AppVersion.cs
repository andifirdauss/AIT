using System.Reflection;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? AppVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
}

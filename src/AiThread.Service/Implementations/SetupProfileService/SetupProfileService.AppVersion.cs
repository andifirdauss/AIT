using System.Reflection;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    private static string? AppVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
}

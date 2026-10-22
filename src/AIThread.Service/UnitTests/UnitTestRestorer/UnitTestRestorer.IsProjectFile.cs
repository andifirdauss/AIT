namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    private static bool IsProjectFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj";
}

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    private static string Full(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}

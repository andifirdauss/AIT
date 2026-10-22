namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestExecutor
{
    private static string Tail(string output) => output.Length <= MaxSuiteMessageChars ? output : "…" + output[^MaxSuiteMessageChars..];
}

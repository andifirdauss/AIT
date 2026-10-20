namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
